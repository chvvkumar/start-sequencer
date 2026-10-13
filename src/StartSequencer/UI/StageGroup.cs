using System.ComponentModel;
using System.Runtime.CompilerServices;
using StartSequencer.Model;

namespace StartSequencer.UI;

// Header data for one group of the list: a stage, or the Native group (Stage == null).
public sealed class StageGroup : INotifyPropertyChanged
{
    private readonly Action _changed;
    private double _endSeconds;
    private int _count;
    private bool _isEditing;

    public StageGroup(Stage? stage, Action changed)
    {
        Stage = stage;
        _changed = changed;
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

    private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
