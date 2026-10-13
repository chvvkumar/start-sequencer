using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using GongSolutions.Wpf.DragDrop;
using StartSequencer.Discovery;
using StartSequencer.Launch;
using StartSequencer.Model;
using StartSequencer.Sys;

namespace StartSequencer.UI;

public sealed class SaveReport { public bool Ok; public List<string> Errors = []; public bool Cancelled; }

public sealed class MainViewModel : INotifyPropertyChanged, IDropTarget
{
    public const string NativeGroup = "Native";

    // Saved state used for dirty tracking: managed and custom entries by key, stages, global settings.
    private Dictionary<string, string> _savedManaged = new();
    private Dictionary<string, string> _savedCustom = new();
    private string _savedStages = "";
    private string _savedSettings = "";
    private Dictionary<string, StageGroup> _groups = new();
    private string? _notice;

    public MainViewModel(AppConfig config)
    {
        Config = config;
        Rebuild(keepEdits: false);
        MarkSaved();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EntryRow> Rows { get; } = [];
    public ObservableCollection<string> StageNames { get; } = [];
    public AppConfig Config { get; private set; }
    public int DirtyCount { get; private set; }
    public bool IsDirty => DirtyCount > 0;
    public string DirtyText => DirtyCount == 1 ? "1 unsaved change" : $"{DirtyCount} unsaved changes";
    public BootLog? LastBoot { get; private set; }
    public IReadOnlyList<string> ScanErrors { get; private set; } = [];
    public string ScanErrorText => string.Join("\n", ScanErrors);
    public bool HasScanErrors => ScanErrors.Count > 0;
    public bool IsEmpty => Rows.Count == 0;

    // One axis for every timeline bar: rows, stage headers and the status line.
    public double TimelineTotalSeconds { get; private set; } = 1;
    public double PlannedTotalSeconds { get; private set; }
    public double? BootActualSeconds => LastBoot?.TotalSeconds;
    public LaunchResult? BootResult => LastBoot == null ? null : BootFailures().Count > 0 ? LaunchResult.Failed : LaunchResult.Started;

    // Shown in an InfoBar: run-now failures and other messages that are not save errors.
    public string? Notice { get => _notice; set { _notice = value; Raise(); Raise(nameof(HasNotice)); } }
    public bool HasNotice => !string.IsNullOrEmpty(_notice);

    public string StatusLine
    {
        get
        {
            if (LastBoot == null) return "No boot log yet";
            var entries = LastBoot.Entries.Where(e => e.Key != "Launcher").ToList();
            var failed = BootFailures().Count(e => e.Key != "Launcher");
            var text = $"Last boot: {entries.Count} {(entries.Count == 1 ? "entry" : "entries")} in {LastBoot.TotalSeconds:0} s";
            if (failed > 0) text += $", {failed} failed";
            var launcher = LastBoot.Entries.FirstOrDefault(e => e.Key == "Launcher");
            if (launcher != null) text += ". Launcher error: " + launcher.Reason;
            return text;
        }
    }

    public int GlobalDelaySeconds { get => Config.GlobalDelaySeconds; set { Config.GlobalDelaySeconds = Math.Max(0, value); Changed(); } }
    public int AskCountdownSeconds { get => Config.AskCountdownSeconds; set { Config.AskCountdownSeconds = Math.Max(1, value); Changed(); } }

    public bool ShowWindowsTasks
    {
        get => Config.ShowWindowsTasks;
        set { if (Config.ShowWindowsTasks == value) return; Config.ShowWindowsTasks = value; Rescan(); }
    }

    public StageGroup? Group(string name) => _groups.GetValueOrDefault(name);

    // ---- scan ----

    public void Rescan() => Rebuild(keepEdits: true);

    private void Rebuild(bool keepEdits)
    {
        var kept = keepEdits ? Rows.Where(r => !r.IsManaged).ToDictionary(r => r.Entry.Key, r => r.NativeEnabled) : null;
        var scan = Scanner.Scan(Config);
        ScanErrors = scan.Errors;
        try { LastBoot = BootLog.ReadLatest(); } catch { LastBoot = null; }
        var actual = new Dictionary<string, BootLogEntry>();
        foreach (var b in LastBoot?.Entries ?? []) actual[b.Key] = b;

        Rows.Clear();
        for (var i = 0; i < scan.Entries.Count; i++)
        {
            var e = scan.Entries[i];
            var managed = Config.Managed.FirstOrDefault(m => m.Key == e.Key);
            var native = kept != null && kept.TryGetValue(e.Key, out var k) ? k
                : managed != null ? Config.NativeSnapshot.GetValueOrDefault(e.Key, true)
                : e.Enabled;
            var row = new EntryRow(e, managed, native, i, Changed);
            if (actual.TryGetValue(e.Key, out var b) && b.LaunchedAt is { } at && LastBoot != null)
            {
                row.ActualOffset = (at - LastBoot.LogonTime).TotalSeconds;
                row.LastResult = b.Result == LaunchResult.Exited && b.ExitCode is not (null or 0) ? LaunchResult.Failed : b.Result;
            }
            row.ShownGroup = row.GroupName;
            Rows.Add(row);
        }

        RebuildGroups();
        StageNames.Clear();
        foreach (var s in Config.Stages) StageNames.Add(s.Name);
        Order();
        Changed();
        Raise(null);
    }

    private void RebuildGroups()
    {
        _groups = Config.Stages.ToDictionary(s => s.Name, s => new StageGroup(s, Changed));
        _groups[NativeGroup] = new StageGroup(null, Changed);
    }

    // ---- sequence edits ----

    public void Manage(EntryRow row, string stage, int index)
    {
        if (row.IsManaged) { Move(row, stage, index); return; }
        if (!row.CanDrag) throw new InvalidOperationException(row.Entry.Name + " cannot be sequenced");
        RequireStage(stage);
        // The On toggle keeps its value: an entry that was off does not start at the next logon.
        var m = new ManagedEntry { Key = row.Entry.Key, Stage = stage, Enabled = row.NativeEnabled };
        Config.Managed.Add(m);
        row.Managed = m;
        Place(m, stage, index);
    }

    public void Move(EntryRow row, string stage, int index)
    {
        if (row.Managed is not { } m) { Manage(row, stage, index); return; }
        RequireStage(stage);
        m.Stage = stage;
        row.Managed = m;   // raises GroupName
        Place(m, stage, index);
    }

    public void Unmanage(EntryRow row)
    {
        if (row.Managed is not { } m) return;
        Config.Managed.Remove(m);
        row.NativeEnabled = Config.NativeSnapshot.GetValueOrDefault(row.Entry.Key, true);
        row.Managed = null;
        Renumber();
        Order();
        Changed();
    }

    private void Place(ManagedEntry m, string stage, int index)
    {
        var list = Config.Managed.Where(x => x.Stage == stage && x != m).OrderBy(x => x.Order).ToList();
        list.Insert(Math.Clamp(index, 0, list.Count), m);
        for (var i = 0; i < list.Count; i++) list[i].Order = i;
        Renumber();
        Order();
        Changed();
    }

    private void Renumber()
    {
        foreach (var g in Config.Managed.GroupBy(x => x.Stage))
        {
            var i = 0;
            foreach (var x in g.OrderBy(x => x.Order)) x.Order = i++;
        }
    }

    private void RequireStage(string stage)
    {
        if (!Config.Stages.Any(s => s.Name == stage)) throw new ArgumentException("No stage named " + stage);
    }

    // Rows in display order: stages in order, entries by Order, then Native in scan order.
    // Rows that change position or group are removed and reinserted so the grouped view regroups them.
    private void Order()
    {
        var rank = Config.Stages.Select((s, i) => (s.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var desired = Rows
            .OrderBy(r => r.Managed is { } m ? rank.GetValueOrDefault(m.Stage, int.MaxValue - 1) : int.MaxValue)
            .ThenBy(r => r.Managed?.Order ?? r.ScanIndex)
            .ToList();
        for (var i = 0; i < desired.Count; i++)
        {
            var row = desired[i];
            if (ReferenceEquals(Rows[i], row) && row.ShownGroup == row.GroupName) continue;
            Rows.Remove(row);
            Rows.Insert(i, row);
            row.ShownGroup = row.GroupName;
        }
    }

    public void AddStage(string name)
    {
        name = ValidStageName(name);
        Config.Stages.Add(new Stage { Name = name });
        RebuildGroups();
        StageNames.Add(name);
        Changed();
    }

    public void RemoveStage(string name)
    {
        if (Config.Managed.Any(m => m.Stage == name))
            throw new InvalidOperationException($"Stage {name} still has entries. Move them out first.");
        Config.Stages.RemoveAll(s => s.Name == name);
        RebuildGroups();
        StageNames.Remove(name);
        Changed();
    }

    public void RenameStage(string oldName, string newName)
    {
        if (oldName == newName) return;
        newName = ValidStageName(newName);
        var stage = Config.Stages.FirstOrDefault(s => s.Name == oldName) ?? throw new ArgumentException("No stage named " + oldName);
        stage.Name = newName;
        if (Config.AskDefaults.Remove(EntryKey.StageKey(oldName), out var askDefault)) Config.AskDefaults[EntryKey.StageKey(newName)] = askDefault;
        foreach (var m in Config.Managed.Where(m => m.Stage == oldName)) m.Stage = newName;
        RebuildGroups();
        foreach (var r in Rows.Where(r => r.IsManaged)) r.Managed = r.Managed;
        StageNames[StageNames.IndexOf(oldName)] = newName;
        Changed();
    }

    public void MoveStage(string name, int delta)
    {
        var i = Config.Stages.FindIndex(s => s.Name == name);
        var j = i + delta;
        if (i < 0 || j < 0 || j >= Config.Stages.Count) return;
        var s = Config.Stages[i];
        Config.Stages.RemoveAt(i);
        Config.Stages.Insert(j, s);
        StageNames.Move(i, j);
        Order();
        Changed();
    }

    public string NextStageName()
    {
        for (var n = Config.Stages.Count + 1; ; n++)
            if (!Config.Stages.Any(s => s.Name == "Stage " + n)) return "Stage " + n;
    }

    private string ValidStageName(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Stage name is empty");
        if (name == NativeGroup) throw new ArgumentException(NativeGroup + " is reserved for entries that are not managed");
        if (Config.Stages.Any(s => s.Name == name)) throw new ArgumentException("A stage named " + name + " already exists");
        return name;
    }

    public void AddCustom(CustomEntry e, string stage)
    {
        RequireStage(stage);
        if (e.Key.Length == 0) e.Key = EntryKey.Make(EntrySource.Custom, Guid.NewGuid().ToString("N"));
        Config.Custom.Add(e);
        Config.KnownKeys.Add(e.Key);
        Rescan();
        var row = Rows.First(r => r.Entry.Key == e.Key);
        Manage(row, stage, int.MaxValue);
    }

    public void RemoveCustom(EntryRow row)
    {
        if (row.Entry.Source != EntrySource.Custom) throw new InvalidOperationException("Only added entries can be removed");
        Unmanage(row);
        Config.Custom.RemoveAll(c => c.Key == row.Entry.Key);
        Rows.Remove(row);
        Changed();
    }

    // Never changes state: starts the entry once, outside the sequence.
    public async void RunNow(EntryRow row)
    {
        var e = row.Entry;
        try
        {
            switch (e.Source)
            {
                // Admin entries run through their elevated task, as at logon.
                case var _ when row.Managed is { Elevated: true }:
                    TaskSchedulerSource.Run(LogonTask.ElevatedTaskPath(e.Key));
                    break;
                case EntrySource.Store:
                    StoreAppSource.RunNow(e.Aumid ?? "");
                    break;
                // A managed task is disabled at source and Task Scheduler will not run it: start its action instead.
                case EntrySource.Task when TaskSchedulerSource.IsEnabled(e.TaskPath ?? EntryKey.Split(e.Key).id):
                    TaskSchedulerSource.Run(e.TaskPath ?? EntryKey.Split(e.Key).id);
                    break;
                default:
                    var o = await new ProcessRunner().RunAsync(e.ExePath, e.Arguments, e.WorkingDirectory, WaitMode.None, 0, 0, CancellationToken.None);
                    if (o.Result is LaunchResult.Failed or LaunchResult.Missing) Notice = $"{e.Name}: {o.Reason}";
                    break;
            }
        }
        catch (Exception ex) { Notice = $"{e.Name}: {ex.Message}"; }
    }

    // ---- save ----

    public SaveReport Save()
    {
        var report = new SaveReport();
        var elevated = new List<(ElevatedOp op, string? key)>();
        var direct = new List<(string key, string name, Action write)>();

        // 0. First save on this machine: record the native states before any source changes,
        //    so a cancelled or failed save can still be restored by Unmanage and Uninstall.
        try
        {
            AppConfig onDisk;
            // An unreadable file is replaced by this save, so it counts as absent.
            try { onDisk = ConfigStore.Load(); }
            catch (JsonException) { onDisk = new AppConfig(); }
            if (onDisk.NativeSnapshot.Count == 0 && Config.NativeSnapshot.Count > 0)
            {
                onDisk.NativeSnapshot = new(Config.NativeSnapshot);
                onDisk.KnownKeys.UnionWith(Rows.Select(r => r.Entry.Key));
                ConfigStore.Save(onDisk);
            }
        }
        catch (Exception ex) { report.Errors.Add("Settings file: " + ex.Message); return report; }

        // 1. Source state: managed rows are disabled at the source, native rows follow their toggle.
        //    Nothing is written yet; elevated changes go first so a cancelled prompt leaves every source untouched.
        foreach (var row in Rows)
        {
            var e = row.Entry;
            if (e.Source == EntrySource.Custom || e.ReadOnly) continue;
            var want = !row.IsManaged && row.Enabled;
            if (want == e.Enabled) continue;
            if (e.Source == EntrySource.Task && !TaskSchedulerSource.CanModify(e.TaskPath ?? EntryKey.Split(e.Key).id))
                elevated.Add((new ElevatedOp { Kind = ElevatedOpKind.SetTaskEnabled, Name = e.TaskPath ?? EntryKey.Split(e.Key).id, Enabled = want }, e.Key));
            else if (Scanner.NeedsElevation(e))
                elevated.Add((new ElevatedOp { Kind = ElevatedOpKind.SetApproved, Hive = "HKLM", List = ApprovedListOf(e.Source).ToString(), Name = EntryKey.Split(e.Key).id, Enabled = want }, e.Key));
            else
                direct.Add((e.Key, e.Name, () => Scanner.SetEnabled(e, want)));
        }

        // 2. Per-entry elevated tasks.
        try
        {
            var existing = LogonTask.ListElevatedKeys().ToHashSet(StringComparer.OrdinalIgnoreCase);
            bool Exists(string key) => existing.Contains(Path.GetFileName(LogonTask.ElevatedTaskPath(key)));
            var wanted = new HashSet<string>();
            foreach (var row in Rows.Where(r => r.Managed is { Elevated: true }))
            {
                wanted.Add(Path.GetFileName(LogonTask.ElevatedTaskPath(row.Entry.Key)));
                // An orphan row has no program to put in a task.
                if (!Exists(row.Entry.Key) && row.Entry.ExePath.Length > 0)
                    elevated.Add((new ElevatedOp { Kind = ElevatedOpKind.CreateElevatedTask, Name = row.Entry.Key, ExePath = row.Entry.ExePath, Arguments = row.Entry.Arguments, WorkingDirectory = row.Entry.WorkingDirectory }, null));
            }
            // Not elevated any more, or no longer managed.
            foreach (var key in _savedManaged.Keys.Concat(Config.Managed.Select(m => m.Key)).Distinct())
                if (!wanted.Contains(Path.GetFileName(LogonTask.ElevatedTaskPath(key))) && Exists(key))
                    elevated.Add((new ElevatedOp { Kind = ElevatedOpKind.DeleteElevatedTask, Name = key }, null));
        }
        catch (Exception ex) { report.Errors.Add("Elevated tasks: " + ex.Message); }

        // 3. One UAC prompt for everything that needs admin, then the direct writes.
        var failed = new HashSet<string>();
        var result = ElevatedOps.RunElevated(elevated.Select(x => x.op).ToList());
        if (!result.Ok && result.Errors is ["Cancelled"])
        {
            report.Cancelled = true;
            report.Errors.Add("Administrator changes were cancelled. Nothing was changed.");
            return report;
        }
        report.Errors.AddRange(result.Errors);
        failed.UnionWith(FailedKeys(elevated, result));
        foreach (var (key, name, write) in direct)
        {
            try { write(); }
            catch (Exception ex) { report.Errors.Add(name + ": " + ex.Message); failed.Add(key); }
        }

        // An entry whose source could not be disabled is not saved as newly managed:
        // Windows would still start it and the launcher would start it a second time.
        var dropped = Config.Managed.Where(m => failed.Contains(m.Key) && !_savedManaged.ContainsKey(m.Key)).ToList();
        foreach (var m in dropped) Config.Managed.Remove(m);

        // 4. Logon task.
        Config.SequencerEnabled = Config.Managed.Count > 0;
        try
        {
            if (Config.SequencerEnabled) LogonTask.Register(); else LogonTask.Unregister();
        }
        catch (Exception ex) { report.Errors.Add("Logon task: " + ex.Message); }

        // 5. Config.
        try
        {
            Config.KnownKeys.UnionWith(Rows.Select(r => r.Entry.Key));
            ConfigStore.Save(Config);
        }
        catch (Exception ex)
        {
            Config.Managed.AddRange(dropped);
            report.Errors.Add("Settings file: " + ex.Message);
            return report;
        }
        Rebuild(keepEdits: false);
        MarkSaved();

        // The dropped entries come back as unsaved edits so the user can retry.
        foreach (var m in dropped)
        {
            Config.Managed.Add(m);
            if (Rows.FirstOrDefault(r => r.Entry.Key == m.Key) is { } row) row.Managed = m;
        }
        if (dropped.Count > 0) { Renumber(); Order(); Changed(); }

        report.Ok = report.Errors.Count == 0;
        return report;
    }

    // ElevatedOps reports "<Kind> <Name>: <message>" for each failed op. A failure that names no op
    // (the process did not start, wrote no result, refused the file) means no source was changed.
    internal static HashSet<string> FailedKeys(IReadOnlyList<(ElevatedOp op, string? key)> elevated, ElevatedResult result)
    {
        bool Named(ElevatedOp op) => result.Errors.Any(err => err.StartsWith($"{op.Kind} {op.Name}: ", StringComparison.Ordinal));
        var global = !result.Ok && !elevated.Any(x => Named(x.op));
        return elevated.Where(x => x.key != null && (global || Named(x.op))).Select(x => x.key!).ToHashSet();
    }

    private static ApprovedList ApprovedListOf(EntrySource s) => s switch
    {
        EntrySource.FolderUser or EntrySource.FolderCommon => ApprovedList.StartupFolder,
        _ => RegistryRunSource.Locate(s).approved ?? throw new InvalidOperationException(s + " has no StartupApproved list"),
    };

    public void Discard()
    {
        Config = ConfigStore.Load();
        Rebuild(keepEdits: false);
        MarkSaved();
    }

    public void ExportTo(string path) => ConfigStore.Export(Config, path);

    // Imported settings apply on Save, like any other edit.
    // NativeSnapshot and KnownKeys describe this machine, so they are kept from the local config.
    public void ImportFrom(string path)
    {
        var imported = ConfigStore.Import(path);
        imported.NativeSnapshot = Config.NativeSnapshot;
        imported.KnownKeys = Config.KnownKeys;
        imported.Managed.RemoveAll(m => !imported.Stages.Any(st => st.Name == m.Stage));
        var previous = Config;
        Config = imported;
        try { Rebuild(keepEdits: false); }
        catch { Config = previous; Rebuild(keepEdits: false); throw; }
    }

    // ---- dirty tracking and timeline ----

    private void MarkSaved()
    {
        _savedManaged = Config.Managed.ToDictionary(m => m.Key, Json);
        _savedCustom = Config.Custom.ToDictionary(c => c.Key, Json);
        _savedStages = Json(Config.Stages);
        _savedSettings = SettingsJson();
        Changed();
    }

    private string SettingsJson() => Json(new { Config.GlobalDelaySeconds, Config.AskCountdownSeconds, Config.ShowWindowsTasks });
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, ConfigStore.Json);

    private int CountChanges()
    {
        var keys = new HashSet<string>();
        var managed = Config.Managed.ToDictionary(m => m.Key, Json);
        foreach (var k in managed.Keys.Union(_savedManaged.Keys))
            if (managed.GetValueOrDefault(k) != _savedManaged.GetValueOrDefault(k)) keys.Add(k);
        var custom = Config.Custom.ToDictionary(c => c.Key, Json);
        foreach (var k in custom.Keys.Union(_savedCustom.Keys))
            if (custom.GetValueOrDefault(k) != _savedCustom.GetValueOrDefault(k)) keys.Add(k);
        foreach (var r in Rows)
            if (!r.IsManaged && r.Entry.Source != EntrySource.Custom && !r.Entry.ReadOnly && r.Enabled != r.Entry.Enabled) keys.Add(r.Entry.Key);
        return keys.Count + (Json(Config.Stages) != _savedStages ? 1 : 0) + (SettingsJson() != _savedSettings ? 1 : 0);
    }

    private void Changed()
    {
        var plan = SequencePlanner.Plan(Config, new HashSet<string>());
        var offsets = plan.ToDictionary(p => p.Entry.Key, p => p.PlannedOffsetSeconds);
        foreach (var r in Rows) r.PlannedOffset = r.IsManaged && offsets.TryGetValue(r.Entry.Key, out var o) ? o : double.NaN;

        double end = Config.GlobalDelaySeconds;
        foreach (var s in Config.Stages)
        {
            foreach (var p in plan.Where(p => p.StageName == s.Name))
                end = Math.Max(end, p.PlannedOffsetSeconds + (p.Entry.Wait == WaitMode.Fixed ? p.Entry.WaitSeconds : 0));
            if (_groups.TryGetValue(s.Name, out var g))
            {
                g.EndSeconds = end;
                g.Count = Config.Managed.Count(m => m.Stage == s.Name);
            }
        }
        if (_groups.TryGetValue(NativeGroup, out var native)) native.Count = Rows.Count(r => !r.IsManaged);

        PlannedTotalSeconds = SequencePlanner.TotalPlannedSeconds(Config);
        TimelineTotalSeconds = Math.Max(1, Math.Max(PlannedTotalSeconds, LastBoot?.TotalSeconds ?? 0));
        DirtyCount = CountChanges();
        foreach (var n in new[] { nameof(PlannedTotalSeconds), nameof(TimelineTotalSeconds), nameof(DirtyCount), nameof(IsDirty), nameof(DirtyText), nameof(IsEmpty) })
            Raise(n);
    }

    private List<BootLogEntry> BootFailures() =>
        LastBoot?.Entries.Where(e => e.Result is LaunchResult.Failed or LaunchResult.Missing || (e.Result == LaunchResult.Exited && e.ExitCode is not (null or 0))).ToList() ?? [];

    // ---- drag and drop ----

    private static string? TargetGroup(IDropInfo d) =>
        d.TargetGroup?.Name as string ?? (d.TargetItem as EntryRow)?.GroupName;

    public void DragOver(IDropInfo d)
    {
        if (d.Data is EntryRow r && r.CanDrag && TargetGroup(d) != null)
        {
            d.Effects = DragDropEffects.Move;
            d.DropTargetAdorner = DropTargetAdorners.Insert;
        }
        else d.Effects = DragDropEffects.None;
    }

    public void Drop(IDropInfo d)
    {
        if (d.Data is not EntryRow r || !r.CanDrag || TargetGroup(d) is not { } group) return;
        if (group == NativeGroup) { Unmanage(r); return; }
        if (ReferenceEquals(d.TargetItem, r)) return;

        var index = d.TargetItem is EntryRow { Managed: { } tm } && tm.Stage == group
            ? DropIndex(r, tm, group, d.InsertPosition.HasFlag(RelativeInsertPosition.AfterTargetItem))
            : 0;
        Move(r, group, index);
    }

    // Insert index within the group, counted without the row being moved.
    public int DropIndex(EntryRow moving, ManagedEntry target, string group, bool after)
    {
        var list = Config.Managed.Where(x => x.Stage == group && x != moving.Managed).OrderBy(x => x.Order).ToList();
        return list.IndexOf(target) + (after ? 1 : 0);
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
