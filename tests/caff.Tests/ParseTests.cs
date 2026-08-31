using Caff;
using Xunit;

namespace CaffTests;

public class ParseTests
{
    [Fact]
    public void NoArgs_DefaultsToSystemRequest()
    {
        var opts = Program.Parse([]);
        Assert.True(opts.Idle);
        Assert.False(opts.Display);
        Assert.Null(opts.Timeout);
        Assert.Null(opts.WaitPid);
        Assert.False(opts.Help);
        Assert.Empty(opts.Command);
    }

    [Fact]
    public void DisplayFlag_DoesNotImplySystem()
    {
        var opts = Program.Parse(["-d"]);
        Assert.True(opts.Display);
        Assert.False(opts.Idle);
    }

    [Fact]
    public void IdleFlag()
    {
        var opts = Program.Parse(["-i"]);
        Assert.True(opts.Idle);
        Assert.False(opts.Display);
    }

    [Fact]
    public void ClusteredFlags()
    {
        var opts = Program.Parse(["-di"]);
        Assert.True(opts.Display);
        Assert.True(opts.Idle);
    }

    [Fact]
    public void SeparateFlags()
    {
        var opts = Program.Parse(["-d", "-i"]);
        Assert.True(opts.Display);
        Assert.True(opts.Idle);
    }

    [Fact]
    public void Timeout_DetachedArgument()
    {
        Assert.Equal(5, Program.Parse(["-t", "5"]).Timeout);
    }

    [Fact]
    public void Timeout_AttachedArgument()
    {
        Assert.Equal(5, Program.Parse(["-t5"]).Timeout);
    }

    [Fact]
    public void Timeout_AtEndOfCluster()
    {
        var opts = Program.Parse(["-dt5"]);
        Assert.True(opts.Display);
        Assert.Equal(5, opts.Timeout);
    }

    [Fact]
    public void Timeout_ZeroMeansNoTimeout()
    {
        // caffeinate -t 0 holds forever (IOKit treats 0 as "no timeout").
        Assert.Null(Program.Parse(["-t", "0"]).Timeout);
    }

    [Fact]
    public void Timeout_IntMaxIsAllowed()
    {
        Assert.Equal(int.MaxValue, Program.Parse(["-t", "2147483647"]).Timeout);
    }

    [Theory]
    [InlineData("-t")]           // missing argument
    [InlineData("-t", "abc")]    // not a number
    [InlineData("-t", "-5")]     // negative
    [InlineData("-t", "2.5")]    // not an integer
    [InlineData("-t", "2147483648")] // overflows int
    public void Timeout_InvalidArguments_Throw(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => Program.Parse(args));
    }

    [Fact]
    public void WaitPid_DetachedArgument()
    {
        Assert.Equal(1234, Program.Parse(["-w", "1234"]).WaitPid);
    }

    [Fact]
    public void WaitPid_AttachedArgument()
    {
        Assert.Equal(1234, Program.Parse(["-w1234"]).WaitPid);
    }

    [Theory]
    [InlineData("-w")]
    [InlineData("-w", "abc")]
    [InlineData("-w", "-1")]
    public void WaitPid_InvalidArguments_Throw(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => Program.Parse(args));
    }

    [Theory]
    [InlineData("-m")]
    [InlineData("-s")]
    [InlineData("-u")]
    public void UnsupportedFlags_ThrowWithExplanation(string flag)
    {
        var e = Assert.Throws<ArgumentException>(() => Program.Parse([flag]));
        Assert.Contains("not supported", e.Message);
    }

    [Fact]
    public void UnsupportedFlag_InCluster_Throws()
    {
        Assert.Throws<ArgumentException>(() => Program.Parse(["-ds"]));
    }

    [Fact]
    public void UnknownFlag_Throws()
    {
        var e = Assert.Throws<ArgumentException>(() => Program.Parse(["-x"]));
        Assert.Contains("unknown option", e.Message);
    }

    [Fact]
    public void HelpFlag()
    {
        Assert.True(Program.Parse(["-h"]).Help);
    }

    [Fact]
    public void Command_IsCaptured()
    {
        var opts = Program.Parse(["cmd", "/c", "echo"]);
        Assert.Equal(["cmd", "/c", "echo"], opts.Command);
        Assert.True(opts.Idle); // default assertion still applies
    }

    [Fact]
    public void Command_StopsFlagParsing()
    {
        var opts = Program.Parse(["cmd", "-t", "5"]);
        Assert.Equal(["cmd", "-t", "5"], opts.Command);
        Assert.Null(opts.Timeout);
    }

    [Fact]
    public void Command_IgnoresTimeoutAndWaitPid()
    {
        var opts = Program.Parse(["-t", "5", "-w", "1", "cmd"]);
        Assert.Equal(["cmd"], opts.Command);
        Assert.Null(opts.Timeout);
        Assert.Null(opts.WaitPid);
    }

    [Fact]
    public void DoubleDash_EndsOptionParsing()
    {
        var opts = Program.Parse(["--", "-d"]);
        Assert.Equal(["-d"], opts.Command);
        Assert.False(opts.Display);
        Assert.True(opts.Idle);
    }

    [Fact]
    public void EmptyCommand_Throws()
    {
        var e = Assert.Throws<ArgumentException>(() => Program.Parse([""]));
        Assert.Contains("empty command", e.Message);
    }

    [Fact]
    public void SingleDash_IsACommand()
    {
        Assert.Equal(["-"], Program.Parse(["-"]).Command);
    }

    [Fact]
    public void FlagsBeforeCommand_Apply()
    {
        var opts = Program.Parse(["-d", "cmd"]);
        Assert.True(opts.Display);
        Assert.Equal(["cmd"], opts.Command);
    }

    [Fact]
    public void Reason_NoArgs()
    {
        Assert.Equal("caff", Program.BuildReason([], Program.Parse([])));
    }

    [Fact]
    public void Reason_FlagsAreIncluded()
    {
        string[] args = ["-d", "-t", "5"];
        Assert.Equal("caff -d -t 5", Program.BuildReason(args, Program.Parse(args)));
    }

    [Fact]
    public void Reason_CommandArgumentsAreOmitted()
    {
        // Command arguments may contain secrets and must not leak into the
        // system-wide power request reason string.
        string[] args = ["-d", "git", "fetch", "--password=hunter2"];
        Assert.Equal("caff -d git ...", Program.BuildReason(args, Program.Parse(args)));
    }

    [Fact]
    public void Reason_BareCommandNameIsKept()
    {
        string[] args = ["notepad"];
        Assert.Equal("caff notepad", Program.BuildReason(args, Program.Parse(args)));
    }
}
