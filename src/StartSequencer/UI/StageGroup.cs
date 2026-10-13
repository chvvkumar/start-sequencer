using System.ComponentModel;
using System.Runtime.CompilerServices;
using StartSequencer.Model;

namespace StartSequencer.UI;

// Header data for one group of the list: a stage, or the Native group (Stage == null).
public sealed class StageGroup : INotifyPropertyChanged
{
    private readonly Action _changed;
    private readonly HashSet<string> _collapsed;
    private double _endSeconds;
    private int _count;
    private bool _isEditing;
    private bool _isDropTarget;

    public StageGroup(Stage? stage, Action changed, HashSet<string> collapsed)
    {
        Stage = stage;
        _changed = changed;
        _collapsed = collapsed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Stage? Stage { get; }
    public string Name => Stage?.Name ?? MainViewModel.NativeGroup;
    public bool IsNative => Stage == null;
    public bool IsStage => Stage != null;

    public bool Ask
    {
        get => Stage?.Ask ?? false;
        set { if (Stage == null) return; Stage.Ask = value; Raise(); _changed(); }
    }

    // Planned seconds after logon at which the stage's last entry is done.
    public double EndSeconds { get => _endSeconds; set { _endSeconds = value; Raise(); Raise(nameof(EndText)); } }
    public string EndText => $"{EndSeconds:0} s";

    public int Count { get => _count; set { _count = value; Raise(); Raise(nameof(CanDelete)); Raise(nameof(CountText)); } }
    public string CountText => Count == 1 ? "1 entry" : $"{Count} entries";
    public bool CanDelete => !IsNative && Count == 0;

    public bool IsEditing { get => _isEditing; set { _isEditing = value; Raise(); Raise(nameof(ShowName)); } }
    public bool ShowName => !_isEditing;

    // Collapsed groups are kept by name in a set the view model owns, so they survive a rebuild.
    public bool IsExpanded
    {
        get => !_collapsed.Contains(Name);
        set { if (value) _collapsed.Remove(Name); else _collapsed.Add(Name); Raise(); Raise(nameof(ChevronGlyph)); }
    }
    public string ChevronGlyph => IsExpanded ? "" : "";

    // Stage strip chip under the pointer during a drag.
    public bool IsDropTarget { get => _isDropTarget; set { if (_isDropTarget == value) return; _isDropTarget = value; Raise(); } }
    public string ChipTip => IsNative ? "Drop a row here to stop managing it." : "Drop a row here to move it into this stage.";

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
