using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace StartSequencer;

public partial class App : Application
{
    // Modes: no args = GUI, --launch, --elevated-op <path> <sha256>, --uninstall.
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        var mode = args.Length > 0 ? args[0] : "";

        switch (mode)
        {
            case "--launch":
                ShutdownMode = ShutdownMode.OnExplicitShutdown;   // closing the ask dialog must not end the process
                ApplicationThemeManager.ApplySystemTheme();
                var code = 0;
                try { await new Launch.Launcher().RunAsync(); }
                catch (Exception ex)
                {
                    code = 1;
                    try
                    {
                        Directory.CreateDirectory(Model.ConfigStore.LogsDir);
                        File.WriteAllText(Path.Combine(Model.ConfigStore.LogsDir, "launcher-error.txt"), ex.ToString());
                    }
                    catch { }
                }
                Shutdown(code);
                break;
            case "--elevated-op":
                Shutdown(args.Length > 2 ? Sys.ElevatedOps.ApplyFile(args[1], args[2]) : 1);
                break;
            case "--uninstall":
                ApplicationThemeManager.ApplySystemTheme();
                Sys.Uninstaller.Run();
                Shutdown(0);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                new UI.MainWindow().Show();
                break;
        }
    }

    // Fluent's own semantic values: "new" and timeouts, ok, failure.
    public static void ApplySemanticBrushes(ApplicationTheme theme)
    {
        var dark = theme == ApplicationTheme.Dark;
        Set("SsWarnBrush", dark ? "#F0C341" : "#C19C00");
        Set("SsOkBrush", dark ? "#6CCB5F" : "#0F7B0F");
        Set("SsFailBrush", dark ? "#FF99A4" : "#C42B1C");

        static void Set(string key, string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            Current.Resources[key] = b;
        }
    }
}
