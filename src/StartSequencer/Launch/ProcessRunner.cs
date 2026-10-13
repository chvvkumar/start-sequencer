using System.Diagnostics;
using StartSequencer.Model;
using StartSequencer.Sys;

namespace StartSequencer.Launch;

public enum LaunchResult { Started, WindowSeen, Exited, WindowTimeout, ExitTimeout, Missing, Failed, Skipped }

public sealed record LaunchOutcome(LaunchResult Result, DateTime LaunchedAt, DateTime? ReadyAt, int? ExitCode, string Reason);

public interface IProcessRunner
{
    Task<LaunchOutcome> RunAsync(string exePath, string arguments, string workingDirectory, WaitMode wait, int waitSeconds, int timeoutSeconds, CancellationToken ct);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<LaunchOutcome> RunAsync(string exePath, string arguments, string workingDirectory, WaitMode wait, int waitSeconds, int timeoutSeconds, CancellationToken ct)
    {
        var now = DateTime.Now;
        if (!File.Exists(exePath) && !OnPath(exePath))
            return new(LaunchResult.Missing, now, null, null, "File not found: " + exePath);

        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory != "" ? workingDirectory : Path.GetDirectoryName(exePath) ?? "",
            UseShellExecute = true,   // honours .lnk, .bat and other file associations
        };
        if (exePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "powershell.exe";
            psi.Arguments = $"-ExecutionPolicy Bypass -File \"{exePath}\" {arguments}".TrimEnd();
        }

        Process? p;
        var launchedAt = DateTime.Now;
        try { p = Process.Start(psi); }
        catch (Exception e) { return new(LaunchResult.Failed, launchedAt, null, null, e.Message); }

        using (p)
        {
            if (p is null)   // shell handed the file to an already running process
                return new(LaunchResult.Started, launchedAt, null, null, "No process handle returned by the shell");

            switch (wait)
            {
                case WaitMode.Fixed:
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
                    return new(LaunchResult.Started, launchedAt, null, null, "");
                case WaitMode.Window:
                    return await WaitWindowAsync(p, launchedAt, timeoutSeconds, ct);
                case WaitMode.Exit:
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                        try { await p.WaitForExitAsync(cts.Token); }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            return new(LaunchResult.ExitTimeout, launchedAt, null, null, $"Still running after {timeoutSeconds} s");
                        }
                        return new(LaunchResult.Exited, launchedAt, DateTime.Now, p.ExitCode, "");
                    }
                default:
                    return new(LaunchResult.Started, launchedAt, null, null, "");
            }
        }
    }

    // Polls the launched process and everything it spawned. Stub launchers exit fast and leave a child
    // that owns the window, so PIDs seen once are kept: a grandchild stays reachable after its parent exits.
    private static async Task<LaunchOutcome> WaitWindowAsync(Process p, DateTime launchedAt, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var known = new HashSet<int> { p.Id };
        while (true)
        {
            var snap = Native.ProcessSnapshot();
            var alive = snap.Select(s => s.pid).ToHashSet();
            foreach (var pid in known.ToList()) known.UnionWith(ProcessTree.Descendants(pid, snap));
            known.IntersectWith(alive);
            if (!p.HasExited) known.Add(p.Id);

            if (known.Count > 0 && Native.HasVisibleTopLevelWindow(known))
                return new(LaunchResult.WindowSeen, launchedAt, DateTime.Now, null, "");
            if (known.Count == 0)
                return new(LaunchResult.Exited, launchedAt, DateTime.Now, p.ExitCode, "Exited without showing a window");
            if (sw.Elapsed.TotalSeconds >= timeoutSeconds)
                return new(LaunchResult.WindowTimeout, launchedAt, null, null, $"No window after {timeoutSeconds} s");
            await Task.Delay(500, ct);
        }
    }

    // Bare names such as "notepad" or "cmd.exe" resolve through PATH and PATHEXT.
    // App Paths registry aliases are not checked; add if an entry uses one.
    internal static bool OnPath(string name)
    {
        if (name == "" || name.IndexOfAny(['\\', '/', ':']) >= 0) return false;
        var exts = new[] { "" }.Concat((Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE").Split(';', StringSplitOptions.RemoveEmptyEntries));
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => exts.Any(ext => File.Exists(Path.Combine(dir.Trim(), name + ext))));
    }
}
