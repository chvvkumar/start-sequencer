using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using StartSequencer.Model;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StartSequencer.UI;

public partial class MainWindow : FluentWindow
{
    // Row padding the list item style adds outside the columns (measured 13 DIP), plus rounding at fractional scale.
    private const double RowChrome = 16;

    private readonly MainViewModel _vm;
    private readonly PropertyGroupDescription _grouping = new(nameof(EntryRow.GroupName));
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);
        App.ApplySemanticBrushes(ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += (theme, _) => App.ApplySemanticBrushes(theme);

        AppConfig config;
        try { config = ConfigStore.Load(); }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Cannot read " + ConfigStore.ConfigPath + "\n\n" + ex.Message +
                "\n\nStarting with default settings. Saving replaces the file.", "StartSequencer",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            config = new AppConfig();
        }

        // Older versions recorded \Microsoft\ tasks in the snapshot; the app never writes them, so forget them.
        foreach (var key in config.NativeSnapshot.Keys.Where(k => EntryKey.Split(k) is ("Task", var id) && Discovery.TaskSchedulerSource.IsMicrosoftPath(id)).ToList())
            config.NativeSnapshot.Remove(key);

        _vm = new MainViewModel(config);
        DataContext = _vm;
        if (Sys.ElevatedOps.IsElevated()) _vm.Notice = "Running as administrator: Run now starts entries as administrator.";
        // The exe was moved or replaced since the logon task was registered: point the task at this exe.
        try
        {
            if (config.SequencerEnabled && Sys.LogonTask.RegisteredExePath() is { } registered && !string.Equals(registered, Sys.LogonTask.ExePath, StringComparison.OrdinalIgnoreCase))
            {
                Sys.LogonTask.Register();
                _vm.Notice = "Logon task updated to the current exe path.";
            }
        }
        catch (Exception ex) { _vm.Notice = "Logon task: " + ex.Message; }
        ((GroupLookupConverter)Resources["GroupLookup"]).ViewModel = _vm;
        GongSolutions.Wpf.DragDrop.DragDrop.SetDragHandler(List, new RowDragHandler());

        var view = (ListCollectionView)CollectionViewSource.GetDefaultView(_vm.Rows);
        view.GroupDescriptions.Add(_grouping);
        SyncGroups();
        _vm.StageNames.CollectionChanged += (_, _) => SyncGroups();
        List.ItemsSource = view;
        LayoutUpdated += (_, _) => AlignBootBar();
        List.SizeChanged += (_, _) => FitCommandColumn();
        List.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) => { if (e.ViewportWidthChange != 0) FitCommandColumn(); }));

        // 36 px rows on top of the container style WPF-UI picks for GridView.
        List.Loaded += (_, _) =>
        {
            var baseStyle = List.ItemContainerStyle ?? List.TryFindResource(typeof(Wpf.Ui.Controls.ListViewItem)) as Style;
            List.ItemContainerStyle = new Style(typeof(Wpf.Ui.Controls.ListViewItem), baseStyle) { Setters = { new Setter(HeightProperty, 36.0), new Setter(MarginProperty, new Thickness(0)) } };
        };
    }

    // Puts the status line bar under the row bars: same left edge as the Timeline column's cells.
    private void AlignBootBar()
    {
        var header = Find<GridViewColumnHeader>(List, h => h.Column == TimelineColumn);
        if (header == null || !header.IsVisible || BootBar.Parent is not FrameworkElement host) return;
        var left = header.TransformToAncestor(this).Transform(new Point(0, 0)).X - host.TransformToAncestor(this).Transform(new Point(0, 0)).X;
        var right = host.ActualWidth - left - 6 - BootBar.Width;   // 6: GridView cell margin
        if (right >= 0 && Math.Abs(BootBar.Margin.Right - right) > 0.5) BootBar.Margin = new Thickness(0, 0, right, 0);
    }

    private static T? Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T t && match(t)) return t;
            if (Find(child, match) is { } found) return found;
        }
        return null;
    }

    // The command column takes what the viewport leaves after the fixed columns, so the list never scrolls sideways.
    private void FitCommandColumn()
    {
        // Looked up each time: the list's template is applied again after load. The header row has its own ScrollViewer.
        var scroll = Find<ScrollViewer>(List, s => s.TemplatedParent == List);
        if (scroll == null || List.View is not System.Windows.Controls.GridView view) return;
        var others = view.Columns.Where(c => c != CommandColumn).Sum(c => c.ActualWidth);
        var width = Math.Max(40, Math.Floor(scroll.ViewportWidth - others - RowChrome));
        if (Math.Abs(CommandColumn.Width - width) > 0.5) CommandColumn.Width = width;
    }

    // Fixed group names keep stage order and show empty stages as drop targets.
    private void SyncGroups()
    {
        _grouping.GroupNames.Clear();
        foreach (var s in _vm.StageNames) _grouping.GroupNames.Add(s);
        _grouping.GroupNames.Add(MainViewModel.NativeGroup);
        CollectionViewSource.GetDefaultView(_vm.Rows).Refresh();
    }

    // Store rows and read-only rows cannot enter the sequence.
    private sealed class RowDragHandler : DefaultDragHandler
    {
        public override bool CanStartDrag(IDragInfo dragInfo) => dragInfo.SourceItem is EntryRow { CanDrag: true };
    }

    private static EntryRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as EntryRow;
    private static StageGroup? GroupOf(object sender) => (sender as FrameworkElement)?.DataContext as StageGroup;

    private void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _vm.Notice = ex.Message;
        }
    }

    // ---- toolbar ----

    private void AddEntry_Click(object sender, RoutedEventArgs e)
    {
        var w = new AddEntryWindow(_vm.StageNames) { Owner = this };
        if (w.ShowDialog() == true && w.Result is { } entry) Try(() => _vm.AddCustom(entry, w.Stage));
    }

    private void AddStage_Click(object sender, RoutedEventArgs e) => Try(() => _vm.AddStage(_vm.NextStageName()));
    private void Rescan_Click(object sender, RoutedEventArgs e) => _vm.Rescan();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsFlyout.IsOpen = true;

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var d = new SaveFileDialog { Filter = "JSON|*.json", FileName = "startsequencer-config.json" };
        if (d.ShowDialog(this) == true) Try(() => _vm.ExportTo(d.FileName));
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "JSON|*.json" };
        if (d.ShowDialog(this) == true) Try(() => _vm.ImportFrom(d.FileName));
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        SettingsFlyout.IsOpen = false;
        Sys.Uninstaller.Run();
        Try(_vm.Discard);
    }

    // ---- rows ----

    private void RunNow_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) _vm.RunNow(r); }
    private void RunNowMenu_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) _vm.RunNow(r); }
    private void UnmanageMenu_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) _vm.Unmanage(r); }
    private void RemoveMenu_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) Try(() => _vm.RemoveCustom(r)); }

    // ---- stage headers ----

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is GridViewColumnHeader { Column.Header: string column } && MainViewModel.SortColumns.Contains(column))
            _vm.SortAll(column);
    }

    private void Chevron_Click(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) g.IsExpanded = !g.IsExpanded; }
    private void Rename_Click(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { IsStage: true } g) g.IsEditing = true; }

    // A click on the header background focuses it so F2 reaches it.
    private void GroupHeader_MouseDown(object sender, MouseButtonEventArgs e) => (sender as UIElement)?.Focus();

    private void GroupHeader_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || GroupOf(sender) is not { IsStage: true, IsEditing: false } g) return;
        g.IsEditing = true;
        e.Handled = true;
    }

    private void StageName_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && GroupOf(sender) is { IsStage: true } g) g.IsEditing = true;
    }

    private void StageEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox t && t.IsVisible)
        {
            t.Text = GroupOf(sender)?.Name ?? "";
            t.Focus();
            t.SelectAll();
        }
    }

    private void StageEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox { ToolTip: System.Windows.Controls.ToolTip tip } box) { tip.IsOpen = false; box.ToolTip = RenameTip; }
        if (e.Key == Key.Enter) CommitRename(sender, keepOpen: true);
        else if (e.Key == Key.Escape && GroupOf(sender) is { } g) g.IsEditing = false;
    }

    // Leaving the box with a rejected name cancels the edit; Enter keeps the box open.
    private void StageEdit_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitRename(sender, keepOpen: false);

    private bool _renaming;
    private const string RenameTip = "Enter to rename, Esc to cancel.";

    private void CommitRename(object sender, bool keepOpen)
    {
        if (_renaming || GroupOf(sender) is not { IsEditing: true } g || sender is not System.Windows.Controls.TextBox t) return;
        bool ok;
        // A successful rename rebuilds the headers, which takes focus from this box and re-enters here.
        _renaming = true;
        try { ok = _vm.RenameStage(g.Name, t.Text); }
        finally { _renaming = false; }
        if (ok || !keepOpen) { g.IsEditing = false; return; }
        t.ToolTip = new System.Windows.Controls.ToolTip { Content = "Name is empty or already used", PlacementTarget = t, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, IsOpen = true };
    }

    private void StageUp_Click(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) _vm.MoveStage(g.Name, -1); }
    private void StageDown_Click(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) _vm.MoveStage(g.Name, 1); }
    private void StageDelete_Click(object sender, RoutedEventArgs e) { if (GroupOf(sender) is { } g) Try(() => _vm.RemoveStage(g.Name)); }

    // ---- save bar and status line ----

    private void Save_Click(object sender, RoutedEventArgs e) => SaveNow();

    private bool SaveNow()
    {
        var r = _vm.Save();
        SaveErrors.Message = string.Join("\n", r.Errors);
        SaveErrors.IsOpen = !r.Ok;
        return r.Ok;
    }

    private void Discard_Click(object sender, RoutedEventArgs e)
    {
        SaveErrors.IsOpen = false;
        Try(_vm.Discard);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Try(() =>
        {
            Directory.CreateDirectory(ConfigStore.LogsDir);
            Process.Start("explorer.exe", ConfigStore.LogsDir)?.Dispose();
        });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !_vm.IsDirty) return;
        e.Cancel = true;
        // The prompt opens after Closing returns; a window that is closing cannot own a dialog.
        Dispatcher.InvokeAsync(PromptCloseAsync);
    }

    private async Task PromptCloseAsync()
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = "StartSequencer",
            Content = $"Save {(_vm.DirtyCount == 1 ? "the unsaved change" : $"the {_vm.DirtyCount} unsaved changes")} before closing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            IsSecondaryButtonEnabled = true,
            CloseButtonText = "Cancel",
            Owner = this,
        };
        var result = await box.ShowDialogAsync();
        if (result == Wpf.Ui.Controls.MessageBoxResult.None) return;
        if (result == Wpf.Ui.Controls.MessageBoxResult.Primary && !SaveNow()) return;
        _closeConfirmed = true;
        Close();
    }
}
