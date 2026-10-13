namespace StartSequencer.Model;

public enum EntrySource { HkcuRun, HkcuRun32, HkcuRunOnce, HklmRun, HklmRun32, HklmRunOnce, HklmRunOnce32, FolderUser, FolderCommon, Task, Store, Custom }

public sealed class StartupEntry
{
    public required string Key { get; init; }
    public required EntrySource Source { get; init; }
    public required string Name { get; init; }
    public string Command { get; init; } = "";      // full command line as stored at source
    public string ExePath { get; init; } = "";      // split exe path
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string Location { get; init; } = "";     // registry path, folder path, task path, or package name
    public string Publisher { get; init; } = "";
    public bool Enabled { get; init; }
    public bool ReadOnly { get; init; }             // RunOnce, Microsoft\ tasks
    public bool CanSequence { get; init; } = true;  // false for Store
    public DateTime? LastRun { get; init; }
    public bool IsNew { get; init; }
    public bool IsManaged { get; init; }
    public string? TaskPath { get; init; }          // for Source == Task
    public string? Aumid { get; init; }             // for Source == Store
}

public static class EntryKey
{
    public static string Prefix(EntrySource s) => s switch
    {
        EntrySource.HkcuRun => "HKCU-Run",
        EntrySource.HkcuRun32 => "HKCU-Run32",
        EntrySource.HkcuRunOnce => "HKCU-RunOnce",
        EntrySource.HklmRun => "HKLM-Run",
        EntrySource.HklmRun32 => "HKLM-Run32",
        EntrySource.HklmRunOnce => "HKLM-RunOnce",
        EntrySource.HklmRunOnce32 => "HKLM-RunOnce32",
        EntrySource.FolderUser => "Folder-User",
        EntrySource.FolderCommon => "Folder-Common",
        EntrySource.Task => "Task",
        EntrySource.Store => "Store",
        EntrySource.Custom => "Custom",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static string Make(EntrySource s, string id) => Prefix(s) + "|" + id;

    // Splits on the first '|'. A key without '|' returns (key, "").
    public static (string prefix, string id) Split(string key)
    {
        var i = key.IndexOf('|');
        return i < 0 ? (key, "") : (key[..i], key[(i + 1)..]);
    }

    public static string StageKey(string stageName) => "Stage|" + stageName;
}
