using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using FoToolbox.Core.Profiles;

namespace ToolBax.App.Services;

/// <summary>
/// Persistent per-session trace log (#168). The app already reports plenty through
/// <see cref="Trace"/> — the degraded-mode reason, the last-resort exception net (#163), the shell's
/// environment-switch warnings, vault warnings, failed requests — but the published exe installed no
/// listener, so every one of those lines went nowhere and a user hitting errors left nothing on disk to
/// diagnose. This attaches a <see cref="TextWriterTraceListener"/> over one file per session.
/// <para>
/// Deliberately a stdlib <see cref="TextWriterTraceListener"/> and not a logging framework: the app has
/// exactly one sink, one process, and no need for structured events or filtering — a dependency would buy
/// nothing here.
/// </para>
/// <para>
/// <b>Never</b> let logging stop the app starting: every failure path in <see cref="Start()"/> is
/// swallowed and costs the log, not the session.
/// </para>
/// </summary>
public static class SessionTraceLog
{
    /// <summary>Sub-directory of the FoToolbox app-data root that holds session logs.</summary>
    public const string DirectoryName = "logs";

    /// <summary>Name given to the installed listener, so a diagnostic can spot it in <see cref="Trace.Listeners"/>.</summary>
    public const string ListenerName = "toolbax-session";

    /// <summary>Glob that matches session logs (and only session logs) in the log directory.</summary>
    public const string FileSearchPattern = "toolbax-*.log";

    /// <summary>At most this many session logs are kept; the oldest are deleted on the next start.</summary>
    public const int MaxFiles = 20;

    /// <summary>Session logs last written longer ago than this are deleted on the next start.</summary>
    public static TimeSpan MaxAge { get; } = TimeSpan.FromDays(14);

    /// <summary>
    /// Full path of the log this process is writing, or <c>null</c> when session logging is not running
    /// (never started, failed to start, or already disposed).
    /// </summary>
    public static string? ActiveLogPath { get; private set; }

    /// <summary>Resolves <c>%LocalAppData%\FoToolbox\logs</c> (or the test override root). Creates nothing.</summary>
    public static string ResolveLogDirectory() => ProfilePaths.ResolveAppDataPath(DirectoryName);

    /// <summary>
    /// Starts session logging under <see cref="ResolveLogDirectory"/>. Returns a handle that flushes and
    /// closes the file; the returned handle never throws and a second dispose is a no-op.
    /// </summary>
    public static IDisposable Start() => Start(ResolveLogDirectory());

