using StartSequencer.Sys;
using Xunit;

public class LogonTaskTests
{
    [Fact]
    public void ElevatedTaskPath_SanitizesKey() =>
        Assert.Equal(@"\StartSequencer\Elevated\HKCU-Run_Discord", LogonTask.ElevatedTaskPath("HKCU-Run|Discord"));

    [Fact]
    public void Register_Unregister_RoundTrip()
    {
        try
        {
            LogonTask.Register();
            Assert.True(LogonTask.IsRegistered());
            LogonTask.Unregister();
            Assert.False(LogonTask.IsRegistered());
            Assert.False(FolderExists(LogonTask.Folder));
        }
        finally { LogonTask.Unregister(); }
    }

    static bool FolderExists(string path)
    {
        using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
        try { return ts.GetFolder(path) != null; } catch (FileNotFoundException) { return false; }
    }
}
