using Microsoft.Win32;
using StartSequencer.Model;

namespace StartSequencer.Discovery;

public static class StartupFolderSource
{
    public static string UserFolder { get; } = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    public static string CommonFolder { get; } = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);

    public static IReadOnlyList<StartupEntry> Scan() =>
        [.. ScanFolder(EntrySource.FolderUser, UserFolder), .. ScanFolder(EntrySource.FolderCommon, CommonFolder)];

    public static void SetEnabled(EntrySource source, string fileName, bool enabled) =>
        StartupApproved.Write(HiveOf(source), ApprovedList.StartupFolder, fileName, enabled);

    static IEnumerable<StartupEntry> ScanFolder(EntrySource source, string folder)
    {
        if (folder.Length == 0 || !Directory.Exists(folder)) return [];
        var hive = HiveOf(source);
        return Directory.EnumerateFiles(folder)
            .Where(f => !Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            .Select(f =>
            {
                var fileName = Path.GetFileName(f);
                return new StartupEntry
                {
                    Key = EntryKey.Make(source, fileName),
                    Source = source,
                    Name = Path.GetFileNameWithoutExtension(f),
                    Command = f,
                    ExePath = "",
                    Location = folder,
                    Enabled = StartupApproved.Read(hive, ApprovedList.StartupFolder, fileName),
                };
            })
            .ToList();
    }

    static RegistryHive HiveOf(EntrySource source) => source switch
    {
        EntrySource.FolderUser => RegistryHive.CurrentUser,
        EntrySource.FolderCommon => RegistryHive.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Not a startup folder source"),
    };
}
