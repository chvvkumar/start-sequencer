using StartSequencer.Discovery;
using Xunit;

public class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\X\\x.exe\" --flag a", "C:\\Program Files\\X\\x.exe", "--flag a")]
    [InlineData("\"C:\\Program Files\\X\\x.exe\"", "C:\\Program Files\\X\\x.exe", "")]
    [InlineData("C:\\Windows\\notepad.exe /a", "C:\\Windows\\notepad.exe", "/a")]
    [InlineData("C:\\Windows\\System32\\rundll32.exe foo.dll,Bar", "C:\\Windows\\System32\\rundll32.exe", "foo.dll,Bar")]
    [InlineData("C:\\Windows\\System32\\cmd.exe", "C:\\Windows\\System32\\cmd.exe", "")]
    [InlineData("", "", "")]
    public void Split_HandlesQuotedAndUnquoted(string input, string exe, string args)
    {
        var (e, a) = CommandLine.Split(input);
        Assert.Equal(exe, e);
        Assert.Equal(args, a);
    }

    [Fact]
    public void Split_UnquotedPathWithSpaces_FindsExistingFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ss dir " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "a b.exe");
        File.WriteAllText(exe, "");
        try
        {
            var (e, a) = CommandLine.Split(exe + " -x");
            Assert.Equal(exe, e);
            Assert.Equal("-x", a);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Publisher_OfNotepad_IsMicrosoft() =>
        Assert.Contains("Microsoft", ShellInfo.Publisher(@"C:\Windows\notepad.exe"));
}
