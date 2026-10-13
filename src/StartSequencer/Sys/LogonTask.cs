using System.Security.Principal;
using Microsoft.Win32.TaskScheduler;

namespace StartSequencer.Sys;

public static class LogonTask
{
    public const string Folder = @"\StartSequencer";
    public const string TaskPath = @"\StartSequencer\Logon";
    public const string ElevatedFolder = @"\StartSequencer\Elevated";

    public static string ExePath { get; } = Environment.ProcessPath ?? "";

    public static bool IsRegistered()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskPath) != null;
    }

    // Program path of the registered logon task: null when there is no task, "" when it has no program action.
    public static string? RegisteredExePath()
    {
        using var ts = new TaskService();
        return ts.GetTask(TaskPath) is { } t ? t.Definition.Actions.OfType<ExecAction>().FirstOrDefault()?.Path ?? "" : null;
    }

    public static void Register()
    {
        var user = WindowsIdentity.GetCurrent().Name;
        using var ts = new TaskService();
        var def = NewDefinition(ts, user, TaskRunLevel.LUA);
        def.Triggers.Add(new LogonTrigger { UserId = user, Delay = TimeSpan.Zero });
        def.Actions.Add(new ExecAction(ExePath, "--launch", Path.GetDirectoryName(ExePath)));
        var folder = GetFolder(ts, Folder) ?? ts.RootFolder.CreateFolder(Folder, null, false);
        folder.RegisterTaskDefinition("Logon", def, TaskCreation.CreateOrUpdate, user, null, TaskLogonType.InteractiveToken);
    }

    public static void Unregister()
    {
        using var ts = new TaskService();
        if (ts.GetTask(TaskPath) != null) ts.RootFolder.DeleteTask(TaskPath, false);
        DeleteFolderIfEmpty(ts);
    }

    static TaskFolder? GetFolder(TaskService ts, string path)
    {
        try { return ts.GetFolder(path); } catch (FileNotFoundException) { return null; }
    }

    static void DeleteFolderIfEmpty(TaskService ts)
    {
        var folder = GetFolder(ts, Folder);
        if (folder != null && folder.Tasks.Count == 0 && folder.SubFolders.Count == 0)
            ts.RootFolder.DeleteFolder(Folder.TrimStart('\\'), false);
    }

    public static string ElevatedTaskPath(string entryKey) => ElevatedFolder + "\\" + Sanitize(entryKey);

    static string Sanitize(string key)
    {
        var chars = key.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (@"\/:*?""<>|".Contains(chars[i])) chars[i] = '_';
        return new string(chars);
    }

    // Requires admin.
    public static void CreateElevated(string entryKey, string exePath, string arguments, string workingDirectory)
    {
        var user = WindowsIdentity.GetCurrent().Name;
        using var ts = new TaskService();
        var folder = GetFolder(ts, ElevatedFolder) ?? ts.RootFolder.CreateFolder(ElevatedFolder, null, false);
        var def = NewDefinition(ts, user, TaskRunLevel.Highest);
        def.Actions.Add(new ExecAction(exePath, arguments, workingDirectory));
        folder.RegisterTaskDefinition(Sanitize(entryKey), def, TaskCreation.CreateOrUpdate, user, null, TaskLogonType.InteractiveToken);
    }

    public static void DeleteElevated(string entryKey)
    {
        using var ts = new TaskService();
        GetFolder(ts, ElevatedFolder)?.DeleteTask(Sanitize(entryKey), false);
    }

    // Uninstall only, requires admin: the elevated tasks, the logon task and both folders.
    // The logon task goes here too, so an admin-created \StartSequencer folder is removed elevated.
    public static void DeleteAllElevated()
    {
        using var ts = new TaskService();
        if (GetFolder(ts, ElevatedFolder) is { } folder)
        {
            foreach (var name in folder.Tasks.Select(t => t.Name).ToList()) folder.DeleteTask(name, false);
            GetFolder(ts, Folder)?.DeleteFolder("Elevated", false);
        }
        if (ts.GetTask(TaskPath) != null) ts.RootFolder.DeleteTask(TaskPath, false);
        DeleteFolderIfEmpty(ts);
    }

    public static IReadOnlyList<string> ListElevatedKeys()
    {
        using var ts = new TaskService();
        var folder = GetFolder(ts, ElevatedFolder);
        return folder == null ? [] : folder.Tasks.Select(t => t.Name).ToList();
    }

    static TaskDefinition NewDefinition(TaskService ts, string user, TaskRunLevel runLevel)
    {
        var def = ts.NewTask();
        def.Principal.UserId = user;
        def.Principal.LogonType = TaskLogonType.InteractiveToken;
        def.Principal.RunLevel = runLevel;
        def.Settings.DisallowStartIfOnBatteries = false;
        def.Settings.StopIfGoingOnBatteries = false;
        def.Settings.ExecutionTimeLimit = TimeSpan.Zero;
        def.Settings.MultipleInstances = TaskInstancesPolicy.IgnoreNew;
        def.Settings.StartWhenAvailable = true;
        return def;
    }
}
