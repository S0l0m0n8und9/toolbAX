using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class StartupSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "toolbax-smoke-test-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    private string Report => Path.Combine(_root, "report.json");
    public StartupSmokeTests() => Directory.CreateDirectory(_root);
    private string[] Args => new[] { "--smoke-test", "--smoke-data-dir", Data, "--smoke-report", Report };
    private StartupObservation Ready() => new(true, true, true, true, false, 0, false,
        Path.Combine(Data, "profile.db"), new[] { "home", "profiles", "query", "post", "metadata", "ops", "compare", "mapbrowser", "virtualtables" },
        new[] { new StartupAssembly("toolbAX", "Release", "1.2.3+" + new string('a', 40), "1.2.3.0"),
            new StartupAssembly("FoToolbox.Core", "Release", "1.2.3+" + new string('a', 40), "1.2.3.0"),
            new StartupAssembly("toolBax.Core", "Release", "1.2.3+" + new string('a', 40), "1.2.3.0") },
        new StartupWebView(true, "available", "142.0.0.0"));

    [Fact]
    public void Normal_launch_does_not_allocate_smoke_state() => Assert.Null(StartupSmoke.Prepare(Array.Empty<string>()));

    [Theory]
    [InlineData("missingPaths")]
    [InlineData("missingReport")]
    [InlineData("missingFlag")]
    [InlineData("relativeData")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public void Invalid_smoke_arguments_fail_before_creating_data_or_report(string kind)
    {
        var args = kind switch
        {
            "missingPaths" => new[] { "--smoke-test" },
            "missingReport" => new[] { "--smoke-test", "--smoke-data-dir", Data },
            "missingFlag" => new[] { "--smoke-data-dir", Data, "--smoke-report", Report },
            "relativeData" => new[] { "--smoke-test", "--smoke-data-dir", "relative", "--smoke-report", Report },
            "duplicate" => new[] { "--smoke-test", "--smoke-test", "--smoke-data-dir", Data, "--smoke-report", Report },
            _ => new[] { "--smoke-test", "--smoke-data-dir", Data, "--smoke-report", Report, "--unexpected" },
        };
        Assert.Throws<ArgumentException>(() => StartupSmoke.Prepare(args));
        Assert.False(Directory.Exists(Data));
        Assert.False(File.Exists(Report));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Fresh_or_empty_data_directory_is_accepted_and_report_is_reserved(bool createEmpty)
    {
        if (createEmpty) Directory.CreateDirectory(Data);
        using var smoke = Assert.IsType<StartupSmoke>(StartupSmoke.Prepare(Args));
        Assert.Equal(Data, smoke.DataDirectory);
        Assert.Equal(Report, smoke.ReportPath);
        Assert.True(Directory.Exists(Data));
        Assert.True(File.Exists(Report));
        Assert.Throws<IOException>(() => StartupSmoke.Prepare(Args));
    }

    [Fact]
    public void Nonempty_data_is_rejected_without_touching_existing_profile()
    {
        Directory.CreateDirectory(Data);
        var sentinel = Path.Combine(Data, "profile.db");
        File.WriteAllText(sentinel, "existing user data");
        Assert.Throws<ArgumentException>(() => StartupSmoke.Prepare(Args));
        Assert.Equal("existing user data", File.ReadAllText(sentinel));
        Assert.False(File.Exists(Report));
    }

    [Fact]
    public void Existing_report_is_never_overwritten()
    {
        File.WriteAllText(Report, "previous evidence");
        Assert.Throws<IOException>(() => StartupSmoke.Prepare(Args));
        Assert.Equal("previous evidence", File.ReadAllText(Report));
    }

    [Fact]
    public void Ready_shutdown_writes_machine_readable_success_once()
    {
        using var smoke = Assert.IsType<StartupSmoke>(StartupSmoke.Prepare(Args));
        smoke.Observe(Ready());
        Assert.Equal(0, smoke.Complete(0));
        using var json = JsonDocument.Parse(File.ReadAllText(Report));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(Data, json.RootElement.GetProperty("dataDirectory").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Throws<InvalidOperationException>(() => smoke.Complete(0));
    }

    [Theory]
    [InlineData("noWindow")]
    [InlineData("noHome")]
    [InlineData("fake")]
    [InlineData("degraded")]
    [InlineData("profiles")]
    [InlineData("active")]
    [InlineData("wrongData")]
    [InlineData("debugCore")]
    [InlineData("missingTool")]
    [InlineData("runtime")]
    [InlineData("loader")]
    [InlineData("background")]
    [InlineData("nonzero")]
    [InlineData("noObservation")]
    public void Incomplete_or_failed_startup_never_reports_success(string failure)
    {
        using var smoke = Assert.IsType<StartupSmoke>(StartupSmoke.Prepare(Args));
        var ready = Ready();
        var observation = failure switch
        {
            "noWindow" => ready with { MainWindowReady = false },
            "noHome" => ready with { HomeReady = false },
            "fake" => ready with { RealComposition = false },
            "degraded" => ready with { Degraded = true },
            "profiles" => ready with { ProfileCount = 1 },
            "active" => ready with { HasActiveEnvironment = true },
            "wrongData" => ready with { ProfileDbPath = Path.Combine(_root, "outside.db") },
            "debugCore" => ready with { Assemblies = new[] { ready.Assemblies[0], ready.Assemblies[1] with { Configuration = "Debug" }, ready.Assemblies[2] } },
            "missingTool" => ready with { Tools = new[] { "home" } },
            "runtime" => ready with { WebView2 = new StartupWebView(true, "missing-runtime", null) },
            "loader" => ready with { WebView2 = new StartupWebView(true, "loader-failure", null) },
            _ => ready,
        };
        if (failure != "noObservation") smoke.Observe(observation);
        if (failure == "background") smoke.RecordFailure("background startup fault");
        Assert.Equal(1, smoke.Complete(failure == "nonzero" ? 17 : 0));
        using var json = JsonDocument.Parse(File.ReadAllText(Report));
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.NotEmpty(json.RootElement.GetProperty("failures").EnumerateArray());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
