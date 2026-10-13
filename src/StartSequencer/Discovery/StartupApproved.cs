using Microsoft.Win32;

namespace StartSequencer.Discovery;

public enum ApprovedList { Run, Run32, StartupFolder }

// Explorer's StartupApproved values, the same data Task Manager reads and writes.
// First byte: bit 0 clear = enabled (0x02, 0x06), bit 0 set = disabled (0x03).
// Disabled blobs carry the FILETIME of the change at offset 4.
public static class StartupApproved
{
    public const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static bool IsEnabled(byte[]? blob) => blob is not { Length: > 0 } || (blob[0] & 1) == 0;

    public static byte[] EnabledBlob()
    {
        var b = new byte[12];
        b[0] = 0x02;
        return b;
    }

    public static byte[] DisabledBlob(DateTime utcNow)
    {
        var b = new byte[12];
        b[0] = 0x03;
        BitConverter.GetBytes(utcNow.ToFileTimeUtc()).CopyTo(b, 4);
        return b;
    }

    public static bool Read(RegistryHive hive, ApprovedList list, string valueName)
    {
        using var root = OpenRoot(hive);
        using var key = root.OpenSubKey(SubKey + list);
        return IsEnabled(key?.GetValue(valueName) as byte[]);
    }

    public static void Write(RegistryHive hive, ApprovedList list, string valueName, bool enabled)
    {
        var blob = enabled ? EnabledBlob() : DisabledBlob(DateTime.UtcNow);
        var path = SubKey + list;
        using var root = OpenRoot(hive);
        using (var key = root.CreateSubKey(path, writable: true))
            key.SetValue(valueName, blob, RegistryValueKind.Binary);

        using var check = root.OpenSubKey(path);
        if (check?.GetValue(valueName) is not byte[] back || !back.AsSpan().SequenceEqual(blob))
            throw new InvalidOperationException($@"Read-back mismatch at {Abbrev(hive)}\{path}\{valueName}");
    }

    public static bool RequiresElevation(RegistryHive hive) => hive == RegistryHive.LocalMachine;

    internal static RegistryKey OpenRoot(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

    internal static string Abbrev(RegistryHive hive) => hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU";
}
