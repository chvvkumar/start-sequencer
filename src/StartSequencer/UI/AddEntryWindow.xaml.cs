using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using StartSequencer.Model;

namespace StartSequencer.UI;

public partial class AddEntryWindow : Wpf.Ui.Controls.FluentWindow
{
    private string _autoName = "";
    private string _autoDir = "";

    public AddEntryWindow(IEnumerable<string> stages)
    {
        InitializeComponent();
        StageBox.ItemsSource = stages.ToList();
        StageBox.SelectedIndex = 0;
        Loaded += (_, _) => PathBox.Focus();
    }

    public CustomEntry? Result { get; private set; }
    public string Stage => StageBox.SelectedItem as string ?? "";

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new OpenFileDialog { Filter = "Programs and scripts|*.exe;*.lnk;*.bat;*.cmd;*.ps1|All files|*.*" };
        if (d.ShowDialog(this) == true) PathBox.Text = d.FileName;
    }

    // Name and working directory follow the path until the user types their own.
    private void PathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var path = PathBox.Text.Trim().Trim('"');
        string name, dir;
        try
        {
            name = Path.GetFileNameWithoutExtension(path);
            dir = Path.GetDirectoryName(path) ?? "";
        }
        catch (ArgumentException) { return; }
        if (NameBox.Text == _autoName) NameBox.Text = name;
        if (DirBox.Text == _autoDir) DirBox.Text = dir;
        _autoName = name;
        _autoDir = dir;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var path = PathBox.Text.Trim().Trim('"');
        var dir = DirBox.Text.Trim();
        string? error =
            !File.Exists(path) ? "File not found: " + path
            : dir.Length > 0 && !Directory.Exists(dir) ? "Working directory not found: " + dir
            : Stage.Length == 0 ? "Add a stage first"
            : null;
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = new CustomEntry
        {
            Name = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : Path.GetFileNameWithoutExtension(path),
            Path = path,
            Arguments = ArgsBox.Text.Trim(),
            WorkingDirectory = dir,
        };
        DialogResult = true;
    }
}
