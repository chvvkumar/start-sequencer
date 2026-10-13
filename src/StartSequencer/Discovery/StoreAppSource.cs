using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using StartSequencer.Model;

namespace StartSequencer.Discovery;

public sealed record StoreStartupTask(string PackageFamilyName, string TaskId, string DisplayName, string PackageDisplayName, string Aumid, string LogoPath, bool Enabled, bool DisabledByPolicy);

public static class StoreAppSource
{
    public const string RepositoryKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
    public const string SystemAppDataKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

    // StartupTaskState values: 0 Disabled, 1 DisabledByUser, 2 Enabled, 3 DisabledByPolicy, 4 EnabledByPolicy.
    private const int StateDisabledByUser = 1, StateEnabled = 2, StateDisabledByPolicy = 3, StateEnabledByPolicy = 4;

    public static IReadOnlyList<StoreStartupTask> ScanTasks()
    {
        var result = new Dictionary<string, StoreStartupTask>(StringComparer.OrdinalIgnoreCase);
        using var repo = Registry.CurrentUser.OpenSubKey(RepositoryKey);
        if (repo == null) return [];

        foreach (var fullName in repo.GetSubKeyNames())
        {
            try
            {
                using var pkg = repo.OpenSubKey(fullName);
                if (pkg == null) continue;
                var root = pkg.GetValue("PackageRootFolder") as string;
                if (string.IsNullOrEmpty(root)) continue;
                var manifestPath = Path.Combine(root, "AppxManifest.xml");
                if (!File.Exists(manifestPath)) continue;

                var parts = fullName.Split('_');
                var family = pkg.GetValue("PackageFamilyName") as string;
                if (string.IsNullOrEmpty(family)) family = parts.Length >= 2 ? parts[0] + "_" + parts[^1] : fullName;
                var pkgDisplay = pkg.GetValue("DisplayName") as string;
                pkgDisplay = ResolveName(fullName, parts[0], pkgDisplay) ?? parts[0];

                var doc = XDocument.Load(manifestPath);
                var logo = FindLogo(root, doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Logo" && e.Parent?.Name.LocalName == "Properties")?.Value);

                // Accepts uap5:Extension and desktop:Extension by matching local names only.
                foreach (var ext in doc.Descendants().Where(e => e.Name.LocalName == "Extension" && (string?)e.Attribute("Category") == "windows.startupTask"))
                {
                    var task = ext.Elements().FirstOrDefault(e => e.Name.LocalName == "StartupTask");
                    var taskId = (string?)task?.Attribute("TaskId");
                    if (string.IsNullOrEmpty(taskId)) continue;
                    var appId = (string?)ext.Ancestors().FirstOrDefault(e => e.Name.LocalName == "Application")?.Attribute("Id") ?? "App";
                    var display = (string?)task!.Attribute("DisplayName");
                    display = ResolveName(fullName, parts[0], display) ?? pkgDisplay;
                    var manifestEnabled = string.Equals((string?)task.Attribute("Enabled"), "true", StringComparison.OrdinalIgnoreCase);

                    var state = ReadState(family, taskId);
                    var enabled = state is null ? manifestEnabled : state is StateEnabled or StateEnabledByPolicy;
                    result[family + "!" + taskId] = new StoreStartupTask(family, taskId, display, pkgDisplay, family + "!" + appId, logo, enabled, state == StateDisabledByPolicy);
                }
            }
            catch
            {
                // Unreadable package or malformed manifest: skip it, keep scanning.
            }
        }
        return [.. result.Values.OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
    }

    public static void SetEnabled(string packageFamilyName, string taskId, bool enabled)
    {
        var path = SystemAppDataKey + "\\" + packageFamilyName + "\\" + taskId;
        var want = enabled ? StateEnabled : StateDisabledByUser;
        using (var key = Registry.CurrentUser.CreateSubKey(path, writable: true))
            key.SetValue("State", want, RegistryValueKind.DWord);
        if (ReadState(packageFamilyName, taskId) != want)
            throw new InvalidOperationException($"Read-back mismatch for HKCU\\{path}\\State, expected {want}");
    }

    public static IReadOnlyList<StartupEntry> Scan() =>
        [.. ScanTasks().Select(t => new StartupEntry
        {
            Key = EntryKey.Make(EntrySource.Store, t.PackageFamilyName + "!" + t.TaskId),
            Source = EntrySource.Store,
            Name = t.DisplayName,
            Publisher = t.PackageDisplayName,
            Location = "Store: " + t.PackageFamilyName,
            Command = "shell:AppsFolder\\" + t.Aumid,
            ExePath = t.LogoPath,
            Enabled = t.Enabled,
            ReadOnly = ReadState(t.PackageFamilyName, t.TaskId) is StateDisabledByPolicy or StateEnabledByPolicy,
            CanSequence = false,
            Aumid = t.Aumid,
        })];

    public static void RunNow(string aumid) =>
        Process.Start("explorer.exe", "shell:AppsFolder\\" + aumid)?.Dispose();

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string pszSource, StringBuilder pszOutBuf, int cchOutBuf, IntPtr ppvReserved);

    // Plain names pass through. ms-resource names resolve via "@{<fullName>?ms-resource://<name>/resources/<id>}";
    // the short "ms-resource:<id>" form fails with 0x80073B17. Null when empty or unresolved.
    private static string? ResolveName(string fullName, string packageName, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (!value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) return value;
        var uri = value.StartsWith("ms-resource://", StringComparison.OrdinalIgnoreCase)
            ? value
            : "ms-resource://" + packageName + "/resources/" + value["ms-resource:".Length..];
        var buf = new StringBuilder(1024);
        return SHLoadIndirectString("@{" + fullName + "?" + uri + "}", buf, buf.Capacity, IntPtr.Zero) == 0 && buf.Length > 0
            ? buf.ToString()
            : null;
    }

    private static int? ReadState(string family, string taskId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SystemAppDataKey + "\\" + family + "\\" + taskId);
        return key?.GetValue("State") is int v ? v : null;
    }

    // Packages ship scaled variants (Name.targetsize-32.png, Name.scale-100.png) beside or instead of the exact file.
    private static string FindLogo(string root, string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return "";
        var exact = Path.Combine(root, relative);
        var stem = Path.Combine(Path.GetDirectoryName(exact) ?? root, Path.GetFileNameWithoutExtension(exact));
        var ext = Path.GetExtension(exact);
        foreach (var c in new[] { stem + ".targetsize-32" + ext, stem + ".scale-100" + ext, exact })
            if (File.Exists(c)) return c;
        // Some packages ship only other scales (for example scale-200): take the first match.
        var dir = Path.GetDirectoryName(stem)!;
        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, Path.GetFileName(stem) + ".*" + ext).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? ""
            : "";
    }
}
