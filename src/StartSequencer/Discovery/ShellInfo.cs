using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartSequencer.Discovery;

public static class CommandLine
{
    // Quoted: exe is the text inside the leading quotes. Unquoted: exe is the shortest
    // space-separated prefix that exists as a file (after env var expansion), else the first token.
    public static (string exe, string args) Split(string commandLine)
    {
        var s = (commandLine ?? "").Trim();
        if (s.Length == 0) return ("", "");

        if (s[0] == '"')
        {
            var close = s.IndexOf('"', 1);
            return close < 0 ? (s[1..], "") : (s[1..close], s[(close + 1)..].Trim());
        }

        for (var i = s.IndexOf(' '); i >= 0; i = s.IndexOf(' ', i + 1))
        {
            var prefix = s[..i];
            if (File.Exists(Environment.ExpandEnvironmentVariables(prefix)))
                return (prefix, s[(i + 1)..].Trim());
        }
        if (File.Exists(Environment.ExpandEnvironmentVariables(s))) return (s, "");

        var sp = s.IndexOf(' ');
        return sp < 0 ? (s, "") : (s[..sp], s[(sp + 1)..].Trim());
    }

    public static string ResolveLnk(string lnkPath) => ReadLnk(lnkPath).target;

    public static (string target, string args, string workDir) ReadLnk(string lnkPath)
    {
        object? shell = null, link = null;
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type == null) return ("", "", "");
            shell = Activator.CreateInstance(type);
            dynamic sh = shell!;
            link = sh.CreateShortcut(lnkPath);
            dynamic l = link!;
            return ((string)l.TargetPath ?? "", (string)l.Arguments ?? "", (string)l.WorkingDirectory ?? "");
        }
        catch
        {
            return ("", "", "");
        }
        finally
        {
            if (link != null && Marshal.IsComObject(link)) Marshal.ReleaseComObject(link);
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}

public static class ShellInfo
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    // Small shell icon for an exe or .lnk; a .png path (Store logo) is loaded as a bitmap. Null on failure.
    public static ImageSource? Icon(string exePath) =>
        string.IsNullOrEmpty(exePath) ? null : Cache.GetOrAdd(exePath, LoadIcon);

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            var p = Environment.ExpandEnvironmentVariables(path);
            if (p.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                if (!File.Exists(p)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }

            var info = new SHFILEINFO();
            SHGetFileInfo(p, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
            if (info.hIcon == IntPtr.Zero) return null;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally { DestroyIcon(info.hIcon); }
        }
        catch
        {
            return null;
        }
    }

    public static string Publisher(string exePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(Environment.ExpandEnvironmentVariables(exePath)).CompanyName ?? "";
        }
        catch
        {
            return "";
        }
    }
}
