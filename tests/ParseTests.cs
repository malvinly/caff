using Caff;
using Xunit;

namespace CaffTests;

public class ParseTests
{
    [Fact]
    public void NoArgs_DefaultsToSystemRequest()
    {
        var opts = Program.Parse([]);
        Assert.True(opts.System);
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
        Assert.False(opts.System);
    }

    [Fact]
    public void SystemFlag()
    {
        var opts = Program.Parse(["-i"]);
        Assert.True(opts.System);
        Assert.False(opts.Display);
    }

    [Fact]
    public void ClusteredFlags()
    {
        var opts = Program.Parse(["-di"]);
        Assert.True(opts.Display);
        Assert.True(opts.System);
    }

    [Fact]
    public void SeparateFlags()
    {
        var opts = Program.Parse(["-d", "-i"]);
        Assert.True(opts.Display);
        Assert.True(opts.System);
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
    public void Timeout_ZeroIsAllowed()
    {
        Assert.Equal(0, Program.Parse(["-t", "0"]).Timeout);
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
        Assert.True(opts.System); // default assertion still applies
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
        Assert.True(opts.System);
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
}
