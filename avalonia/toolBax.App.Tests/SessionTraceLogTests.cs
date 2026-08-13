using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using FoToolbox.Core.Profiles;
using ToolBax.App.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// The persistent per-session trace log (#168): the published exe installed no trace listener, so every
/// Trace.* report the app already made — the degraded-mode reason, the last-resort net's exception dumps,
/// failed requests — went nowhere, and a real user's failing session left nothing on disk to diagnose.
/// <para>
/// Every test drives <see cref="SessionTraceLog.Start(string)"/> with a throwaway directory rather than the
/// resolved app-data path, so nothing here touches the developer's real
/// <c>%LocalAppData%\FoToolbox\logs</c> and nothing here sets the process-wide
/// <see cref="ProfilePaths.AppDataDirEnvVar"/> override that <see cref="CompositionRootTests"/> owns.
/// </para>
/// </summary>
public class SessionTraceLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"toolbax-logtest-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private string[] Logs() => Directory.Exists(_dir)
        ? Directory.GetFiles(_dir, SessionTraceLog.FileSearchPattern)
        : Array.Empty<string>();

    // The file is still open while the session is running, so read it with the same sharing the writer allows.
    private static string ReadWhileOpen(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void Start_creates_one_session_file_headed_by_the_version_and_platform()
    {
        using (SessionTraceLog.Start(_dir))
        {
            var log = Assert.Single(Logs());
            Assert.StartsWith("toolbax-", Path.GetFileName(log), StringComparison.Ordinal);

            var header = ReadWhileOpen(log);
            Assert.Contains("toolbAX session log", header);

            // The version is the whole point of the header: a bug report has to say which build produced it.
            var version = typeof(SessionTraceLog).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
            Assert.Contains(version, header);
            Assert.Contains(".NET", header);   // runtime line

            // Identity-free by design: whoever reads this file learns the build, not the user.
            Assert.DoesNotContain(Environment.UserName, header, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Environment.MachineName, header, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_traced_line_reaches_the_session_file_without_waiting_for_shutdown()
    {
        // Trace.AutoFlush is the guarantee that matters: a crash must not take the tail of the log with it.
        using (SessionTraceLog.Start(_dir))
        {
            Trace.TraceError("kaboom-marker");

            Assert.Contains("kaboom-marker", ReadWhileOpen(Assert.Single(Logs())));
        }
    }

    [Fact]
    public void Disposing_the_session_detaches_the_listener_so_later_traces_are_not_written()
    {
        SessionTraceLog.Start(_dir).Dispose();
        var log = Assert.Single(Logs());

        Trace.TraceError("after-dispose-marker");

        Assert.DoesNotContain("after-dispose-marker", File.ReadAllText(log));
        Assert.Null(SessionTraceLog.ActiveLogPath);
    }

    [Fact]
    public void Retention_keeps_the_newest_files_up_to_the_cap()
    {
        Directory.CreateDirectory(_dir);
        // Distinct write times so "newest" is unambiguous; the cap, not the age rule, is what's under test.
        for (var i = 0; i < SessionTraceLog.MaxFiles + 5; i++)
        {
            var path = Path.Combine(_dir, $"toolbax-2026010{i / 10}-0000{i % 10}.log");
            File.WriteAllText(path, $"old session {i}");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-i));
        }

        using (SessionTraceLog.Start(_dir))
        {
            // Retention runs before this session's file is opened and keeps MaxFiles - 1, so the new file
            // brings the total back to exactly the cap.
            Assert.Equal(SessionTraceLog.MaxFiles, Logs().Length);
            Assert.Contains(Logs(), log => log == SessionTraceLog.ActiveLogPath);
            // The oldest went first: "old session 0" is the newest of the pre-existing files and survives.
            Assert.Contains(Logs(), log => log.EndsWith("20260100-00000.log", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Retention_deletes_files_past_the_age_limit_even_under_the_cap()
    {
        Directory.CreateDirectory(_dir);
        var stale = Path.Combine(_dir, "toolbax-20250101-000000.log");
        var recent = Path.Combine(_dir, "toolbax-20260101-000000.log");
        File.WriteAllText(stale, "stale");
        File.WriteAllText(recent, "recent");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - SessionTraceLog.MaxAge - TimeSpan.FromDays(1));
        File.SetLastWriteTimeUtc(recent, DateTime.UtcNow - TimeSpan.FromDays(1));

        using (SessionTraceLog.Start(_dir))
        {
            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(recent));
        }
    }

    [Fact]
    public void A_start_failure_costs_the_log_and_not_the_session()
    {
        // A file where the log directory should be: Directory.CreateDirectory throws, which is the shape of
        // every real failure here (read-only volume, denied ACL, redirected profile). Starting the app must
        // survive all of them.
        var blocked = Path.Combine(_dir, "blocked");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(blocked, "not a directory");

        var handle = SessionTraceLog.Start(blocked);   // must not throw

        Assert.Null(SessionTraceLog.ActiveLogPath);
        handle.Dispose();
        handle.Dispose();   // a second dispose is a no-op, whether or not logging started
    }

    [AvaloniaFact]
    public void The_headless_test_host_has_no_desktop_lifetime_so_it_never_starts_a_session_log()
    {
        // A classic desktop lifetime is what scopes Start() (and the #163 net) to a real desktop run. If
        // the headless host ever grew one, the whole suite would start littering the developer's app-data —
        // which is what the "no log in the real location" test below would then catch.
        Assert.NotNull(Application.Current);   // the real App type really is initialised here
        Assert.Null(Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime);
    }

    [Fact]
    public void The_test_suite_writes_no_session_log_to_the_real_app_data_location()
    {
        var production = SessionTraceLog.ResolveLogDirectory();
        if (!Directory.Exists(production))
        {
            return;   // nothing has ever logged here (every CI run) — the strongest possible pass
        }

        // A developer machine that has run the app has real logs here. None of them may have been created
        // by this test process: every test above writes to its own temp directory instead.
        var startedAt = Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var written = Directory.GetFiles(production, SessionTraceLog.FileSearchPattern)
            .Where(log => File.GetCreationTimeUtc(log) >= startedAt)
            .ToArray();

        Assert.Empty(written);
    }
}
