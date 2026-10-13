using StartSequencer.Launch;
using StartSequencer.Model;
using Xunit;

public class BootLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-log-" + Guid.NewGuid().ToString("N"));
    public BootLogTests() { ConfigStore.SetRootForTests(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Write_ReadLatest_Prune()
    {
        for (int i = 0; i < 35; i++)
        {
            var l = new BootLog { LogonTime = DateTime.Now.AddMinutes(-1), LauncherStart = new DateTime(2026, 1, 1, 0, 0, i), SequenceStart = DateTime.Now, SequenceEnd = DateTime.Now };
            l.Entries.Add(new BootLogEntry { Key = "k" + i, Result = LaunchResult.Started });
            BootLog.Write(l);
        }
        Assert.Equal(30, BootLog.ListFiles().Count);
        Assert.Equal("k34", BootLog.ReadLatest()!.Entries[0].Key);
    }
}
