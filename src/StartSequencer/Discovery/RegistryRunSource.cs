using Microsoft.Win32;
using StartSequencer.Model;

namespace StartSequencer.Discovery;

public static class RegistryRunSource
{
    const string Native = @"Software\Microsoft\Windows\CurrentVersion\";
    const string Wow = @"Software\Wow6432Node\Microsoft\Windows\CurrentVersion\";

    static readonly EntrySource[] Sources =
    [
        EntrySource.HkcuRun, EntrySource.HkcuRun32, EntrySource.HkcuRunOnce,
        EntrySource.HklmRun, EntrySource.HklmRun32, EntrySource.HklmRunOnce, EntrySource.HklmRunOnce32,
    ];

    public static (RegistryHive hive, string subKey, ApprovedList? approved) Locate(EntrySource source) => source switch
    {
        EntrySource.HkcuRun => (RegistryHive.CurrentUser, Native + "Run", ApprovedList.Run),
        EntrySource.HkcuRun32 => (RegistryHive.CurrentUser, Wow + "Run", ApprovedList.Run32),
        EntrySource.HkcuRunOnce => (RegistryHive.CurrentUser, Native + "RunOnce", null),
        EntrySource.HklmRun => (RegistryHive.LocalMachine, Native + "Run", ApprovedList.Run),
        EntrySource.HklmRun32 => (RegistryHive.LocalMachine, Wow + "Run", ApprovedList.Run32),
        EntrySource.HklmRunOnce => (RegistryHive.LocalMachine, Native + "RunOnce", null),
        EntrySource.HklmRunOnce32 => (RegistryHive.LocalMachine, Wow + "RunOnce", null),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a registry Run source"),
    };

    public static IReadOnlyList<StartupEntry> Scan()
    {
        var list = new List<StartupEntry>();
        foreach (var source in Sources)
        {
            var (hive, subKey, approved) = Locate(source);
            // 64-bit view with explicit Wow6432Node paths, so redirection never applies.
            using var root = StartupApproved.OpenRoot(hive);
            using var key = root.OpenSubKey(subKey);
            if (key is null) continue;

            foreach (var name in key.GetValueNames())
            {
                // GetValue expands REG_EXPAND_SZ by default; non-string values are not commands.
                if (name.Length == 0 || key.GetValue(name) is not string command) continue;
                list.Add(new StartupEntry
                {
                    Key = EntryKey.Make(source, name),
                    Source = source,
                    Name = name,
                    Command = command,
                    ExePath = "",
                    Location = StartupApproved.Abbrev(hive) + "\\" + subKey,
                    Enabled = approved is not { } a || StartupApproved.Read(hive, a, name),
                    ReadOnly = approved is null,
                });
            }
        }
        return list;
    }

    public static void SetEnabled(EntrySource source, string valueName, bool enabled)
    {
        var (hive, _, approved) = Locate(source);
        if (approved is not { } a)
            throw new InvalidOperationException($"{source} entries have no StartupApproved switch: {valueName}");
        StartupApproved.Write(hive, a, valueName, enabled);
    }
}
