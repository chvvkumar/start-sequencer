namespace StartSequencer.Model;

public enum WaitMode { None, Fixed, Window, Exit }

public sealed class Stage
{
    public string Name { get; set; } = "";
    public bool Ask { get; set; }
}

public sealed class ManagedEntry
{
    public string Key { get; set; } = "";
    public string Stage { get; set; } = "";
    public int Order { get; set; }
    public bool Enabled { get; set; } = true;
    public int DelaySeconds { get; set; }
    public int? CpuBelowPercent { get; set; }
    public int CpuWaitCapSeconds { get; set; } = 120;
    public WaitMode Wait { get; set; } = WaitMode.None;
    public int WaitSeconds { get; set; } = 5;
    public int WindowTimeoutSeconds { get; set; } = 30;
    public bool Ask { get; set; }
    public bool Elevated { get; set; }
}

public sealed class CustomEntry
{
    public string Key { get; set; } = "";       // "Custom|<guid>"
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public bool SequencerEnabled { get; set; }
    public int GlobalDelaySeconds { get; set; } = 10;
    public int AskCountdownSeconds { get; set; } = 20;
    public bool ShowWindowsTasks { get; set; }
    public List<Stage> Stages { get; set; } = [new() { Name = "Core" }, new() { Name = "Comms" }, new() { Name = "Background" }];
    public List<ManagedEntry> Managed { get; set; } = [];
    public List<CustomEntry> Custom { get; set; } = [];
    public Dictionary<string, bool> NativeSnapshot { get; set; } = new();
    public HashSet<string> KnownKeys { get; set; } = new();
    public Dictionary<string, bool> AskDefaults { get; set; } = new();
}
