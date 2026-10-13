using StartSequencer.Model;
using Xunit;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-test-" + Guid.NewGuid().ToString("N"));
    public ConfigStoreTests() { ConfigStore.SetRootForTests(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Fact]
    public void Load_WhenMissing_ReturnsDefaults()
    {
        var c = ConfigStore.Load();
        Assert.Equal(3, c.Stages.Count);
        Assert.Equal("Core", c.Stages[0].Name);
        Assert.Equal(10, c.GlobalDelaySeconds);
        Assert.Equal(20, c.AskCountdownSeconds);
    }

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        var c = new AppConfig();
        c.Managed.Add(new ManagedEntry { Key = "HKCU-Run|Discord", Stage = "Comms", Order = 2, Wait = WaitMode.Window, CpuBelowPercent = 30 });
        c.NativeSnapshot["HKCU-Run|Discord"] = true;
        c.KnownKeys.Add("HKCU-Run|Discord");
        c.AskDefaults["Stage|Comms"] = false;
        ConfigStore.Save(c);
        var back = ConfigStore.Load();
        var m = Assert.Single(back.Managed);
        Assert.Equal(WaitMode.Window, m.Wait);
        Assert.Equal(30, m.CpuBelowPercent);
        Assert.True(back.NativeSnapshot["HKCU-Run|Discord"]);
        Assert.Contains("HKCU-Run|Discord", back.KnownKeys);
        Assert.False(back.AskDefaults["Stage|Comms"]);
    }

    [Fact]
    public void Save_WritesEnumsAsStrings()
    {
        var c = new AppConfig();
        c.Managed.Add(new ManagedEntry { Key = "k", Wait = WaitMode.Exit });
        ConfigStore.Save(c);
        Assert.Contains("\"wait\": \"Exit\"", File.ReadAllText(ConfigStore.ConfigPath));
    }

    [Fact]
    public void Export_Import_RoundTrips()
    {
        var c = new AppConfig { GlobalDelaySeconds = 42 };
        var p = Path.Combine(_dir, "x.json");
        Directory.CreateDirectory(_dir);
        ConfigStore.Export(c, p);
        Assert.Equal(42, ConfigStore.Import(p).GlobalDelaySeconds);
    }

    [Fact]
    public void EntryKey_Split_SplitsOnFirstPipe()
    {
        var (p, id) = EntryKey.Split("Task|\\A|B");
        Assert.Equal("Task", p);
        Assert.Equal("\\A|B", id);
    }
}
