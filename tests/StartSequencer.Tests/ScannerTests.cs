using StartSequencer.Discovery;
using StartSequencer.Model;
using Xunit;

public class ScannerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-scan-" + Guid.NewGuid().ToString("N"));
    public ScannerTests() { ConfigStore.SetRootForTests(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Scan_AddsCustom_MarksNew_AndSnapshots()
    {
        var c = new AppConfig();
        c.Custom.Add(new CustomEntry { Key = "Custom|abc", Name = "Mine", Path = @"C:\Windows\notepad.exe", Arguments = "/a" });
        c.Managed.Add(new ManagedEntry { Key = "Custom|abc", Stage = "Core" });
        var r = Scanner.Scan(c);
        var mine = r.Entries.Single(e => e.Key == "Custom|abc");
        Assert.True(mine.IsManaged);
        Assert.True(mine.IsNew);
        Assert.Equal(@"C:\Windows\notepad.exe", mine.ExePath);
        Assert.DoesNotContain("Custom|abc", c.NativeSnapshot.Keys);
        Assert.All(r.Entries.Where(e => e.Source != EntrySource.Custom), e => Assert.Equal(!e.ReadOnly, c.NativeSnapshot.ContainsKey(e.Key)));
        Assert.All(r.Entries.Where(e => e.Source != EntrySource.Custom && e.Source != EntrySource.Store), e => Assert.False(string.IsNullOrEmpty(e.ExePath), e.Key));
    }

    [Fact]
    public void Scan_KnownKey_NotNew()
    {
        var c = new AppConfig();
        c.Custom.Add(new CustomEntry { Key = "Custom|k", Name = "K", Path = @"C:\Windows\notepad.exe" });
        c.KnownKeys.Add("Custom|k");
        Assert.False(Scanner.Scan(c).Entries.Single(e => e.Key == "Custom|k").IsNew);
    }

    [Fact]
    public void Scan_ManagedKeyWithoutSource_YieldsReadOnlyRow()
    {
        var key = "HKCU-Run|does-not-exist-" + Guid.NewGuid();
        var c = new AppConfig();
        c.Managed.Add(new ManagedEntry { Key = key, Stage = "Core" });
        var e = Scanner.Scan(c).Entries.Single(x => x.Key == key);
        Assert.True(e.ReadOnly);
        Assert.True(e.IsManaged);
        Assert.Equal(EntrySource.HkcuRun, e.Source);
        Assert.Equal("Source entry not found", e.Location);
    }
}
