using System.Diagnostics;
using Caff;
using Xunit;

namespace CaffTests;

// The status lines are interactive-terminal-only; these tests cover the pure
// text-building helpers. The e2e suite verifies redirected runs stay silent.
public class StatusTests
{
    private static readonly DateTime Noon = new(2026, 1, 1, 12, 0, 0);

    private static Options Parse(params string[] args) => Program.Parse(args);

    [Fact]
    public void HeldPhrase_SystemOnly()
    {
        Assert.Equal("keeping the system awake", Program.HeldPhrase(Parse()));
    }

    [Fact]
    public void HeldPhrase_DisplayOnly()
    {
        Assert.Equal("keeping the display awake", Program.HeldPhrase(Parse("-d")));
    }

    [Fact]
    public void HeldPhrase_Both()
    {
        Assert.Equal("keeping the system + display awake", Program.HeldPhrase(Parse("-di")));
    }

    [Fact]
    public void Describe_Forever()
    {
        Assert.Equal("keeping the system awake until Ctrl+C", Program.Describe(Parse(), Noon));
    }

    [Fact]
    public void Describe_Timeout_ShowsDurationAndEndTime()
    {
        Assert.Equal("keeping the display awake for 1h 0m (until 1:00 PM)",
            Program.Describe(Parse("-d", "-t", "3600"), Noon));
    }

    [Theory]
    [InlineData(9, 5, "9:05 AM")]
    [InlineData(15, 47, "3:47 PM")]
    [InlineData(0, 30, "12:30 AM")]
    [InlineData(12, 0, "12:00 PM")]
    public void FormatTime_TwelveHourLocal(int hour, int minute, string expected)
    {
        Assert.Equal(expected, Program.FormatTime(new DateTime(2026, 1, 1, hour, minute, 0)));
    }

    [Fact]
    public void Describe_WaitPid_NonexistentProcess_OmitsName()
    {
        Assert.Equal("keeping the system awake until pid 999999999 exits",
            Program.Describe(Parse("-w", "999999999"), Noon));
    }

    [Fact]
    public void Describe_WaitPid_IncludesProcessName()
    {
        using var self = Process.GetCurrentProcess();
        string line = Program.Describe(Parse("-w", self.Id.ToString()), Noon);
        Assert.Contains($"until pid {self.Id} ({self.ProcessName}) exits", line);
    }

    [Fact]
    public void Describe_WaitPidWithTimeout_ShowsBoth()
    {
        Assert.Equal("keeping the system awake until pid 999999999 exits (or for 30s)",
            Program.Describe(Parse("-w", "999999999", "-t", "30"), Noon));
    }

    [Fact]
    public void Describe_CommandMode_ShowsCommandName()
    {
        Assert.Equal("keeping the system + display awake while cmd runs",
            Program.Describe(Parse("-di", "cmd", "/c", "exit"), Noon));
    }

    [Theory]
    [InlineData(5, "5s")]
    [InlineData(90, "1m 30s")]
    [InlineData(3600, "1h 0m")]
    [InlineData(5430, "1h 30m")]
    public void FormatDuration_Formats(int seconds, string expected)
    {
        Assert.Equal(expected, Program.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(59, "0:59")]
    [InlineData(90, "1:30")]
    [InlineData(3661, "1:01:01")]
    public void Clock_Formats(int seconds, string expected)
    {
        Assert.Equal(expected, Program.Clock(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Clock_NegativeClampsToZero()
    {
        Assert.Equal("0:00", Program.Clock(TimeSpan.FromSeconds(-3)));
    }
}
