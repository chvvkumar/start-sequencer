using Microsoft.Win32;
using StartSequencer.Discovery;
using StartSequencer.Model;
using Xunit;

public class RegistryRunSourceTests
{
    [Fact]
    public void Locate_MapsEverySource()
    {
        Assert.Equal((RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", (ApprovedList?)ApprovedList.Run), RegistryRunSource.Locate(EntrySource.HkcuRun));
        Assert.Equal((RegistryHive.LocalMachine, @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Run", (ApprovedList?)ApprovedList.Run32), RegistryRunSource.Locate(EntrySource.HklmRun32));
        Assert.Null(RegistryRunSource.Locate(EntrySource.HklmRunOnce).approved);
    }

    [Fact]
    public void Scan_SeesTemporaryHkcuValue_WithExpandedEnvVar()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!;
        run.SetValue(name, @"""%SystemRoot%\notepad.exe"" /a", RegistryValueKind.ExpandString);
        try
        {
            var e = RegistryRunSource.Scan().Single(x => x.Name == name);
            Assert.Equal(EntrySource.HkcuRun, e.Source);
            Assert.Equal("HKCU-Run|" + name, e.Key);
            Assert.DoesNotContain("%SystemRoot%", e.Command);
            Assert.True(e.Enabled);
            Assert.False(e.ReadOnly);
        }
        finally { run.DeleteValue(name, false); }
    }

    [Fact]
    public void SetEnabled_RunOnce_Throws() =>
        Assert.Throws<InvalidOperationException>(() => RegistryRunSource.SetEnabled(EntrySource.HkcuRunOnce, "x", false));

    [Fact]
    public void SameName_InRunAndRun32_YieldsTwoKeys()
    {
        // pure key test, no registry needed
        Assert.NotEqual(EntryKey.Make(EntrySource.HklmRun, "X"), EntryKey.Make(EntrySource.HklmRun32, "X"));
    }
}
