using StartSequencer.Model;

namespace StartSequencer.Launch;

public sealed record AskItem(string Key, string Label, bool IsStage, bool Default);

public sealed record PlannedStep(ManagedEntry Entry, string StageName, double PlannedOffsetSeconds);

public static class SequencePlanner
{
    // An Ask stage is one item covering all its entries; Ask entries inside it are not listed separately.
    // A stage with no enabled entries is not asked about.
    public static IReadOnlyList<AskItem> AskItems(AppConfig c)
    {
        var enabled = c.Managed.Where(m => m.Enabled).ToList();
        var askStages = c.Stages.Where(s => s.Ask).Select(s => s.Name).ToHashSet();
        var items = new List<AskItem>();
        foreach (var s in c.Stages.Where(s => s.Ask && enabled.Any(m => m.Stage == s.Name)))
            items.Add(Item(c, EntryKey.StageKey(s.Name), s.Name, true));
        foreach (var m in Ordered(c).Where(m => m.Enabled && m.Ask && !askStages.Contains(m.Stage)))
        {
            var id = EntryKey.Split(m.Key).id;
            items.Add(Item(c, m.Key, id != "" ? id : m.Key, false));
        }
        return items;
    }

    static AskItem Item(AppConfig c, string key, string label, bool isStage) =>
        new(key, label, isStage, c.AskDefaults.TryGetValue(key, out var d) ? d : true);

    public static IReadOnlyList<PlannedStep> Plan(AppConfig c, IReadOnlySet<string> skippedKeys)
    {
        var steps = new List<PlannedStep>();
        double t = c.GlobalDelaySeconds;
        foreach (var m in Ordered(c))
        {
            if (!m.Enabled || skippedKeys.Contains(m.Key) || skippedKeys.Contains(EntryKey.StageKey(m.Stage))) continue;
            t += m.DelaySeconds;
            steps.Add(new PlannedStep(m, m.Stage, t));
            if (m.Wait == WaitMode.Fixed) t += m.WaitSeconds;
        }
        return steps;
    }

    public static double TotalPlannedSeconds(AppConfig c)
    {
        var plan = Plan(c, new HashSet<string>());
        if (plan.Count == 0) return c.GlobalDelaySeconds;
        var last = plan[^1];
        return last.PlannedOffsetSeconds + (last.Entry.Wait == WaitMode.Fixed ? last.Entry.WaitSeconds : 0);
    }

    // Stages in configured order, then entries whose stage no longer exists; Order within each.
    static IEnumerable<ManagedEntry> Ordered(AppConfig c)
    {
        var rank = c.Stages.Select((s, i) => (s.Name, i)).GroupBy(x => x.Name).ToDictionary(g => g.Key, g => g.First().i);
        return c.Managed.OrderBy(m => rank.TryGetValue(m.Stage, out var r) ? r : int.MaxValue).ThenBy(m => m.Order);
    }
}
