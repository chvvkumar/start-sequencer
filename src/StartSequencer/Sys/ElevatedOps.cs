using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using StartSequencer.Discovery;
using StartSequencer.Model;

namespace StartSequencer.Sys;

public enum ElevatedOpKind { SetApproved, SetTaskEnabled, CreateElevatedTask, DeleteElevatedTask, DeleteAllElevatedTasks }

public sealed class ElevatedOp
{
    public ElevatedOpKind Kind { get; set; }
    public string Hive { get; set; } = "";        // "HKCU" or "HKLM" for SetApproved
    public string List { get; set; } = "";        // ApprovedList name
    public string Name { get; set; } = "";        // value name / task path / entry key
    public bool Enabled { get; set; }
    public string ExePath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
}

public sealed class ElevatedResult { public bool Ok { get; set; } public List<string> Errors { get; set; } = []; }

public static class ElevatedOps
{
    const int ErrorCancelled = 1223;

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ElevatedResult Apply(IReadOnlyList<ElevatedOp> ops)
    {
        var errors = new List<string>();
        foreach (var op in ops)
        {
            try { Run(op); }
            catch (Exception ex) { errors.Add($"{op.Kind} {op.Name}: {ex.Message}"); }
        }
        return new ElevatedResult { Ok = errors.Count == 0, Errors = errors };
    }

    static void Run(ElevatedOp op)
    {
        switch (op.Kind)
        {
            case ElevatedOpKind.SetApproved:
                var hive = op.Hive switch
                {
                    "HKCU" => RegistryHive.CurrentUser,
                    "HKLM" => RegistryHive.LocalMachine,
                    _ => throw new InvalidOperationException("Unknown hive: " + op.Hive),
                };
                // Numeric strings parse to undefined values, so require a defined name.
                if (op.List.All(char.IsAsciiDigit) || !Enum.TryParse<ApprovedList>(op.List, true, out var list) || !Enum.IsDefined(list))
                    throw new InvalidOperationException("Unknown approved list: " + op.List);
                StartupApproved.Write(hive, list, op.Name, op.Enabled);
                break;
            case ElevatedOpKind.SetTaskEnabled:
                TaskSchedulerSource.SetEnabled(op.Name, op.Enabled);
                break;
            case ElevatedOpKind.CreateElevatedTask:
                LogonTask.CreateElevated(op.Name, op.ExePath, op.Arguments, op.WorkingDirectory);
                break;
            case ElevatedOpKind.DeleteElevatedTask:
                LogonTask.DeleteElevated(op.Name);
                break;
            case ElevatedOpKind.DeleteAllElevatedTasks:
                LogonTask.DeleteAllElevated();
                break;
            default:
                throw new InvalidOperationException("Unknown operation: " + op.Kind);
        }
    }

    const long MaxOpsFileBytes = 1024 * 1024;

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    // --elevated-op entry. The ops file sits in the user's %TEMP%, so it is applied only when its
    // SHA-256 matches the hash the unelevated side passed on the command line.
    // The result file is written even when the ops file is refused or cannot be read.
    public static int ApplyFile(string path, string expectedHash)
    {
        ElevatedResult result;
        try
        {
            if (new FileInfo(path).Length > MaxOpsFileBytes) throw new InvalidDataException("Operations file is larger than 1 MB");
            var bytes = File.ReadAllBytes(path);
            result = string.Equals(Hash(bytes), expectedHash, StringComparison.OrdinalIgnoreCase)
                ? Apply(JsonSerializer.Deserialize<List<ElevatedOp>>(bytes, ConfigStore.Json) ?? [])
                : new ElevatedResult { Ok = false, Errors = ["Operations file changed"] };
        }
        catch (Exception ex)
        {
            result = new ElevatedResult { Ok = false, Errors = ["Cannot read operations file: " + ex.Message] };
        }
        try
        {
            // An elevated write must not follow a link planted in a user-writable folder.
            var resultPath = path + ".result";
            if (File.Exists(resultPath) && File.GetAttributes(resultPath).HasFlag(FileAttributes.ReparsePoint)) return 1;
            using var f = new FileStream(resultPath, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(f, result, ConfigStore.Json);
        }
        catch { return 1; }
        return result.Ok ? 0 : 1;
    }

    public static ElevatedResult RunElevated(IReadOnlyList<ElevatedOp> ops)
    {
        if (ops.Count == 0) return new ElevatedResult { Ok = true };

        var path = Path.Combine(Path.GetTempPath(), $"ss-ops-{Guid.NewGuid():N}.json");
        var resultPath = path + ".result";
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(ops, ConfigStore.Json);
            File.WriteAllBytes(path, bytes);
            int exitCode;
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--elevated-op \"{path}\" {Hash(bytes)}")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                });
                if (p is null) return new ElevatedResult { Ok = false, Errors = ["Elevated process did not start"] };
                p.WaitForExit();
                exitCode = p.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                return new ElevatedResult { Ok = false, Errors = ["Cancelled"] };
            }

            if (!File.Exists(resultPath))
                return new ElevatedResult { Ok = false, Errors = [$"Elevated process wrote no result, exit code {exitCode}"] };
            return JsonSerializer.Deserialize<ElevatedResult>(File.ReadAllText(resultPath), ConfigStore.Json)
                ?? new ElevatedResult { Ok = false, Errors = ["Elevated result file is empty"] };
        }
        catch (Exception ex)
        {
            return new ElevatedResult { Ok = false, Errors = [ex.Message] };
        }
        finally
        {
            foreach (var f in new[] { path, resultPath })
                try { File.Delete(f); } catch (Exception) { }
        }
    }
}
