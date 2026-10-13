using Microsoft.Win32.TaskScheduler;
using StartSequencer.Model;

namespace StartSequencer.Discovery;

public static class TaskSchedulerSource
{
    public static IReadOnlyList<StartupEntry> Scan(bool includeMicrosoft)
    {
        var list = new List<StartupEntry>();
        using var ts = new TaskService();
        Walk(ts.RootFolder, includeMicrosoft, list);
        return list;
    }

    static void Walk(TaskFolder folder, bool includeMicrosoft, List<StartupEntry> list)
    {
        var path = folder.Path;
        if (IsOwnPath(path + "\\")) return;
        if (!includeMicrosoft && IsMicrosoftPath(path + "\\")) return;

        TaskCollection tasks;
        try { tasks = folder.Tasks; } catch { return; }
        foreach (var task in tasks)
        {
            try
            {
                var e = ToEntry(task, includeMicrosoft);
                if (e != null) list.Add(e);
            }
            catch { } // some tasks throw on Definition for permission reasons; skip them
        }

        TaskFolderCollection subs;
        try { subs = folder.SubFolders; } catch { return; }
        foreach (var sub in subs)
        {
            try { Walk(sub, includeMicrosoft, list); } catch { }
        }
    }

    static StartupEntry? ToEntry(Microsoft.Win32.TaskScheduler.Task task, bool includeMicrosoft)
    {
        var path = task.Path;
        if (IsOwnPath(path)) return null;
        var ms = IsMicrosoftPath(path);
        if (ms && !includeMicrosoft) return null;

        var def = task.Definition;
        if (!def.Triggers.Any(t => t.TriggerType == TaskTriggerType.Logon)) return null;

        var exec = def.Actions.OfType<ExecAction>().FirstOrDefault();
        var command = exec == null ? "" : string.IsNullOrEmpty(exec.Arguments) ? exec.Path : exec.Path + " " + exec.Arguments;
        var lastRun = task.LastRunTime;

        return new StartupEntry
        {
            Key = EntryKey.Make(EntrySource.Task, path),
            Source = EntrySource.Task,
            Name = task.Name,
            Command = command,
            // Set directly so a quoted or %var% action path is not re-split from Command.
            ExePath = exec == null ? "" : Environment.ExpandEnvironmentVariables(exec.Path.Trim('"')),
            Arguments = exec?.Arguments ?? "",
            WorkingDirectory = Environment.ExpandEnvironmentVariables(exec?.WorkingDirectory ?? ""),
            Location = path,
            Enabled = task.Enabled,
            ReadOnly = ms,
            // The launcher runs one program action; a COM handler or a second action would be lost.
            CanSequence = exec != null && def.Actions.Count == 1,
            LastRun = lastRun.Year > 1999 ? lastRun : null,
            Publisher = def.RegistrationInfo.Author ?? "",
            TaskPath = path,
        };
    }

    static bool IsOwnPath(string taskPath) =>
        taskPath.StartsWith(Sys.LogonTask.Folder + "\\", StringComparison.OrdinalIgnoreCase);

    public static bool IsMicrosoftPath(string taskPath) => taskPath.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);

    // UnauthorizedAccessException propagates; the caller routes it to elevated ops.
    public static void SetEnabled(string taskPath, bool enabled)
    {
        using var ts = new TaskService();
        Get(ts, taskPath).Enabled = enabled;
    }

    // Writes the current Enabled value back: succeeds only when this user may change the task.
    public static bool CanModify(string taskPath)
    {
        try
        {
            using var ts = new TaskService();
            var task = ts.GetTask(taskPath);
            if (task == null) return false;
            task.Enabled = task.Enabled;
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070005)) { return false; }
    }

    public static void Run(string taskPath)
    {
        using var ts = new TaskService();
        Get(ts, taskPath).Run();
    }

    public static bool IsEnabled(string taskPath)
    {
        using var ts = new TaskService();
        return Get(ts, taskPath).Enabled;
    }

    static Microsoft.Win32.TaskScheduler.Task Get(TaskService ts, string taskPath) =>
        ts.GetTask(taskPath) ?? throw new InvalidOperationException("Scheduled task not found: " + taskPath);
}
