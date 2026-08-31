// caff - a stripped-down Windows counterpart of macOS caffeinate(8).
// Holds Windows power requests (visible in `powercfg /requests`) to keep the
// system or display awake for a duration, until a process exits, or while a
// command runs.

using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Caff;

internal sealed class Options
{
    public bool Display;    // -d: prevent display sleep
    public bool Idle;       // -i: prevent system idle sleep
    public int? Timeout;
    public int? WaitPid;
    public bool Help;
    public string[] Command = [];
}

internal static class Program
{
    private const string Usage = "usage: caff [-di] [-t timeout] [-w pid] [command arguments...]";

    private static int Main(string[] args)
    {
        Options opts;
        try
        {
            opts = Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine($"caff: {e.Message}");
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (opts.Help)
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            HoldPowerRequest(opts, BuildReason(args, opts));
        }
        catch (Exception e) when (e is Win32Exception or DllNotFoundException)
        {
            Console.Error.WriteLine($"caff: power request failed: {e.Message}");
            return 1;
        }

        // Status lines go to stderr, and only when it is an interactive
        // terminal, so scripts and redirected output keep caffeinate's
        // silent behavior.
        bool chatty = !Console.IsErrorRedirected;
        if (chatty)
        {
            EnableAnsi();
            WriteStatusLine(Describe(opts, DateTime.Now));
        }

        if (opts.Command.Length > 0)
            return RunCommand(opts.Command);

        string? status = chatty ? HeldPhrase(opts) : null;
        DateTime begin = DateTime.Now;
        int result;
        if (opts.WaitPid is int pid)
        {
            result = WaitForProcess(pid, opts.Timeout, status);
        }
        else
        {
            SleepFor(opts.Timeout, status);
            result = 0;
        }

        if (chatty)
        {
            Console.Error.Write("\r\x1b[K"); // erase the ticker line
            WriteStatusLine($"released after {FormatDuration(DateTime.Now - begin)}");
        }
        return result;
    }

    // BSD getopt style, like caffeinate: clustered flags (-di), attached (-t5) or
    // detached (-t 5) option arguments; "--" or the first non-flag argument ends
    // option parsing and everything after it is the command to run.
    internal static Options Parse(string[] args)
    {
        var opts = new Options();
        int i = 0;
        // Length > 1 keeps a lone "-" out of the flag loop: like getopt, it is a
        // non-option argument and starts the command.
        while (i < args.Length && args[i].Length > 1 && args[i][0] == '-')
        {
            string arg = args[i++];
            if (arg == "--")
                break;
            for (int j = 1; j < arg.Length; j++)
            {
                switch (arg[j])
                {
                    case 'd': opts.Display = true; break;
                    case 'i': opts.Idle = true; break;
                    case 'h': opts.Help = true; break;
                    case 'm': throw new ArgumentException("-m (prevent disk idle) is not supported: Windows has no disk-idle assertion API");
                    case 's': throw new ArgumentException("-s (prevent sleep while on AC) is not supported: it would be identical to -i on Windows; use -i");
                    case 'u': throw new ArgumentException("-u (declare user activity) is not supported: it would be redundant with -d on Windows; use -d");
                    case 't' or 'w':
                        char c = arg[j];
                        string? value = j + 1 < arg.Length ? arg[(j + 1)..] : i < args.Length ? args[i++] : null;
                        if (value is null)
                            throw new ArgumentException($"option -{c} requires an argument");
                        if (!int.TryParse(value, out int n) || n < 0)
                            throw new ArgumentException($"invalid -{c} argument: {value}");
                        if (c == 't') opts.Timeout = n; else opts.WaitPid = n;
                        // The rest of the cluster was the option's argument; this
                        // also ends the flag loop (break only leaves the switch).
                        j = arg.Length;
                        break;
                    default:
                        throw new ArgumentException($"unknown option -{arg[j]}");
                }
            }
        }
        opts.Command = args[i..];
        if (opts.Command.Length > 0)
        {
            // caffeinate ignores -t and -w when a command is given.
            opts.Timeout = null;
            opts.WaitPid = null;
        }
        // IOKit treats a zero assertion timeout as "no timeout", so caffeinate
        // -t 0 holds forever; match that instead of exiting immediately.
        if (opts.Timeout == 0)
            opts.Timeout = null;
        if (!opts.Display && !opts.Idle)
            opts.Idle = true; // caffeinate defaults to preventing idle sleep.
        return opts;
    }

