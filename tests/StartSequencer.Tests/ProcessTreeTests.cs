using StartSequencer.Launch;
using Xunit;

public class ProcessTreeTests
{
    [Fact]
    public void Descendants_IncludesGrandchildren_AndSelf()
    {
        var snap = new List<(int, int, string)> { (1, 0, "a"), (2, 1, "b"), (3, 2, "c"), (4, 9, "d"), (5, 3, "e") };
        var d = ProcessTree.Descendants(1, snap);
        Assert.Equal(new HashSet<int> { 1, 2, 3, 5 }, d);
    }

    [Fact]
    public void Descendants_SurvivesPidReuseCycle()
    {
        var snap = new List<(int, int, string)> { (1, 2, "a"), (2, 1, "b") };
        Assert.Equal(new HashSet<int> { 1, 2 }, ProcessTree.Descendants(1, snap));
    }

    [Fact]
    public async Task CpuGate_ReturnsWhenAverageBelow()
    {
        var samples = new Queue<float>(new float[] { 90, 90, 10, 10, 10, 10, 10 });
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await CpuGate.WaitBelowAsync(50, capSeconds: 30, () => samples.Count > 0 ? samples.Dequeue() : 10, CancellationToken.None);
        Assert.InRange(sw.Elapsed.TotalSeconds, 4, 9);   // needs 5 samples, averages fall below 50 at sample 6 or 7
    }

    [Fact]
    public async Task CpuGate_StopsAtCap()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await CpuGate.WaitBelowAsync(1, capSeconds: 2, () => 100, CancellationToken.None);
        Assert.InRange(sw.Elapsed.TotalSeconds, 1.5, 4);
    }
}
