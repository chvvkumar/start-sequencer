using StartSequencer.Launch;
using StartSequencer.Model;

namespace StartSequencer.Discovery;

public sealed class ScanResult
{
    public required IReadOnlyList<StartupEntry> Entries { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }   // per-source failures, source name + message
}

public static class Scanner
{
    public const string OrphanLocation = "Source entry not found";

    // Mutates config.NativeSnapshot for keys seen the first time; the caller saves.
    public static ScanResult Scan(AppConfig config)
    {
        var raw = new List<StartupEntry>();
        var errors = new List<string>();
        Collect("Registry", RegistryRunSource.Scan);
        Collect("Startup folder", StartupFolderSource.Scan);
        Collect("Task Scheduler", () => TaskSchedulerSource.Scan(config.ShowWindowsTasks));
        Collect("Store apps", StoreAppSource.Scan);

        foreach (var c in config.Custom)
        {
            var managed = config.Managed.FirstOrDefault(m => m.Key == c.Key);
            raw.Add(new StartupEntry
            {
                Key = c.Key,
                Source = EntrySource.Custom,
                Name = c.Name,
                Command = string.IsNullOrEmpty(c.Arguments) ? c.Path : c.Path + " " + c.Arguments,
                ExePath = c.Path,
                Arguments = c.Arguments,
                WorkingDirectory = c.WorkingDirectory,
                Enabled = managed?.Enabled ?? true,
                Location = "Added here",
            });
        }

        var lastRuns = new Dictionary<string, DateTime?>();
        try
        {
            foreach (var b in BootLog.ReadLatest()?.Entries ?? [])
                if (b?.Key is { } k && b.LaunchedAt.HasValue) lastRuns[k] = b.LaunchedAt;
        }
        catch (Exception ex) { errors.Add("Boot log: " + ex.Message); }
        var managedKeys = config.Managed.Select(m => m.Key).ToHashSet();

        var entries = raw.Select(e =>
        {
            var exe = e.ExePath;
            var args = e.Arguments;
            var workDir = e.WorkingDirectory;
            // Store entries carry the logo path in ExePath; it is not a command.
            if (e.Source is not (EntrySource.Custom or EntrySource.Store) && exe.Length == 0)
            {
                if (e.Source is EntrySource.FolderUser or EntrySource.FolderCommon)
                {
                    var (target, lnkArgs, lnkDir) = e.Command.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                        ? CommandLine.ReadLnk(e.Command)
                        : ("", "", "");
                    // Advertised shortcuts and non-.lnk files have no readable target: run the file itself.
                    if (target.Length > 0) (exe, args, workDir) = (target, lnkArgs, lnkDir);
                    else exe = e.Command;
                }
                else
                {
                    (exe, args) = CommandLine.Split(e.Command);
                    exe = Environment.ExpandEnvironmentVariables(exe);   // REG_SZ values can hold %VAR% paths
                }
            }
            var publisher = e.Publisher.Length == 0 && e.Source != EntrySource.Store && exe.Length > 0
                ? ShellInfo.Publisher(exe)
                : e.Publisher;

            return new StartupEntry
            {
                Key = e.Key,
                Source = e.Source,
                Name = e.Name,
                Command = e.Command,
                ExePath = exe,
                Arguments = args,
                WorkingDirectory = workDir,
                Location = e.Location,
                Publisher = publisher,
                Enabled = e.Enabled,
                ReadOnly = e.ReadOnly,
                CanSequence = e.CanSequence,
                LastRun = lastRuns.TryGetValue(e.Key, out var t) ? t : e.LastRun,
                IsNew = !config.KnownKeys.Contains(e.Key),
                IsManaged = managedKeys.Contains(e.Key),
                TaskPath = e.TaskPath,
                Aumid = e.Aumid,
            };
        })
        .OrderBy(e => e.Source)
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var e in entries)
            // Read-only entries are never written, so uninstall has nothing to restore for them.
            if (e.Source != EntrySource.Custom && !e.ReadOnly) config.NativeSnapshot.TryAdd(e.Key, e.Enabled);

        // A managed entry whose source is gone still needs a row, or it can never be unmanaged.
        var found = entries.Select(e => e.Key).ToHashSet();
        foreach (var key in managedKeys.Where(k => !found.Contains(k)))
        {
            var (prefix, id) = EntryKey.Split(key);
            var source = Enum.GetValues<EntrySource>().Where(s => EntryKey.Prefix(s) == prefix).DefaultIfEmpty(EntrySource.Custom).First();
            entries.Add(new StartupEntry
            {
                Key = key,
                Source = source,
                Name = id,
                Command = "",
                Location = OrphanLocation,
                Enabled = false,
                ReadOnly = true,
                CanSequence = true,
                IsManaged = true,
                IsNew = false,
            });
        }

        return new ScanResult { Entries = entries, Errors = errors };

        void Collect(string name, Func<IReadOnlyList<StartupEntry>> scan)
        {
            try { raw.AddRange(scan()); }
            catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
        }
    }

    public static void SetEnabled(StartupEntry e, bool enabled)
    {
        var id = EntryKey.Split(e.Key).id;
        switch (e.Source)
        {
            case EntrySource.HkcuRun or EntrySource.HkcuRun32 or EntrySource.HkcuRunOnce
                or EntrySource.HklmRun or EntrySource.HklmRun32 or EntrySource.HklmRunOnce or EntrySource.HklmRunOnce32:
                RegistryRunSource.SetEnabled(e.Source, id, enabled);
                break;
            case EntrySource.FolderUser or EntrySource.FolderCommon:
                StartupFolderSource.SetEnabled(e.Source, id, enabled);
                break;
            case EntrySource.Task:
                TaskSchedulerSource.SetEnabled(e.TaskPath ?? id, enabled);
                break;
            case EntrySource.Store:
                var (family, taskId) = SplitStoreId(id);
                StoreAppSource.SetEnabled(family, taskId, enabled);
                break;
            default:
                throw new InvalidOperationException("Custom entries have no source switch: " + e.Key);
        }
    }

    // Task entries return false: the caller tries unelevated first and routes UnauthorizedAccessException to elevated ops.
    public static bool NeedsElevation(StartupEntry e) =>
        e.Source is EntrySource.HklmRun or EntrySource.HklmRun32 or EntrySource.FolderCommon;

    // Store ids are "<PackageFamilyName>!<TaskId>"; family names never contain '!'.
    internal static (string family, string taskId) SplitStoreId(string id)
    {
        var i = id.IndexOf('!');
        return i < 0 ? (id, "") : (id[..i], id[(i + 1)..]);
    }
}
