# caff

A stripped-down .NET version of macOS `caffeinate` for Windows, written for personal use. It keeps the system or display awake for a duration, until a process exits, or while a command runs.

The flags match the macOS original, so the same command lines work on both OSs (only the executable name differs).

## Usage

```
usage: caff [-di] [-t timeout] [-w pid] [command arguments...]
```

| Flag | Effect |
|------|--------|
| `-d` | Prevent the display from sleeping |
| `-i` | Prevent the system from idle sleeping (default if no flags are given) |
| `-t <timeout>` | Hold the assertion for this long, then exit. Seconds by default (`-t 3000`), or with lowercase `s`/`m`/`h` suffixes (`-t 90m`, `-t 1h30m`). The suffix form is a caff extension: macOS `caffeinate` does not understand it. `-t 0` (or `-t 0s`) means no timeout (hold forever), matching `caffeinate` |
| `-w <pid>` | Hold the assertion until the given process exits |
| `-h` | Print usage |
| `command args...` | Run the command and hold the assertion until it exits; caff exits with the command's exit code. `-t` and `-w` are ignored in this mode, matching `caffeinate` |

With no timeout, pid, or command, caff holds the assertion until you Ctrl+C it. If both `-t` and `-w` are given, caff exits when either fires.

In an interactive terminal, caff prints a dim status line to stderr showing what it is holding, updates it in place with a live countdown (or elapsed time), and prints a closing "released" line when it ends, including on Ctrl+C:

```
☕ caff: keeping the display awake for 1h 0m (until 3:47 PM)
```

In command mode only the startup line is printed, so caff's output never interleaves with the wrapped command's. When stderr is redirected (scripts, pipes, CI), caff is completely silent, matching `caffeinate`, and stdout is always reserved for wrapped-command output.

Examples:

```
caff -d -t 3600          # keep the display awake for an hour
caff -d -t 1h            # same, using the suffix form (caff only)
caff -w 4242             # keep the system awake until pid 4242 exits
caff -di .\my-build.cmd  # keep system and display awake while a build runs
```

The command is resolved like `execvp` on macOS: from PATH, not from the current directory. This is why the build example above uses the `.\` prefix.

caff registers its keep-awake request through the Windows power request API. You can see it by running the following from an elevated prompt:

```
powercfg /requests
```

The reason string shows caff's flags and the wrapped command's name; the command's arguments are omitted so secrets never land in power diagnostics.

## Flags from caffeinate that caff does not support

These flags are rejected with an error rather than silently accepted, so a script that relies on them fails loudly instead of misbehaving:

- **`-m`** (prevent disk from idle sleeping): Windows has no per-process disk-idle assertion API; disk spin-down is purely a power-plan setting. There is no way to implement this without globally rewriting power-plan settings.
- **`-s`** (prevent system sleep, AC power only): Windows power requests make no distinction between "prevent idle sleep" and "prevent sleep while on AC"; `-s` would behave identically to `-i`. Use `-i`.
- **`-u`** (declare user activity): on macOS this wakes a dark display. Windows has no native way to wake the display; it would require synthesizing fake input. Without the wake, `-u` is just `-d` with a 5-second default timeout, so it is not worth emulating. Use `-d`.

## Building

Requires the .NET 10 SDK. No package dependencies.

```
dotnet publish src/caff/caff.csproj -c Release
```

The output is a single-file executable at `src/caff/bin/Release/net10.0/win-x64/publish/caff.exe` (framework-dependent; needs the .NET 10 runtime on the target machine). Copy `caff.exe` to a folder on your PATH to run it as `caff` from any terminal.

To run the tests (from the repo root, via `caff.slnx`):

```
dotnet test
```

## Acknowledgments

caff's command-line interface is modeled on Apple's [`caffeinate`](https://github.com/apple-oss-distributions/PowerManagement/blob/main/caffeinate/caffeinate.c). It is an independent reimplementation containing no Apple code, distributed under the [MIT license](LICENSE).
