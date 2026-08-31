using System.Diagnostics;
using Xunit;

namespace CaffTests;

// End-to-end tests that spawn the real caff executable (copied into the test
// output directory by the project reference).
public class CaffExeTests
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private static (int ExitCode, string StdOut, string StdErr) RunCaff(params string[] args) =>
        RunCaff(DefaultTimeout, args);

    private static (int ExitCode, string StdOut, string StdErr) RunCaff(TimeSpan timeout, params string[] args)
    {
        using var caff = StartCaff(args);
        return Complete(caff, timeout, args);
    }

    private static Process StartCaff(params string[] args)
    {
        string dir = AppContext.BaseDirectory;
        string exe = Path.Combine(dir, "caff.exe");
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        if (File.Exists(exe))
        {
            psi.FileName = exe;
        }
        else
        {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(Path.Combine(dir, "caff.dll"));
        }
        foreach (string arg in args)
            psi.ArgumentList.Add(arg);
        return Process.Start(psi)!;
    }

    private static (int ExitCode, string StdOut, string StdErr) Complete(Process caff, TimeSpan timeout, string[] args)
    {
        var stdout = caff.StandardOutput.ReadToEndAsync();
        var stderr = caff.StandardError.ReadToEndAsync();
        if (!caff.WaitForExit((int)timeout.TotalMilliseconds))
        {
            caff.Kill(entireProcessTree: true);
            caff.WaitForExit();
            throw new TimeoutException($"caff {string.Join(' ', args)} did not exit within {timeout}");
        }
        return (caff.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void Help_PrintsUsage_ExitsZero()
    {
        var (exitCode, stdout, _) = RunCaff("-h");
        Assert.Equal(0, exitCode);
        Assert.Contains("usage: caff", stdout);
    }

    [Fact]
    public void Timeout_ExitsZeroAfterRoughlyThatLong()
    {
        var stopwatch = Stopwatch.StartNew();
        var (exitCode, _, _) = RunCaff("-t", "1");
        stopwatch.Stop();
        Assert.Equal(0, exitCode);
        Assert.True(stopwatch.Elapsed >= TimeSpan.FromSeconds(0.9),
            $"exited after only {stopwatch.Elapsed}");
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"took {stopwatch.Elapsed} for a 1-second timeout");
    }

    [Fact]
    public void TimeoutZero_HoldsForever()
    {
        // Matches caffeinate: -t 0 means no timeout, not "exit immediately".
        using var caff = StartCaff("-t", "0");
        try
        {
            Assert.False(caff.WaitForExit(2000), "caff -t 0 exited; it should hold indefinitely");
        }
        finally
        {
            caff.Kill(entireProcessTree: true);
            caff.WaitForExit();
        }
    }

    [Theory]
    [InlineData("-d")]
    [InlineData("-i")]
    public void SingleAssertionFlag_RunsCleanly(string flag)
    {
        var (exitCode, _, stderr) = RunCaff(flag, "-t", "1");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void ClusteredFlagsWithTimeout_ExitZero()
    {
        var (exitCode, _, stderr) = RunCaff("-di", "-t", "1");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void CommandMode_PropagatesChildExitCode()
    {
        var (exitCode, _, _) = RunCaff("cmd", "/c", "exit 7");
        Assert.Equal(7, exitCode);
    }

    [Fact]
    public void CommandMode_ZeroExitCode()
    {
        var (exitCode, _, _) = RunCaff("cmd", "/c", "exit 0");
        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void RedirectedOutput_StaysCompletelySilent()
    {
        // Status lines are interactive-terminal-only; with stderr redirected
        // (as it is here), caff must keep caffeinate's silent behavior.
        var (exitCode, stdout, stderr) = RunCaff("-t", "1");
        Assert.Equal(0, exitCode);
        Assert.Equal("", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void CommandMode_AfterDoubleDash()
    {
        var (exitCode, _, _) = RunCaff("--", "cmd", "/c", "exit 3");
        Assert.Equal(3, exitCode);
    }

    [Fact]
    public void CommandMode_MissingExecutable_ExitsOne()
    {
        var (exitCode, _, stderr) = RunCaff("caff-test-no-such-command");
        Assert.Equal(1, exitCode);
        Assert.Contains("caff-test-no-such-command", stderr);
    }

    [Fact]
    public void CommandMode_EmptyCommand_ExitsOne()
    {
        var (exitCode, _, stderr) = RunCaff("");
        Assert.Equal(1, exitCode);
        Assert.Contains("empty command", stderr);
    }

    [Fact]
    public void CommandMode_ChildOutputIsPassedThrough()
    {
        var (exitCode, stdout, _) = RunCaff("cmd", "/c", "echo hello");
        Assert.Equal(0, exitCode);
        Assert.Equal("hello", stdout.Trim());
    }

    [Fact]
    public void Wait_NonexistentPid_ExitsOne()
    {
        // Far above anything Windows will have allocated in practice;
        // Process.GetProcessById throws for it.
        var (exitCode, _, stderr) = RunCaff("-w", "999999999");
        Assert.Equal(1, exitCode);
        Assert.Contains("no process with pid", stderr);
    }

    [Fact]
    public void Wait_ExitsWhenWatchedProcessExits()
    {
        using var watched = StartInertProcess();
        try
        {
            using var caff = StartCaff("-w", watched.Id.ToString());

            // caff should still be waiting while the watched process is alive.
            Assert.False(caff.WaitForExit(2000), "caff exited before the watched process did");

            watched.StandardInput.Close(); // makes the watched cmd.exe exit
            Assert.True(watched.WaitForExit(10_000), "watched process did not exit");

            var (exitCode, _, _) = Complete(caff, DefaultTimeout, ["-w", watched.Id.ToString()]);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            if (!watched.HasExited)
                watched.Kill(entireProcessTree: true);
        }
    }

    [Fact]
    public void Wait_WithTimeout_ExitsAtTimeoutIfProcessOutlivesIt()
    {
        using var watched = StartInertProcess();
        try
        {
            var (exitCode, _, _) = RunCaff("-w", watched.Id.ToString(), "-t", "1");
            Assert.Equal(0, exitCode);
            Assert.False(watched.HasExited); // caff left the process alone
        }
        finally
        {
            watched.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [InlineData("-m")]
    [InlineData("-s")]
    [InlineData("-u")]
    public void UnsupportedFlags_ExitOneWithExplanation(string flag)
    {
        var (exitCode, _, stderr) = RunCaff(flag);
        Assert.Equal(1, exitCode);
        Assert.Contains("not supported", stderr);
        Assert.Contains("usage: caff", stderr);
    }

    [Fact]
    public void UnknownFlag_ExitsOneWithUsage()
    {
        var (exitCode, _, stderr) = RunCaff("-x");
        Assert.Equal(1, exitCode);
        Assert.Contains("usage: caff", stderr);
    }

    // A cmd.exe that sits idle reading stdin until we close it.
    private static Process StartInertProcess()
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Process.Start(psi)!;
    }
}
