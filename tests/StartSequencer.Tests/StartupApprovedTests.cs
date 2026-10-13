using Microsoft.Win32;
using StartSequencer.Discovery;
using Xunit;

public class StartupApprovedTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0,0,0, 0,0,0,0, 0,0,0,0 }, true)]
    [InlineData(new byte[] { 0x06, 0,0,0, 0,0,0,0, 0,0,0,0 }, true)]
    [InlineData(new byte[] { 0x03, 0,0,0, 1,2,3,4, 5,6,7,8 }, false)]
    [InlineData(null, true)]
    [InlineData(new byte[0], true)]
    public void IsEnabled_ReadsFirstByte(byte[]? blob, bool expected) => Assert.Equal(expected, StartupApproved.IsEnabled(blob));

    [Fact]
    public void DisabledBlob_Has12BytesAndFiletime()
    {
        var t = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var b = StartupApproved.DisabledBlob(t);
        Assert.Equal(12, b.Length);
        Assert.Equal(0x03, b[0]);
        Assert.Equal(t.ToFileTimeUtc(), BitConverter.ToInt64(b, 4));
    }

    [Fact]
    public void Write_ThenRead_HkcuRoundTrip()
    {
        var name = "ss-test-" + Guid.NewGuid().ToString("N");
        try
        {
            StartupApproved.Write(RegistryHive.CurrentUser, ApprovedList.Run, name, false);
            Assert.False(StartupApproved.Read(RegistryHive.CurrentUser, ApprovedList.Run, name));
            StartupApproved.Write(RegistryHive.CurrentUser, ApprovedList.Run, name, true);
            Assert.True(StartupApproved.Read(RegistryHive.CurrentUser, ApprovedList.Run, name));
        }
        finally
        {
            using var k = Registry.CurrentUser.OpenSubKey(StartupApproved.SubKey + "Run", writable: true);
            k?.DeleteValue(name, throwOnMissingValue: false);
        }
    }
}
