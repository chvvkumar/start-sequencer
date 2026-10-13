using StartSequencer.Model;
using StartSequencer.UI;
using Xunit;

public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ss-vm-" + Guid.NewGuid().ToString("N"));
    public MainViewModelTests() { ConfigStore.SetRootForTests(_dir); }
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    private static MainViewModel Vm()
    {
        var c = new AppConfig();
        c.Custom.Add(new CustomEntry { Key = "Custom|a", Name = "A", Path = @"C:\Windows\notepad.exe" });
        c.Custom.Add(new CustomEntry { Key = "Custom|b", Name = "B", Path = @"C:\Windows\notepad.exe" });
        return new MainViewModel(c);
    }

    // Rows also hold the real machine's entries; tests touch only the two custom rows so Save never writes a source.
    private static EntryRow Row(MainViewModel vm, string key) => vm.Rows.Single(r => r.Entry.Key == key);

    [Fact]
    public void Manage_Move_Unmanage_RenumbersOrder_AndDirty()
    {
        var vm = Vm();
        var a = Row(vm, "Custom|a");
        var b = Row(vm, "Custom|b");
        Assert.False(vm.IsDirty);
        vm.Manage(a, "Core", 0);
        vm.Manage(b, "Core", 0);
        Assert.Equal(0, b.Managed!.Order);
        Assert.Equal(1, a.Managed!.Order);
        Assert.True(vm.IsDirty);
        vm.Move(a, "Comms", 0);
        Assert.Equal("Comms", a.Managed!.Stage);
        Assert.Equal(0, b.Managed!.Order);
        vm.Unmanage(b);
        Assert.Null(b.Managed);
        Assert.Equal(MainViewModel.NativeGroup, b.GroupName);
    }

    [Fact]
    public void RemoveStage_WithRows_Throws()
    {
        var vm = Vm();
        vm.Manage(Row(vm, "Custom|a"), "Core", 0);
        Assert.Throws<InvalidOperationException>(() => vm.RemoveStage("Core"));
    }

    [Fact]
    public void RenameStage_UpdatesManagedEntries()
    {
        var vm = Vm();
        var a = Row(vm, "Custom|a");
        vm.Manage(a, "Core", 0);
        vm.RenameStage("Core", "First");
        Assert.Equal("First", a.Managed!.Stage);
        Assert.Equal("First", vm.Config.Stages[0].Name);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditThenRevert_ClearsDirty_StageMoveIsDirty()
    {
        var vm = Vm();
        var a = Row(vm, "Custom|a");
        vm.Manage(a, "Core", 0);
        vm.Unmanage(a);
        Assert.False(vm.IsDirty);
        vm.MoveStage("Core", 1);
        Assert.Equal("Core", vm.StageNames[1]);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void DropIndex_AfterNextRow_CountsWithoutMovingRow()
    {
        var c = new AppConfig();
        foreach (var k in new[] { "a", "b", "c" })
            c.Custom.Add(new CustomEntry { Key = "Custom|" + k, Name = k, Path = @"C:\Windows\notepad.exe" });
        var vm = new MainViewModel(c);
        var (a, b, cc) = (Row(vm, "Custom|a"), Row(vm, "Custom|b"), Row(vm, "Custom|c"));
        vm.Manage(a, "Core", 0);
        vm.Manage(b, "Core", 1);
        vm.Manage(cc, "Core", 2);
        vm.Move(a, "Core", vm.DropIndex(a, b.Managed!, "Core", after: true));
        Assert.Equal(["Custom|b", "Custom|a", "Custom|c"], vm.Config.Managed.OrderBy(m => m.Order).Select(m => m.Key));
    }

    [Fact]
    public void Import_KeepsLocalSnapshotAndKnownKeys()
    {
        var vm = Vm();
        vm.Config.NativeSnapshot["HKCU-Run|Local"] = false;
        var other = new AppConfig { GlobalDelaySeconds = 42 };
        other.NativeSnapshot["HKCU-Run|Local"] = true;
        other.KnownKeys.Add("HKCU-Run|Elsewhere");
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "import.json");
        ConfigStore.Export(other, path);
        vm.ImportFrom(path);
        Assert.Equal(42, vm.Config.GlobalDelaySeconds);
        Assert.False(vm.Config.NativeSnapshot["HKCU-Run|Local"]);
        Assert.DoesNotContain("HKCU-Run|Elsewhere", vm.Config.KnownKeys);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void CanModify_OwnTaskTrue_WindowsTaskFalse()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
        var def = ts.NewTask();
        def.Triggers.Add(new Microsoft.Win32.TaskScheduler.LogonTrigger { UserId = System.Security.Principal.WindowsIdentity.GetCurrent().Name });
        def.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(@"C:\Windows\notepad.exe"));
        ts.RootFolder.RegisterTaskDefinition(name, def);
        try
        {
            Assert.True(StartSequencer.Discovery.TaskSchedulerSource.CanModify("\\" + name));
            var windowsTask = ts.GetTask(@"\Microsoft\Windows\Defrag\ScheduledDefrag")?.Path
                ?? ts.AllTasks.First(t => t.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase)).Path;
            Assert.False(StartSequencer.Discovery.TaskSchedulerSource.CanModify(windowsTask));
        }
        finally { ts.RootFolder.DeleteTask(name, false); }
    }

    [Fact]
    public void Save_WithOnlyCustomEntries_PersistsAndClearsDirty()
    {
        var vm = Vm();
        vm.Manage(Row(vm, "Custom|a"), "Core", 0);
        var r = vm.Save();
        try
        {
            Assert.True(r.Ok, string.Join(";", r.Errors));
            Assert.False(vm.IsDirty);
            Assert.Single(ConfigStore.Load().Managed);
            Assert.True(ConfigStore.Load().SequencerEnabled);
            Assert.True(StartSequencer.Sys.LogonTask.IsRegistered());
        }
        finally { StartSequencer.Sys.LogonTask.Unregister(); }
    }

    [Fact]
    public void Save_CorruptConfigFile_DoesNotThrow_AndReplacesIt()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(ConfigStore.ConfigPath, "{");
        var vm = Vm();
        var r = vm.Save();
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Assert.Equal(2, ConfigStore.Load().Custom.Count);
    }

    [Fact]
    public void FailedKeys_GlobalElevatedFailure_MarksEveryElevatedKey()
    {
        var set = new StartSequencer.Sys.ElevatedOp { Kind = StartSequencer.Sys.ElevatedOpKind.SetApproved, Name = "x" };
        var task = new StartSequencer.Sys.ElevatedOp { Kind = StartSequencer.Sys.ElevatedOpKind.SetTaskEnabled, Name = @"\y" };
        var create = new StartSequencer.Sys.ElevatedOp { Kind = StartSequencer.Sys.ElevatedOpKind.CreateElevatedTask, Name = "z" };
        List<(StartSequencer.Sys.ElevatedOp, string?)> ops = [(set, "HKLM-Run|x"), (task, @"Task|\y"), (create, null)];

        var global = new StartSequencer.Sys.ElevatedResult { Ok = false, Errors = ["Elevated process wrote no result, exit code 1"] };
        Assert.Equal(new HashSet<string> { "HKLM-Run|x", @"Task|\y" }, MainViewModel.FailedKeys(ops, global));

        var perOp = new StartSequencer.Sys.ElevatedResult { Ok = false, Errors = [@"SetTaskEnabled \y: denied"] };
        Assert.Equal(new HashSet<string> { @"Task|\y" }, MainViewModel.FailedKeys(ops, perOp));

        var otherOp = new StartSequencer.Sys.ElevatedResult { Ok = false, Errors = ["CreateElevatedTask z: denied"] };
        Assert.Empty(MainViewModel.FailedKeys(ops, otherOp));
    }
}
