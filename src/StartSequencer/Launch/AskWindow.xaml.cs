using System.Windows;
using System.Windows.Threading;
using StartSequencer.Sys;

namespace StartSequencer.Launch;

public sealed partial class AskWindow : Wpf.Ui.Controls.FluentWindow
{
    public sealed class Row
    {
        public required string Key { get; init; }
        public required string Label { get; init; }
        public bool IsChecked { get; set; }
        public Visibility StageCaptionVisibility { get; init; }
    }

    private readonly List<Row> _rows;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _remaining;
    private bool _decided;

    public IReadOnlySet<string> Selected { get; private set; } = new HashSet<string>();
    public bool TimedOut { get; private set; }
    public bool SavedDefaults => !TimedOut;

    public AskWindow(IReadOnlyList<AskItem> items, IReadOnlyList<(string key, string name)> displayNames, int countdownSeconds)
    {
        InitializeComponent();
        var names = new Dictionary<string, string>();
        foreach (var (key, name) in displayNames) names.TryAdd(key, name);
        _rows = items.Select(i => new Row
        {
            Key = i.Key,
            Label = names.TryGetValue(i.Key, out var n) && n != "" ? n : i.Label,
            IsChecked = i.Default,
            StageCaptionVisibility = i.IsStage ? Visibility.Visible : Visibility.Collapsed,
        }).ToList();
        ItemsList.ItemsSource = _rows;

        // Do not take focus from a fullscreen game or video; Topmost still makes the dialog visible.
        ShowActivated = !Native.ForegroundIsFullscreen();

        _remaining = Math.Max(1, countdownSeconds);
        UpdateCountdown();
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining > 0) { UpdateCountdown(); return; }
            TimedOut = true;
            Finish(CheckedKeys());
        };
        Loaded += (_, _) => _timer.Start();
        // Closing from the title bar counts as Skip all.
        Closing += (_, _) => { if (!_decided) Decide([]); };
    }

    private void UpdateCountdown() => CountdownText.Text = $"Starting in {_remaining} s";

    private HashSet<string> CheckedKeys() => _rows.Where(r => r.IsChecked).Select(r => r.Key).ToHashSet();

    private void StartSelected_Click(object sender, RoutedEventArgs e) => Finish(CheckedKeys());
    private void StartAll_Click(object sender, RoutedEventArgs e) => Finish(_rows.Select(r => r.Key).ToHashSet());
    private void SkipAll_Click(object sender, RoutedEventArgs e) => Finish([]);

    private void Decide(HashSet<string> keys)
    {
        _decided = true;
        _timer.Stop();
        Selected = keys;
    }

    private void Finish(HashSet<string> keys)
    {
        if (_decided) return;
        Decide(keys);
        Close();
    }
}
