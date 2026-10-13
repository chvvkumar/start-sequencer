using Microsoft.Win32;
using StartSequencer.Discovery;
using StartSequencer.Sys;
using Xunit;

public class ElevatedOpsTests
{
    [Fact]
    public void Apply_SetApproved_Hkcu_Works_Unelevated()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        try
        {
            var r = ElevatedOps.Apply([new ElevatedOp { Kind = ElevatedOpKind.SetApproved, Hive = "HKCU", List = "Run", Name = name, Enabled = false }]);
            Assert.True(r.Ok, string.Join(";", r.Errors));
            Assert.False(StartupApproved.Read(RegistryHive.CurrentUser, ApprovedList.Run, name));
        }
        finally { using var k = Registry.CurrentUser.OpenSubKey(StartupApproved.SubKey + "Run", true); k?.DeleteValue(name, false); }
    }

    private static string OpsFile(byte[] content)
    {
        var p = Path.Combine(Path.GetTempPath(), "ss-ops-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(p, content);
        return p;
    }

    [Fact]
    public void ApplyFile_WritesResultFile()
    {
        var bytes = "[]"u8.ToArray();
        var p = OpsFile(bytes);
        try
        {
            Assert.Equal(0, ElevatedOps.ApplyFile(p, ElevatedOps.Hash(bytes)));
            Assert.True(File.Exists(p + ".result"));
        }
        finally { File.Delete(p); File.Delete(p + ".result"); }
    }

    [Fact]
    public void ApplyFile_WrongHash_Refuses_WithErrorResult()
    {
        var p = OpsFile("[]"u8.ToArray());
        try
        {
            Assert.Equal(1, ElevatedOps.ApplyFile(p, ElevatedOps.Hash("[ ]"u8.ToArray())));
            var r = System.Text.Json.JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(p + ".result"), StartSequencer.Model.ConfigStore.Json)!;
            Assert.False(r.Ok);
            Assert.Equal(["Operations file changed"], r.Errors);
        }
        finally { File.Delete(p); File.Delete(p + ".result"); }
    }

    [Fact]
    public void ApplyFile_Oversized_Refuses()
    {
        var bytes = new byte[1024 * 1024 + 1];
        var p = OpsFile(bytes);
        try { Assert.Equal(1, ElevatedOps.ApplyFile(p, ElevatedOps.Hash(bytes))); }
        finally { File.Delete(p); File.Delete(p + ".result"); }
    }

    [Fact]
    public void ApplyFile_ExistingResultFile_IsNotOverwritten()
    {
        var bytes = "[]"u8.ToArray();
        var p = OpsFile(bytes);
        File.WriteAllText(p + ".result", "planted");
        try
        {
            Assert.Equal(1, ElevatedOps.ApplyFile(p, ElevatedOps.Hash(bytes)));
            Assert.Equal("planted", File.ReadAllText(p + ".result"));
        }
        finally { File.Delete(p); File.Delete(p + ".result"); }
    }

    [Fact]
    public void RunElevated_EmptyOps_IsOk_WithoutPrompt() => Assert.True(ElevatedOps.RunElevated([]).Ok);
}
