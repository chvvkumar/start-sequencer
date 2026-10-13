using Microsoft.Win32.TaskScheduler;
using Task = System.Threading.Tasks.Task;
using StartSequencer.Launch;
using StartSequencer.Model;
using Xunit;

public class LauncherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-launch-" + Guid.NewGuid().ToString("N"));
    public LauncherTests() { ConfigStore.SetRootForTests(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    sealed class FakeRunner : IProcessRunner
    {
        public List<string> Calls = [];
        public Task<LaunchOutcome> RunAsync(string exe, string args, string wd, WaitMode w, int ws, int t, CancellationToken ct)
        { Calls.Add(exe); return Task.FromResult(new LaunchOutcome(LaunchResult.Started, DateTime.Now, null, null, "")); }
    }

    [Fact]
    public async Task Run_MissingEntry_LoggedMissing_SequenceContinues_AskDefaultsSaved()
    {
        var c = new AppConfig { SequencerEnabled = true, GlobalDelaySeconds = 0 };
        c.Custom.Add(new CustomEntry { Key = "Custom|x", Name = "X", Path = @"C:\Windows\notepad.exe" });
        c.Managed.Add(new ManagedEntry { Key = "HKCU-Run|does-not-exist-" + Guid.NewGuid(), Stage = "Core", Order = 0 });
        c.Managed.Add(new ManagedEntry { Key = "Custom|x", Stage = "Core", Order = 1, Ask = true });
        ConfigStore.Save(c);
        var runner = new FakeRunner();
        var launcher = new Launcher(runner, items => (new HashSet<string>(items.Select(i => i.Key)), false));
        var log = await launcher.RunAsync(c);
        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(LaunchResult.Missing, log.Entries[0].Result);
        Assert.Equal(LaunchResult.Started, log.Entries[1].Result);
        Assert.Single(runner.Calls);
        Assert.True(ConfigStore.Load().AskDefaults["Custom|x"]);
        Assert.NotNull(BootLog.ReadLatest());
    }

    [Fact]
    public async Task Run_Cancelled_LogsRemainingStepsSkipped_RunnerNotCalled()
    {
        var c = new AppConfig { SequencerEnabled = true, GlobalDelaySeconds = 0 };
        c.Custom.Add(new CustomEntry { Key = "Custom|x", Name = "X", Path = @"C:\Windows\notepad.exe" });
        c.Custom.Add(new CustomEntry { Key = "Custom|y", Name = "Y", Path = @"C:\Windows\notepad.exe" });
        c.Managed.Add(new ManagedEntry { Key = "Custom|x", Stage = "Core", Order = 0 });
        c.Managed.Add(new ManagedEntry { Key = "Custom|y", Stage = "Core", Order = 1 });
        var runner = new FakeRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var log = await new Launcher(runner, _ => (new HashSet<string>(), false)).RunAsync(c, cts.Token);
        Assert.Equal(2, log.Entries.Count);
        Assert.All(log.Entries, e => { Assert.Equal(LaunchResult.Skipped, e.Result); Assert.Equal("cancelled", e.Reason); });
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Run_SequencerDisabled_WritesEmptyLog()
    {
        var log = await new Launcher(new FakeRunner(), _ => (new HashSet<string>(), false)).RunAsync(new AppConfig());
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Run_DisabledManagedTask_RunsActionThroughRunner()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        using var ts = new TaskService();
        var def = ts.NewTask();
        def.Triggers.Add(new LogonTrigger { UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name });
        def.Actions.Add(new ExecAction(@"C:\Windows\System32\cmd.exe", "/c exit 0"));
        ts.RootFolder.RegisterTaskDefinition(name, def).Enabled = false;
        try
        {
            var key = "Task|\\" + name;
            var c = new AppConfig { SequencerEnabled = true, GlobalDelaySeconds = 0 };
            c.Managed.Add(new ManagedEntry { Key = key, Stage = "Core", Order = 0, Wait = WaitMode.Window });
            var runner = new FakeRunner();
            var log = await new Launcher(runner, _ => (new HashSet<string>(), false)).RunAsync(c);
            Assert.Equal(@"C:\Windows\System32\cmd.exe", Assert.Single(runner.Calls));
            Assert.Equal(LaunchResult.Started, log.Entries.Single(e => e.Key == key).Result);
        }
        finally { ts.RootFolder.DeleteTask(name, false); }
    }
}