    // The reason string shows up system-wide in `powercfg /requests` and in
    // power diagnostic reports, so include caff's own flags and the command
    // name but never the command's arguments (they may contain secrets).
    internal static string BuildReason(string[] args, Options opts)
    {
        string reason = "caff";
        int flagCount = args.Length - opts.Command.Length;
        if (flagCount > 0)
            reason += " " + string.Join(' ', args[..flagCount]);
        if (opts.Command.Length > 0)
            reason += " " + opts.Command[0] + (opts.Command.Length > 1 ? " ..." : "");
        return reason;
    }

    // Internal so tests can exercise the real P/Invoke path. The returned
    // request handle is deliberately never closed: Windows releases the request
    // when the process exits, which is exactly the lifetime we want.
    internal static IntPtr HoldPowerRequest(Options opts, string reason)
    {
        var context = new ReasonContext
        {
            Version = 0, // POWER_REQUEST_CONTEXT_VERSION
            Flags = 1,   // POWER_REQUEST_CONTEXT_SIMPLE_STRING
            SimpleReasonString = reason,
        };
        IntPtr request = PowerCreateRequest(ref context);
        if (request == new IntPtr(-1)) // INVALID_HANDLE_VALUE, not null, on failure
            throw new Win32Exception();
        if (opts.Idle)
        {
            if (!PowerSetRequest(request, PowerRequestSystemRequired))
                throw new Win32Exception();
            // On Modern Standby (S0) hardware SystemRequired alone does not keep
            // the process running; ExecutionRequired does, and on classic S3
            // systems it is documented as equivalent to SystemRequired.
            if (!PowerSetRequest(request, PowerRequestExecutionRequired))
                throw new Win32Exception();
        }
        if (opts.Display && !PowerSetRequest(request, PowerRequestDisplayRequired))
            throw new Win32Exception();
        return request;
    }

