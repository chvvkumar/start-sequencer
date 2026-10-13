using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using StartSequencer.Discovery;
using StartSequencer.Launch;
using StartSequencer.Model;

namespace StartSequencer.UI;

public sealed class EntryRow : INotifyPropertyChanged
{
    private static readonly WaitMode[] AllWaits = [WaitMode.None, WaitMode.Fixed, WaitMode.Window, WaitMode.Exit];

    private readonly Action _changed;
    private ManagedEntry? _managed;
    private bool _nativeEnabled;
    private double _plannedOffset = double.NaN;
    private double? _actualOffset;
    private LaunchResult? _lastResult;

    public EntryRow(StartupEntry entry, ManagedEntry? managed, bool nativeEnabled, int scanIndex, Action changed)
    {
        Entry = entry;
        _managed = managed;
        _nativeEnabled = nativeEnabled;
        ScanIndex = scanIndex;
        _changed = changed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public StartupEntry Entry { get; }
    internal int ScanIndex { get; }
    internal string? ShownGroup { get; set; }   // group the list view last placed this row in

    public ManagedEntry? Managed
    {
        get => _managed;
        set { _managed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); }
    }

    public bool IsManaged => _managed != null;
    public string GroupName => Managed?.Stage ?? MainViewModel.NativeGroup;

    // Native rows: the state the source should have after Save. Managed rows: whether the launcher starts it.
    public bool Enabled
    {
        get => _managed?.Enabled ?? _nativeEnabled;
        set
        {
            if (_managed != null) _managed.Enabled = value; else _nativeEnabled = value;
            Edited();
        }
    }

    internal bool NativeEnabled { get => _nativeEnabled; set => _nativeEnabled = value; }

    public int DelaySeconds { get => _managed?.DelaySeconds ?? 0; set => Set(m => m.DelaySeconds = Math.Max(0, value)); }
    public int? CpuBelowPercent { get => _managed?.CpuBelowPercent; set => Set(m => m.CpuBelowPercent = value is null ? null : Math.Clamp(value.Value, 1, 100)); }
    public int WaitSeconds { get => _managed?.WaitSeconds ?? 0; set => Set(m => m.WaitSeconds = Math.Max(0, value)); }
    public int WindowTimeoutSeconds { get => _managed?.WindowTimeoutSeconds ?? 0; set => Set(m => m.WindowTimeoutSeconds = Math.Max(1, value)); }
    public WaitMode Wait { get => _managed?.Wait ?? WaitMode.None; set => Set(m => m.Wait = value); }
    public bool Ask { get => _managed?.Ask ?? false; set => Set(m => m.Ask = value); }
    public bool Elevated { get => _managed?.Elevated ?? false; set => Set(m => m.Elevated = value); }

    // Seconds next to the wait mode: fixed wait length, or the timeout for Window and Exit.
    public int WaitValue
    {
        get => Wait == WaitMode.Fixed ? WaitSeconds : WindowTimeoutSeconds;
        set { if (Wait == WaitMode.Fixed) WaitSeconds = value; else WindowTimeoutSeconds = value; }
    }

    public bool HasWaitValue => Wait != WaitMode.None;
    public string WaitValueTip => Wait == WaitMode.Fixed ? "Seconds to wait before the next entry" : "Timeout in seconds";
    public IReadOnlyList<WaitMode> WaitModes => AllWaits;

    public double PlannedOffset { get => _plannedOffset; set { _plannedOffset = value; Raise(); Raise(nameof(TimelineTip)); } }
    public double? ActualOffset { get => _actualOffset; set { _actualOffset = value; Raise(); Raise(nameof(TimelineTip)); } }
    public LaunchResult? LastResult { get => _lastResult; set { _lastResult = value; Raise(); } }

    public string TimelineTip =>
        double.IsNaN(PlannedOffset) ? "Not started at logon"
        : ActualOffset is { } a ? $"planned {PlannedOffset:0.#} s, actual {a:0.#} s"
        : $"planned {PlannedOffset:0.#} s";

    public ImageSource? Icon => ShellInfo.Icon(Entry.ExePath);

    public bool IsNew => Entry.IsNew;
    public bool IsOrphan => Entry.Location == Scanner.OrphanLocation;
    // Orphan rows are read only but can still be dragged to Native to forget them.
    public bool CanDrag => Entry.CanSequence && (!Entry.ReadOnly || IsOrphan);
    public bool CanToggle => !Entry.ReadOnly && (IsManaged || Entry.Source != EntrySource.Custom);
    public bool CanRemove => Entry.Source == EntrySource.Custom;
    public string CommandTip => Entry.Location.Length > 0 ? Entry.Command + "\n" + Entry.Location : Entry.Command;

    public string? ToggleTip =>
        Entry.Location == Scanner.OrphanLocation ? "Source entry not found. Drag to Native to forget it."
        : Entry.Source is EntrySource.HkcuRunOnce or EntrySource.HklmRunOnce or EntrySource.HklmRunOnce32 ? "RunOnce entries cannot be disabled"
        : Entry.ReadOnly && Entry.Source == EntrySource.Task ? "Windows tasks are shown read only"
        : Entry.ReadOnly && Entry.Source == EntrySource.Store ? "Set by policy"
        : Entry.Source == EntrySource.Task && !Entry.CanSequence ? "Task has no single program action"
        : Entry.Source == EntrySource.Custom && !IsManaged ? "Added entries run only from a stage"
        : IsManaged ? "Start this entry in the sequence"
        : "Let Windows start this entry at logon";

    public string LastRunText => Entry.LastRun is { } t ? t.ToString("MMM d HH:mm") : "";

    public string SourceLabel => Entry.Source switch
    {
        EntrySource.HkcuRun => "HKCU Run",
        EntrySource.HkcuRun32 => "HKCU Run (32)",
        EntrySource.HkcuRunOnce => "HKCU RunOnce",
        EntrySource.HklmRun => "HKLM Run",
        EntrySource.HklmRun32 => "HKLM Run (32)",
        EntrySource.HklmRunOnce => "HKLM RunOnce",
        EntrySource.HklmRunOnce32 => "HKLM RunOnce (32)",
        EntrySource.FolderUser => "Startup folder",
        EntrySource.FolderCommon => "Common startup",
        EntrySource.Task => "Task",
        EntrySource.Store => "Store",
        _ => "Added",
    };

    public string SourceGlyph => Entry.Source switch
    {
        EntrySource.FolderUser or EntrySource.FolderCommon => "",
        EntrySource.Task => "",
        EntrySource.Store => "",
        EntrySource.Custom => "",
        _ => "",
    };

    private void Set(Action<ManagedEntry> apply)
    {
        if (_managed == null) return;
        apply(_managed);
        Edited();
    }

    private void Edited()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        _changed();
    }

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
