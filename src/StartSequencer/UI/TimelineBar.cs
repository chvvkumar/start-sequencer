using System.Windows;
using System.Windows.Media;
using StartSequencer.Launch;
using Wpf.Ui.Appearance;

namespace StartSequencer.UI;

// Thin track on the shared boot axis: rounded marker at the planned offset, line at the last boot's actual launch.
public sealed class TimelineBar : FrameworkElement
{
    public static readonly DependencyProperty TotalSecondsProperty = Register(nameof(TotalSeconds), typeof(double), 1.0);
    public static readonly DependencyProperty PlannedOffsetProperty = Register(nameof(PlannedOffset), typeof(double), double.NaN);
    public static readonly DependencyProperty ActualOffsetProperty = Register(nameof(ActualOffset), typeof(double?), null);
    public static readonly DependencyProperty ResultProperty = Register(nameof(Result), typeof(LaunchResult?), null);

    private static DependencyProperty Register(string name, Type type, object? value) =>
        DependencyProperty.Register(name, type, typeof(TimelineBar), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public double TotalSeconds { get => (double)GetValue(TotalSecondsProperty); set => SetValue(TotalSecondsProperty, value); }
    public double PlannedOffset { get => (double)GetValue(PlannedOffsetProperty); set => SetValue(PlannedOffsetProperty, value); }
    public double? ActualOffset { get => (double?)GetValue(ActualOffsetProperty); set => SetValue(ActualOffsetProperty, value); }
    public LaunchResult? Result { get => (LaunchResult?)GetValue(ResultProperty); set => SetValue(ResultProperty, value); }

    public TimelineBar()
    {
        // Brushes are looked up while rendering, so a theme switch needs a new render pass.
        ThemeChangedEvent onTheme = (_, _) => InvalidateVisual();
        Loaded += (_, _) => ApplicationThemeManager.Changed += onTheme;
        Unloaded += (_, _) => ApplicationThemeManager.Changed -= onTheme;
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 14);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 8) return;
        var total = TotalSeconds > 0 ? TotalSeconds : 1;
        var mid = h / 2;

        dc.DrawRectangle(Brush("ControlStrokeColorDefaultBrush"), null, new Rect(0, mid - 1, w, 2));

        if (!double.IsNaN(PlannedOffset))
        {
            var x = Math.Clamp(PlannedOffset / total * w, 4, w - 4);
            dc.DrawRoundedRectangle(Brush("TextFillColorSecondaryBrush"), null, new Rect(x - 4, mid - 5, 8, 10), 2, 2);
        }

        if (ActualOffset is { } a)
        {
            var x = Math.Clamp(a / total * w, 1, w - 1);
            var key = Result switch
            {
                LaunchResult.WindowTimeout or LaunchResult.ExitTimeout => "SsWarnBrush",
                LaunchResult.Failed or LaunchResult.Missing => "SsFailBrush",
                _ => "SsOkBrush",
            };
            dc.DrawRectangle(Brush(key), null, new Rect(x - 1, mid - 7, 2, 14));
        }
    }

    private Brush Brush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
