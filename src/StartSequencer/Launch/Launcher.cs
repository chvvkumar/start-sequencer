using System.Diagnostics;
using System.Windows;
using StartSequencer.Discovery;
using StartSequencer.Model;
using StartSequencer.Sys;

namespace StartSequencer.Launch;

public sealed class Launcher
{
    private readonly IProcessRunner _runner;
    private readonly Func<IReadOnlyList<AskItem>, (IReadOnlySet<string> selected, bool timedOut)>? _ask;

    public Launcher() : this(new ProcessRunner(), null) { }

    // ask null shows AskWindow on the UI thread.
    public Launcher(IProcessRunner runner, Func<IReadOnlyList<AskItem>, (IReadOnlySet<string> selected, bool timedOut)>? ask)
    {
        _runner = runner;
        _ask = ask;
    }

    // Never throws: each stage records its failure in the log, and the log is always written.
    public async Task<BootLog> RunAsync(AppConfig? config = null, CancellationToken ct = default)
    {
        var log = new BootLog { LauncherStart = DateTime.Now };
        var notes = new List<string>();
        try { log.LogonTime = Native.LogonTime() ?? log.LauncherStart; }
        catch { log.LogonTime = log.LauncherStart; }

        try
        {
            config ??= ConfigStore.Load();
            if (config.SequencerEnabled)
                await RunSequenceAsync(config, log, notes, ct);
        }
        catch (Exception e)
        {
            notes.Add(e.Message);
        }

        if (log.SequenceStart == default) log.SequenceStart = DateTime.Now;
        log.SequenceEnd = DateTime.Now;
        // BootLog has no free-text field, so launcher-level failures go on a synthetic entry.
        if (notes.Count > 0)
            log.Entries.Add(new BootLogEntry { Key = "Launcher", Name = "Launcher", Result = LaunchResult.Failed, Reason = string.Join("; ", notes) });
        try { BootLog.Write(log); } catch { }
        return log;
    }

