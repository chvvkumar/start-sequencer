using System.Text.Json;
using StartSequencer.Model;

namespace StartSequencer.Launch;

public sealed class BootLogEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Stage { get; set; } = "";
    public double PlannedOffsetSeconds { get; set; }
    public DateTime? LaunchedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public int? ExitCode { get; set; }
    public LaunchResult Result { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class BootLog
{
    public DateTime LogonTime { get; set; }
    public DateTime LauncherStart { get; set; }
    public DateTime SequenceStart { get; set; }
    public DateTime SequenceEnd { get; set; }
    public List<BootLogEntry> Entries { get; set; } = [];
    public List<string> AskSelected { get; set; } = [];
    public List<string> AskSkipped { get; set; } = [];
    public bool AskTimedOut { get; set; }
    public double TotalSeconds => (SequenceEnd - LogonTime).TotalSeconds;

    public static string Write(BootLog log)
    {
        Directory.CreateDirectory(ConfigStore.LogsDir);
        var path = Path.Combine(ConfigStore.LogsDir, $"boot-{log.LauncherStart:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(log, ConfigStore.Json));
        Prune(30);
        return path;
    }

    public static BootLog? ReadLatest()
    {
        var f = ListFiles().FirstOrDefault();
        if (f is null) return null;
        try { return JsonSerializer.Deserialize<BootLog>(File.ReadAllText(f), ConfigStore.Json); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    // File names embed yyyyMMdd-HHmmss, so name order is time order.
    public static IReadOnlyList<string> ListFiles() =>
        Directory.Exists(ConfigStore.LogsDir)
            ? Directory.GetFiles(ConfigStore.LogsDir, "boot-*.json").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).ToList()
            : [];

    public static void Prune(int keep)
    {
        foreach (var f in ListFiles().Skip(keep)) File.Delete(f);
    }
}