    private static int RunCommand(string[] command)
    {
        if (command[0].Length == 0)
        {
            Console.Error.WriteLine("caff: empty command");
            return 1;
        }
        var psi = new ProcessStartInfo(command[0]) { UseShellExecute = false };
        foreach (string arg in command[1..])
            psi.ArgumentList.Add(arg);
        Process? child;
        try
        {
            child = Process.Start(psi);
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"caff: {command[0]}: {e.Message}");
            return 1;
        }
        using (child)
        {
            child!.WaitForExit();
            return child.ExitCode;
        }
    }

    // status is the phrase to tick with each second, or null to wait silently.
    private static int WaitForProcess(int pid, int? timeoutSeconds, string? status)
    {
        Process process;
        try
        {
            process = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"caff: no process with pid {pid}");
            return 1;
        }
        try
        {
            using (process)
            {
                if (status is not null)
                {
                    DateTime start = DateTime.Now;
                    DateTime? deadline = timeoutSeconds is int s ? start.AddSeconds(s) : null;
                    while (!process.WaitForExit(1000))
                    {
                        if (deadline is DateTime d)
                        {
                            TimeSpan left = d - DateTime.Now;
                            if (left <= TimeSpan.Zero)
                                break;
                            Tick(status, left, remaining: true);
                        }
                        else
                        {
                            Tick(status, DateTime.Now - start, remaining: false);
                        }
                    }
                }
                else if (timeoutSeconds is int seconds)
                {
                    // WaitForExit takes at most int.MaxValue milliseconds, so wait in chunks.
                    for (long remaining = seconds * 1000L; remaining > 0 && !process.HasExited; remaining -= int.MaxValue)
                        process.WaitForExit((int)Math.Min(remaining, int.MaxValue));
                }
                else
                {
                    process.WaitForExit();
                }
            }
        }
        catch (Win32Exception e)
        {
            Console.Error.WriteLine($"caff: cannot wait for pid {pid}: {e.Message}");
            return 1;
        }
        return 0;
    }

    // status is the phrase to tick with each second, or null to sleep silently.
    private static void SleepFor(int? timeoutSeconds, string? status)
    {
        if (status is not null)
        {
            DateTime start = DateTime.Now;
            DateTime? deadline = timeoutSeconds is int s ? start.AddSeconds(s) : null;
            while (true)
            {
                if (deadline is DateTime d)
                {
                    TimeSpan left = d - DateTime.Now;
                    if (left <= TimeSpan.Zero)
                        return;
                    Tick(status, left, remaining: true);
                    Thread.Sleep(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1));
                }
                else
                {
                    Tick(status, DateTime.Now - start, remaining: false);
                    Thread.Sleep(1000);
                }
            }
        }
        if (timeoutSeconds is not int seconds)
        {
            Thread.Sleep(Timeout.Infinite);
            return;
        }
        // Thread.Sleep takes at most int.MaxValue milliseconds, so sleep in chunks.
        for (long remaining = seconds * 1000L; remaining > 0; remaining -= int.MaxValue)
            Thread.Sleep((int)Math.Min(remaining, int.MaxValue));
    }

    // "keeping the system + display awake" etc.; mirrors the flag vocabulary so
    // the status line doubles as confirmation of what was requested.
    internal static string HeldPhrase(Options opts) => (opts.Idle, opts.Display) switch
    {
        (true, true) => "keeping the system + display awake",
        (true, false) => "keeping the system awake",
        _ => "keeping the display awake",
    };

    // The full startup line, e.g. "keeping the display awake for 1h 0m (until 15:47)".
    internal static string Describe(Options opts, DateTime now)
    {
        string held = HeldPhrase(opts);
        if (opts.Command.Length > 0)
            return $"{held} while {opts.Command[0]} runs";
        if (opts.WaitPid is int pid)
        {
            string name = "";
            try
            {
                using var process = Process.GetProcessById(pid);
                name = $" ({process.ProcessName})";
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
            }
            string limit = opts.Timeout is int s ? $" (or for {FormatDuration(TimeSpan.FromSeconds(s))})" : "";
            return $"{held} until pid {pid}{name} exits{limit}";
        }
        if (opts.Timeout is int t)
            return $"{held} for {FormatDuration(TimeSpan.FromSeconds(t))} (until {FormatTime(now.AddSeconds(t))})";
        return $"{held} until Ctrl+C";
    }

    // 12-hour local wall-clock time, e.g. "3:47 PM".
    internal static string FormatTime(DateTime t) => t.ToString("h:mm tt");

    internal static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1)
            return $"{(long)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1)
            return $"{(int)t.TotalMinutes}m {t.Seconds}s";
        return $"{Math.Max(0, (int)t.TotalSeconds)}s";
    }

    // Compact h:mm:ss / m:ss for the once-a-second ticker.
    internal static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero)
            t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(long)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
            : $"{t.Minutes}:{t.Seconds:D2}";
    }

    private static void WriteStatusLine(string text) =>
        Console.Error.WriteLine($"\x1b[2m☕ caff: {text}\x1b[0m");

    // Rewrites the current terminal line in place (\r ... \x1b[K clears the tail).
    private static void Tick(string status, TimeSpan span, bool remaining) =>
        Console.Error.Write($"\r\x1b[2m☕ caff: {status} ({Clock(span)} {(remaining ? "remaining" : "elapsed")})\x1b[0m\x1b[K");

    // Turns on ANSI escape processing for stderr; on by default in Windows
    // Terminal, needed for classic conhost. Best effort: failure just means the
    // escapes would show literally, and GetConsoleMode failing implies no real
    // console anyway (in which case chatty mode is off).
    private static void EnableAnsi()
    {
        IntPtr handle = GetStdHandle(-12); // STD_ERROR_HANDLE
        if (GetConsoleMode(handle, out uint mode))
            SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; // so the coffee cup renders
        }
        catch (IOException)
        {
        }
    }

    private const int PowerRequestDisplayRequired = 0;
    private const int PowerRequestSystemRequired = 1;
    private const int PowerRequestExecutionRequired = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        [MarshalAs(UnmanagedType.LPWStr)] public string SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int stdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
