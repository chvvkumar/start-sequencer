using System.Windows;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using StartSequencer.Discovery;
using StartSequencer.Model;

namespace StartSequencer.Sys;

public static class Uninstaller
{
    const string Title = "StartSequencer";

    public static ElevatedResult Restore(AppConfig config)
    {
        var direct = new List<(string key, System.Action write)>();
        var elevated = new List<ElevatedOp>();
        var errors = new List<string>();

        // Nothing is written until the UAC prompt is answered.
        foreach (var (key, enabled) in config.NativeSnapshot)
        {
            var (prefix, id) = EntryKey.Split(key);
            try
            {
                if (Approved(prefix) is var (hive, list))
                {
                    // Skip values already in the snapshot state, so unchanged HKLM entries need no UAC prompt.
                    if (StartupApproved.Read(hive, list, id) == enabled) continue;
                    if (StartupApproved.RequiresElevation(hive))
                        elevated.Add(new ElevatedOp { Kind = ElevatedOpKind.SetApproved, Hive = StartupApproved.Abbrev(hive), List = list.ToString(), Name = id, Enabled = enabled });
                    else
                        direct.Add((key, () => StartupApproved.Write(hive, list, id, enabled)));
                }
                else if (prefix == "Task")
                {
                    // The app never writes \Microsoft\ tasks; Windows servicing changes them on its own.
                    if (TaskSchedulerSource.IsMicrosoftPath(id)) continue;
                    // A task deleted since the snapshot has nothing to restore.
                    if (TaskEnabled(id) is not { } current || current == enabled) continue;
                    if (TaskSchedulerSource.CanModify(id)) direct.Add((key, () => TaskSchedulerSource.SetEnabled(id, enabled)));
                    else elevated.Add(new ElevatedOp { Kind = ElevatedOpKind.SetTaskEnabled, Name = id, Enabled = enabled });
                }
                else if (prefix == "Store")
                {
                    var (family, taskId) = Scanner.SplitStoreId(id);
                    // No State value means nothing ever toggled it; 3 and 4 are policy states. Leave both alone.
                    var state = StoreState(family, taskId);
                    if (state is not (StoreDisabledByUser or StoreEnabled) || (state == StoreEnabled) == enabled) continue;
                    direct.Add((key, () => StoreAppSource.SetEnabled(family, taskId, enabled)));
                }
                // RunOnce keys have no switch and were never changed.
            }
            catch (Exception ex)
            {
                errors.Add(key + ": " + ex.Message);
            }
        }

        // Removes the elevated tasks, the logon task and both folders. Queued even when the folder is empty.
        try
        {
            if (ElevatedFolderExists())
                elevated.Add(new ElevatedOp { Kind = ElevatedOpKind.DeleteAllElevatedTasks });
        }
        catch (Exception ex) { errors.Add("Elevated tasks: " + ex.Message); }

        // The prompt comes first, as in Save: a declined prompt leaves every source and the logon task as they were.
        var result = ElevatedOps.RunElevated(elevated);
        if (!result.Ok && result.Errors is ["Cancelled"]) return result;
        errors.AddRange(result.Errors);

        foreach (var (key, write) in direct)
        {
            try { write(); }
            catch (Exception ex) { errors.Add(key + ": " + ex.Message); }
        }

        // A failed elevated step leaves managed HKLM entries disabled; keep the logon task so they still start until the user retries.
        if (result.Ok)
        {
            try { LogonTask.Unregister(); }
            catch (Exception ex) { errors.Add("Logon task: " + ex.Message); }
        }
        else errors.Add("Logon task kept because the administrator step failed. Run uninstall again.");

        return new ElevatedResult { Ok = errors.Count == 0, Errors = errors };
    }

    const int StoreDisabledByUser = 1, StoreEnabled = 2;

    static int? StoreState(string family, string taskId)
    {
        using var key = Registry.CurrentUser.OpenSubKey(StoreAppSource.SystemAppDataKey + "\\" + family + "\\" + taskId);
        return key?.GetValue("State") is int v ? v : null;
    }

    static bool? TaskEnabled(string taskPath)
    {
        using var ts = new TaskService();
        return ts.GetTask(taskPath)?.Enabled;
    }

    static bool ElevatedFolderExists()
    {
        using var ts = new TaskService();
        try { ts.GetFolder(LogonTask.ElevatedFolder); return true; }
        catch (FileNotFoundException) { return false; }
    }

    static (RegistryHive hive, ApprovedList list)? Approved(string prefix) => prefix switch
    {
        "HKCU-Run" => (RegistryHive.CurrentUser, ApprovedList.Run),
        "HKCU-Run32" => (RegistryHive.CurrentUser, ApprovedList.Run32),
        "HKLM-Run" => (RegistryHive.LocalMachine, ApprovedList.Run),
        "HKLM-Run32" => (RegistryHive.LocalMachine, ApprovedList.Run32),
        "Folder-User" => (RegistryHive.CurrentUser, ApprovedList.StartupFolder),
        "Folder-Common" => (RegistryHive.LocalMachine, ApprovedList.StartupFolder),
        _ => null,
    };

    // --uninstall entry.
    public static void Run()
    {
        AppConfig config;
        try { config = ConfigStore.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show("Cannot read the configuration file.\n\n" + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (MessageBox.Show("Restore all startup entries to their original state and remove StartSequencer scheduled tasks?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var result = Restore(config);

        if (result.Errors is ["Cancelled"])
        {
            MessageBox.Show("Administrator approval was cancelled. Nothing was changed.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Settings hold the snapshot needed to retry, so keep them when anything failed.
        if (result.Errors.Count > 0)
        {
            MessageBox.Show("These items were not restored:\n\n" + string.Join("\n", result.Errors) +
                "\n\nSettings were kept. Run uninstall again to retry.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var summary = "Startup entries restored and scheduled tasks removed.";
        try
        {
            if (Directory.Exists(ConfigStore.RootDir) &&
                MessageBox.Show($"Delete settings and logs in {ConfigStore.RootDir}?", Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Directory.Delete(ConfigStore.RootDir, true);
                summary += "\n\nSettings and logs deleted.";
            }
            else
            {
                // Kept settings must not show entries as managed once their source switches are restored.
                config.Managed.Clear();
                config.NativeSnapshot.Clear();
                config.SequencerEnabled = false;
                ConfigStore.Save(config);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(summary + "\n\nSettings folder: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(summary, Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
