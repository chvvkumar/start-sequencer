using StartSequencer.Discovery;
using Xunit;
using Xunit.Abstractions;

public class StoreAppSourceTests(ITestOutputHelper output)
{
    [Fact]
    public void ScanTasks_DoesNotThrow_AndEveryTaskHasFamilyAndId()
    {
        var tasks = StoreAppSource.ScanTasks();
        foreach (var t in tasks)
            output.WriteLine($"{t.PackageFamilyName}!{t.TaskId} | {t.DisplayName} | {t.PackageDisplayName} | enabled={t.Enabled} policy={t.DisabledByPolicy} | {t.LogoPath}");
        Assert.All(tasks, t => { Assert.False(string.IsNullOrEmpty(t.PackageFamilyName)); Assert.False(string.IsNullOrEmpty(t.TaskId)); Assert.Contains("!", t.Aumid); });
    }

    [Fact]
    public void Scan_NamesAreResolved_NotResourceStrings() =>
        Assert.All(StoreAppSource.Scan(), e =>
        {
            Assert.False(e.Name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase), e.Name);
            Assert.False(e.Publisher.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase), e.Publisher);
        });
}
