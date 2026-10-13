using StartSequencer.Launch;
using StartSequencer.Model;
using Xunit;

public class ProcessRunnerTests
{
    [Fact]
    public async Task Run_Missing_ReturnsMissing()
    {
        var r = await new ProcessRunner().RunAsync(@"C:\nope\missing.exe", "", "", WaitMode.None, 0, 5, CancellationToken.None);
        Assert.Equal(LaunchResult.Missing, r.Result);
    }

    [Fact]
    public async Task Run_CmdExit_ReturnsExitCode()
    {
        var r = await new ProcessRunner().RunAsync(@"C:\Windows\System32\cmd.exe", "/c exit 7", "", WaitMode.Exit, 0, 10, CancellationToken.None);
        Assert.Equal(LaunchResult.Exited, r.Result);
        Assert.Equal(7, r.ExitCode);
    }

    // Needs an interactive desktop: the wait succeeds only when Notepad shows a visible top-level window.
    [Fact]
    [Trait("Category", "Desktop")]
    public async Task Run_NotepadWindow_SeesWindow_ThenKill()
    {
        var before = System.Diagnostics.Process.GetProcessesByName("notepad").Select(p => p.Id).ToHashSet();
        var r = await new ProcessRunner().RunAsync(@"C:\Windows\System32\notepad.exe", "", "", WaitMode.Window, 0, 20, CancellationToken.None);
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("notepad").Where(p => !before.Contains(p.Id))) { try { p.Kill(); } catch { } }
        Assert.Equal(LaunchResult.WindowSeen, r.Result);
        Assert.NotNull(r.ReadyAt);
    }
}
