using System.Diagnostics;
using StartSequencer.Sys;

namespace StartSequencer.Launch;

public sealed class CpuGate
{
    // GetSystemTimes deltas instead of PerformanceCounter: same total CPU figure, no extra package.
    private (long idle, long total) _last = Native.SystemTimes();

    public CpuGate() { }

    // Percent busy across all cores since the previous call (or construction).
    public float Sample()
    {
        var now = Native.SystemTimes();
        var idle = now.idle - _last.idle;
        var total = now.total - _last.total;
        _last = now;
        return total <= 0 ? 0 : (float)(100.0 * (total - idle) / total);
    }

    public static async Task WaitBelowAsync(int percent, int capSeconds, Func<float> sample, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var window = new Queue<float>();
        while (sw.Elapsed.TotalSeconds < capSeconds)
        {
            await Task.Delay(1000, ct);
            window.Enqueue(sample());
            if (window.Count > 5) window.Dequeue();
            if (window.Count == 5 && window.Average() < percent) return;
        }
    }
}