    /// <summary>
    /// Starts session logging in an explicit directory. Production calls the no-arg overload; an explicit
    /// directory lets the tests run without touching either the developer's real
    /// <c>%LocalAppData%\FoToolbox\logs</c> or the process-wide
    /// <see cref="ProfilePaths.AppDataDirEnvVar"/> override (which another test class already owns, and
    /// which would cross-talk if two classes set it in parallel).
    /// </summary>
    public static IDisposable Start(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            ApplyRetention(logDirectory);

            var stream = CreateSessionFile(logDirectory);
            var writer = new StreamWriter(stream);
            WriteHeader(writer);

            var listener = new TextWriterTraceListener(writer, ListenerName);
            var previousAutoFlush = Trace.AutoFlush;
            Trace.Listeners.Add(listener);
            // A crash must not lose the tail: the lines that matter are the last ones written before the
            // process died, so pay a flush per write rather than buffer them into oblivion.
            Trace.AutoFlush = true;
            ActiveLogPath = stream.Name;
            return new Session(listener, previousAutoFlush);
        }
        catch (Exception ex)
        {
            // A read-only or full disk, a locked directory, a redirected-profile failure: all cost the log
            // and nothing else. Traced (to whatever listener a debugger-attached run has) rather than
            // silently dropped, so "why is there no log?" is answerable.
            Trace.TraceWarning($"Session logging is unavailable; this run will not be logged to file. {ex.Message}");
            return NoSession.Instance;
        }
    }

    /// <summary>
    /// Deletes session logs beyond the newest <see cref="MaxFiles"/> or last written more than
    /// <see cref="MaxAge"/> ago. No size-based rolling: one file per session is the unit a user reports
    /// and the unit that gets deleted, so a long session stays in one piece.
    /// <para>
    /// Runs <b>before</b> this session's file is opened and keeps <see cref="MaxFiles"/> - 1 of the
    /// existing logs, so the new file brings the total back to <see cref="MaxFiles"/> and deleting our own
    /// open file is impossible.
    /// </para>
    /// </summary>
    private static void ApplyRetention(string logDirectory)
    {
        try
        {
            var cutoff = DateTime.UtcNow - MaxAge;
            var existing = new DirectoryInfo(logDirectory)
                .GetFiles(FileSearchPattern)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            for (var i = 0; i < existing.Length; i++)
            {
                if (i < MaxFiles - 1 && existing[i].LastWriteTimeUtc >= cutoff)
                {
                    continue;
                }

                TryDelete(existing[i]);
            }
        }
        catch (Exception ex)
        {
            // Housekeeping is not worth the log: an unreadable directory must not cost this session's file.
            Trace.TraceWarning($"Session-log retention did not run. {ex.Message}");
        }
    }

    // A log still held open by a second running instance simply survives to the next start.
    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // One file per session. Two instances starting inside the same second would collide on the timestamped
    // name, so the loser takes a numbered suffix; FileMode.CreateNew is what detects the clash, because an
    // "does it exist?" check would race with the other process. FileShare.Read so the user can open the
    // log (or paste it into a bug report) while the app is still running.
    private static FileStream CreateSessionFile(string logDirectory)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        for (var attempt = 1; ; attempt++)
        {
            var name = attempt == 1 ? $"toolbax-{stamp}.log" : $"toolbax-{stamp}-{attempt}.log";
            var path = Path.Combine(logDirectory, name);
            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            // Only retry a genuine name clash, and only a few times — anything else (and the tenth
            // collision) is a real problem that belongs in Start's catch.
            catch (IOException) when (attempt < 10 && File.Exists(path))
            {
            }
        }
    }

    // Written straight to the file rather than through Trace so it always heads the log, whatever the
    // listener collection looks like.
    private static void WriteHeader(TextWriter writer)
    {
        var assembly = typeof(SessionTraceLog).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        // Deliberately identity-free: no user name, machine name, tenant, environment URL, profile name or
        // file path. A user must be able to hand this file to support without handing over who they are or
        // which customer environment they were pointed at.
        writer.WriteLine($"toolbAX session log · started {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        writer.WriteLine($"version {version}");
        writer.WriteLine($"runtime {RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        writer.WriteLine(new string('-', 72));
        writer.Flush();
    }

    // Detaches the listener and closes the file. Removing it from Trace.Listeners first means a trace
    // raised while we are closing can't reach a half-disposed writer.
    private sealed class Session : IDisposable
    {
        private readonly bool _previousAutoFlush;
        private TextWriterTraceListener? _listener;

        public Session(TextWriterTraceListener listener, bool previousAutoFlush)
        {
            _listener = listener;
            _previousAutoFlush = previousAutoFlush;
        }

        public void Dispose()
        {
            var listener = _listener;
            _listener = null;
            if (listener is null)
            {
                return;   // a second dispose is a no-op
            }

            Trace.Listeners.Remove(listener);
            Trace.AutoFlush = _previousAutoFlush;
            ActiveLogPath = null;

            try
            {
                listener.Flush();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (IOException)
            {
            }

            listener.Dispose();
        }
    }

    // Handed back when logging could not start, so callers need no null check and no try/finally special case.
    private sealed class NoSession : IDisposable
    {
        public static readonly NoSession Instance = new();

        public void Dispose()
        {
        }
    }
}
