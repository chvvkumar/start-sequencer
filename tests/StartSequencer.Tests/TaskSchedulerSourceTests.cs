using Microsoft.Win32.TaskScheduler;
using StartSequencer.Discovery;
using Xunit;

public class TaskSchedulerSourceTests
{
    [Fact]
    public void IsMicrosoftPath_Detects() { Assert.True(TaskSchedulerSource.IsMicrosoftPath(@"\Microsoft\Windows\X")); Assert.False(TaskSchedulerSource.IsMicrosoftPath(@"\Adobe\X")); }

    [Fact]
    public void Scan_FindsTemporaryLogonTask_AndHidesMicrosoftByDefault()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        using var ts = new TaskService();
        var def = ts.NewTask();
        // A trigger for any user needs admin; scope it to the current user so the test runs unelevated.
        def.Triggers.Add(new LogonTrigger { UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name });
        def.Actions.Add(new ExecAction(@"C:\Windows\notepad.exe", "/p", @"C:\Windows"));
        ts.RootFolder.RegisterTaskDefinition(name, def);
        try
        {
            var all = TaskSchedulerSource.Scan(includeMicrosoft: false);
            var e = all.Single(x => x.Name == name);
            Assert.Equal("Task|\\" + name, e.Key);
            Assert.Equal(@"C:\Windows\notepad.exe /p", e.Command);
            Assert.Equal(@"C:\Windows", e.WorkingDirectory);
            Assert.True(e.Enabled);
            Assert.DoesNotContain(all, x => TaskSchedulerSource.IsMicrosoftPath(x.TaskPath!));
            TaskSchedulerSource.SetEnabled("\\" + name, false);
            Assert.False(TaskSchedulerSource.Scan(false).Single(x => x.Name == name).Enabled);
        }
        finally { ts.RootFolder.DeleteTask(name, false); }
    }
}
