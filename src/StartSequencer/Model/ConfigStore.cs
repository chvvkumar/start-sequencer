using System.Text.Json;
using System.Text.Json.Serialization;

namespace StartSequencer.Model;

public static class ConfigStore
{
    private static string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StartSequencer");

    public static string RootDir => _root;
    public static string ConfigPath => Path.Combine(_root, "config.json");
    public static string LogsDir => Path.Combine(_root, "logs");

    public static JsonSerializerOptions Json { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void SetRootForTests(string dir) => _root = dir;

    // Missing file returns defaults. Corrupt file throws JsonException.
    public static AppConfig Load() => File.Exists(ConfigPath) ? Import(ConfigPath) : new AppConfig();

    public static void Save(AppConfig c)
    {
        Directory.CreateDirectory(RootDir);
        var tmp = ConfigPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(c, Json));
        File.Move(tmp, ConfigPath, overwrite: true);
    }

    public static void Export(AppConfig c, string path) => File.WriteAllText(path, JsonSerializer.Serialize(c, Json));

    // Null items, null lists and duplicate keys would break every lookup by key, so they are dropped here.
    public static AppConfig Import(string path)
    {
        var c = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Json) ?? throw new JsonException("Config file is empty: " + path);
        c.Stages = (c.Stages ?? []).Where(s => s != null).DistinctBy(s => s.Name).ToList();
        c.Managed = (c.Managed ?? []).Where(m => m != null).DistinctBy(m => m.Key).ToList();
        c.Custom = (c.Custom ?? []).Where(x => x != null).DistinctBy(x => x.Key).ToList();
        c.NativeSnapshot ??= new();
        c.KnownKeys ??= new();
        c.AskDefaults ??= new();
        return c;
    }
}