    private async Task RunSequenceAsync(AppConfig config, BootLog log, List<string> notes, CancellationToken ct)
    {
        // Cancellation during the global delay falls through: the step loop logs every step as cancelled.
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, config.GlobalDelaySeconds)), ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }

        // Scan before asking so the dialog shows display names instead of raw keys.
        var entries = new Dictionary<string, StartupEntry>();
        try
        {
            var scan = Scanner.Scan(config);
            // Per-source scan errors are not recorded: an entry from a failed source is logged Missing on its own.
            foreach (var e in scan.Entries)
                if (e.Location != Scanner.OrphanLocation) entries.TryAdd(e.Key, e);
        }
        catch (Exception e) { notes.Add("Scan failed: " + e.Message); }

        var skipped = new HashSet<string>();
        try
        {
            var items = SequencePlanner.AskItems(config)
                .Select(i => !i.IsStage && entries.TryGetValue(i.Key, out var se) && se.Name != "" ? i with { Label = se.Name } : i)
                .ToList();
            if (items.Count > 0 && !ct.IsCancellationRequested)
            {
                var (selected, timedOut) = _ask is not null ? _ask(items) : await ShowAskWindowAsync(items, config.AskCountdownSeconds);
                foreach (var i in items)
                {
                    if (selected.Contains(i.Key)) log.AskSelected.Add(i.Key);
                    else { log.AskSkipped.Add(i.Key); skipped.Add(i.Key); }
                }
                log.AskTimedOut = timedOut;
                if (!timedOut)
                {
                    foreach (var i in items) config.AskDefaults[i.Key] = selected.Contains(i.Key);
                    try { ConfigStore.Save(config); }
                    catch (Exception e) { notes.Add("Saving ask defaults failed: " + e.Message); }
                }
            }
        }
        catch (Exception e) { notes.Add("Ask dialog failed: " + e.Message); }

        log.SequenceStart = DateTime.Now;
        foreach (var step in SequencePlanner.Plan(config, skipped))
        {
            var m = step.Entry;
            var entry = new BootLogEntry { Key = m.Key, Name = m.Key, Stage = step.StageName, PlannedOffsetSeconds = step.PlannedOffsetSeconds };
            if (entries.TryGetValue(m.Key, out var known) && known.Name != "") entry.Name = known.Name;
            if (ct.IsCancellationRequested)
            {
                entry.Result = LaunchResult.Skipped;
                entry.Reason = "cancelled";
                log.Entries.Add(entry);
                continue;
            }
            try
            {
                if (m.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(m.DelaySeconds), ct);
                if (m.CpuBelowPercent is int pct)
                {
                    var gate = new CpuGate();
                    await CpuGate.WaitBelowAsync(pct, m.CpuWaitCapSeconds, gate.Sample, ct);
                }

                if (!entries.TryGetValue(m.Key, out var se))
                    Apply(entry, new(LaunchResult.Missing, DateTime.Now, null, null, "entry not found"));
                else
                    Apply(entry, await LaunchAsync(m, se, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                entry.Result = LaunchResult.Skipped;
                entry.Reason = "cancelled";
            }
            catch (Exception e)
            {
                entry.Result = LaunchResult.Failed;
                entry.Reason = e.Message;
            }
            log.Entries.Add(entry);
        }
    }

    private static void Apply(BootLogEntry entry, LaunchOutcome o)
    {
        entry.Result = o.Result;
        entry.LaunchedAt = o.LaunchedAt;
        entry.ReadyAt = o.ReadyAt;
        entry.ExitCode = o.ExitCode;
        entry.Reason = o.Reason;
    }

    private async Task<LaunchOutcome> LaunchAsync(ManagedEntry m, StartupEntry se, CancellationToken ct)
    {
        if (se.Source == EntrySource.Store)
            return new(LaunchResult.Skipped, DateTime.Now, null, null, "store");

        // Managed tasks are disabled at source and Task Scheduler refuses to run a disabled task,
        // so the task's program action runs as a plain process like any other entry.
        if (se.Source == EntrySource.Task && se.ExePath.Length == 0)
            return new(LaunchResult.Missing, DateTime.Now, null, null, "task has no program action");

        // The elevated task would start nothing and still report Started.
        if (m.Elevated && !File.Exists(se.ExePath) && !ProcessRunner.OnPath(se.ExePath))
            return new(LaunchResult.Missing, DateTime.Now, null, null, "File not found: " + se.ExePath);

        if (m.Elevated)
        {
            var at = DateTime.Now;
            TaskSchedulerSource.Run(LogonTask.ElevatedTaskPath(m.Key));
            var image = Path.GetFileNameWithoutExtension(se.ExePath);
            switch (m.Wait)
            {
                case WaitMode.Fixed:
                    await Task.Delay(TimeSpan.FromSeconds(m.WaitSeconds), ct);
                    return new(LaunchResult.Started, at, null, null, "");
                case WaitMode.Window:
                    return await PollAsync(() => { var p = StartedSince(image, at); return p.Count > 0 && Native.HasVisibleTopLevelWindow(p); }, m.WindowTimeoutSeconds, ct)
                        ? new(LaunchResult.WindowSeen, at, DateTime.Now, null, "")
                        : new(LaunchResult.WindowTimeout, at, null, null, $"No window after {m.WindowTimeoutSeconds} s");
                case WaitMode.Exit:
                    // The process has to appear before its absence means it exited.
                    var seen = false;
                    return await PollAsync(() => { var any = StartedSince(image, at).Count > 0; seen |= any; return seen && !any; }, m.WindowTimeoutSeconds, ct)
                        ? new(LaunchResult.Exited, at, DateTime.Now, null, "")
                        : new(LaunchResult.ExitTimeout, at, null, null, seen ? $"Still running after {m.WindowTimeoutSeconds} s" : "Process not seen");
                default:
                    return new(LaunchResult.Started, at, null, null, "");
            }
        }

        return await _runner.RunAsync(se.ExePath, se.Arguments, se.WorkingDirectory, m.Wait, m.WaitSeconds, m.WindowTimeoutSeconds, ct);
    }

    // Processes with this image name started at or after the trigger. A small margin covers clock granularity.
    private static HashSet<int> StartedSince(string image, DateTime at)
    {
        var pids = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(image))
        {
            try { if (p.StartTime >= at.AddSeconds(-1)) pids.Add(p.Id); } catch { }
            finally { p.Dispose(); }
        }
        return pids;
    }

    private static async Task<bool> PollAsync(Func<bool> done, int timeoutSeconds, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            if (done()) return true;
            if (sw.Elapsed.TotalSeconds >= timeoutSeconds) return false;
            await Task.Delay(500, ct);
        }
    }

    // Show() rather than ShowDialog(): ShowDialog always activates and would ignore ShowActivated,
    // taking focus from a fullscreen app.
    private static async Task<(IReadOnlySet<string> selected, bool timedOut)> ShowAskWindowAsync(IReadOnlyList<AskItem> items, int countdownSeconds)
    {
        var app = Application.Current;
        if (app is null) return (items.Where(i => i.Default).Select(i => i.Key).ToHashSet(), true);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var w = await app.Dispatcher.InvokeAsync(() =>
        {
            var w = new AskWindow(items, items.Select(i => (i.Key, i.Label)).ToList(), countdownSeconds);
            w.Closed += (_, _) => done.TrySetResult();
            w.Show();
            return w;
        });
        await done.Task;
        return (w.Selected, w.TimedOut);
    }
}
