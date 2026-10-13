namespace StartSequencer.Launch;

public static class ProcessTree
{
    // rootPid plus every transitive child. The visited set stops cycles caused by PID reuse.
    public static HashSet<int> Descendants(int rootPid, IReadOnlyList<(int pid, int parentPid, string exeName)> snapshot)
    {
        var children = snapshot.ToLookup(p => p.parentPid, p => p.pid);
        var seen = new HashSet<int> { rootPid };
        var stack = new Stack<int>([rootPid]);
        while (stack.Count > 0)
            foreach (var c in children[stack.Pop()])
                if (seen.Add(c)) stack.Push(c);
        return seen;
    }
}
