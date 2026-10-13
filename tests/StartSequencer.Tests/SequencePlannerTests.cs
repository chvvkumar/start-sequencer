using StartSequencer.Launch;
using StartSequencer.Model;
using Xunit;

public class SequencePlannerTests
{
    private static AppConfig Cfg()
    {
        var c = new AppConfig { GlobalDelaySeconds = 10 };
        c.Stages[1].Ask = true; // Comms
        c.Managed.Add(new ManagedEntry { Key = "a", Stage = "Core", Order = 1, DelaySeconds = 2, Wait = WaitMode.Fixed, WaitSeconds = 3 });
        c.Managed.Add(new ManagedEntry { Key = "b", Stage = "Core", Order = 0 });
        c.Managed.Add(new ManagedEntry { Key = "c", Stage = "Comms", Order = 0, Ask = true });
        c.Managed.Add(new ManagedEntry { Key = "d", Stage = "Background", Order = 0, Ask = true, DelaySeconds = 5 });
        c.Managed.Add(new ManagedEntry { Key = "e", Stage = "Background", Order = 1, Enabled = false });
        return c;
    }

    [Fact]
    public void AskItems_StageWins_OverEntryInsideIt()
    {
        var items = SequencePlanner.AskItems(Cfg());
        Assert.Contains(items, i => i.Key == "Stage|Comms" && i.IsStage);
        Assert.DoesNotContain(items, i => i.Key == "c");
        Assert.Contains(items, i => i.Key == "d" && !i.IsStage && i.Default);
    }

    [Fact]
    public void Plan_OrdersByStageThenOrder_SkipsDisabledAndSkipped_ComputesOffsets()
    {
        var plan = SequencePlanner.Plan(Cfg(), new HashSet<string> { "Stage|Comms" });
        Assert.Equal(new[] { "b", "a", "d" }, plan.Select(p => p.Entry.Key).ToArray());
        Assert.Equal(10, plan[0].PlannedOffsetSeconds);       // global only
        Assert.Equal(12, plan[1].PlannedOffsetSeconds);       // + a.delay 2
        Assert.Equal(20, plan[2].PlannedOffsetSeconds);       // + a.wait 3 + d.delay 5
        Assert.Equal(20, SequencePlanner.TotalPlannedSeconds(Cfg()));
    }
}
