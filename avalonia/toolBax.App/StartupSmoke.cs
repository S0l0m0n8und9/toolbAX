using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
#if WEBVIEW2
using Microsoft.Web.WebView2.Core;
#endif

namespace ToolBax.App;

internal sealed record StartupAssembly(string Name, string Configuration, string InformationalVersion, string FileVersion);
internal sealed record StartupWebView(bool Compiled, string Status, string? Version, string? Error = null);
internal sealed record StartupObservation(bool Windows, bool MainWindowReady, bool HomeReady, bool RealComposition,
    bool Degraded, int ProfileCount, bool HasActiveEnvironment, string ProfileDbPath, string[] Tools,
    StartupAssembly[] Assemblies, StartupWebView WebView2);

internal sealed class StartupSmoke : IDisposable
{
    private static readonly string[] ExpectedTools = { "home", "profiles", "query", "post", "metadata", "ops", "compare", "mapbrowser", "virtualtables" };
    private static readonly string[] ExpectedAssemblies = { "toolbAX", "FoToolbox.Core", "toolBax.Core" };
    private readonly FileStream _report;
    private readonly object _sync = new();
    private readonly List<string> _failures = new();
    private StartupObservation? _observation;
    private bool _completed;
    internal static StartupSmoke? Current { get; set; }
    internal string DataDirectory { get; }
    internal string ReportPath { get; }

    private StartupSmoke(string data, string report)
    {
        DataDirectory = data;
        ReportPath = report;
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(Path.GetDirectoryName(report)!);
        _report = new FileStream(report, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    internal static StartupSmoke? Prepare(string[] args)
    {
        if (!args.Any(a => a.StartsWith("--smoke", StringComparison.OrdinalIgnoreCase))) return null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flag = false;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--smoke-test" && !flag) { flag = true; continue; }
            if (args[i] is not ("--smoke-data-dir" or "--smoke-report") || i + 1 >= args.Length
                || !values.TryAdd(args[i], args[++i]))
                throw new ArgumentException("Smoke requires one --smoke-test, --smoke-data-dir and --smoke-report, with no other arguments.");
        }
        if (!flag || !values.TryGetValue("--smoke-data-dir", out var data) || !values.TryGetValue("--smoke-report", out var report)
            || !Path.IsPathFullyQualified(data) || !Path.IsPathFullyQualified(report))
            throw new ArgumentException("Smoke data and report paths must both be explicit absolute paths.");
        data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(data));
        report = Path.GetFullPath(report);
        if (string.IsNullOrEmpty(Path.GetFileName(report)) || File.Exists(data)
            || Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any())
            throw new ArgumentException("Smoke data must be absent or empty, and the report must name a new file.");
        return new StartupSmoke(data, report);
    }

    internal void Observe(StartupObservation observation) { lock (_sync) _observation = observation; }
    internal void RecordFailure(string message) { lock (_sync) _failures.Add(message); }

    internal void Attach(MainWindow window, IClassicDesktopStyleApplicationLifetime desktop, ShellViewModel shell, bool realComposition)
    {
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Opacity = 0; // Native startup/XAML still run, without a visible or activated smoke window.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var assemblies = new[] { typeof(App).Assembly, typeof(FoEnvironment).Assembly, typeof(EnvProfile).Assembly }
                    .Select(a => new StartupAssembly(a.GetName().Name!,
                        a.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? string.Empty,
                        a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty,
                        a.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? string.Empty)).ToArray();
                Observe(new StartupObservation(OperatingSystem.IsWindows(), window.IsLoaded && window.IsVisible
                    && window.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "HeaderBar"),
                    shell.CurrentTool.Id == "home" && shell.CurrentContent is PluginsHomeViewModel home && home.Plugins.Count > 0
                    && window.GetVisualDescendants().OfType<PluginsHomeView>().Any(v => v.IsLoaded),
                    realComposition, shell.IsDegraded, shell.Environments.Count, shell.ActiveEnvironment is not null,
                    ProfilePaths.ResolveProfileDbPath(), shell.Tools.Select(t => t.Id).ToArray(), assemblies, ProbeWebView()));
                if (!string.IsNullOrWhiteSpace(shell.BackgroundError)) RecordFailure(shell.BackgroundError);
            }
            catch (Exception ex) { RecordFailure($"Startup readiness failed: {ex.GetType().Name}: {ex.Message}"); }
            finally { desktop.Shutdown(0); }
        }, DispatcherPriority.ApplicationIdle);
    }

    private static StartupWebView ProbeWebView()
    {
#if WEBVIEW2
        if (!OperatingSystem.IsWindows()) return new(true, "not-windows", null);
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString(null);
            return new(true, string.IsNullOrWhiteSpace(version) ? "missing-runtime" : "available", version);
        }
        catch (WebView2RuntimeNotFoundException ex) { return new(true, "missing-runtime", null, ex.Message); }
        catch (Exception ex) { return new(true, "loader-failure", null, $"{ex.GetType().Name}: {ex.Message}"); }
#else
        return new(false, "not-compiled", null);
#endif
    }

    internal int Complete(int exitCode)
    {
        lock (_sync)
        {
            if (_completed) throw new InvalidOperationException("Smoke report is already finalized.");
            _completed = true;
            var observation = _observation;
            if (exitCode != 0) _failures.Add($"Desktop exited with code {exitCode}.");
            if (observation is null) _failures.Add("Desktop readiness was never observed.");
            else
            {
                if (!observation.Windows || !observation.MainWindowReady || !observation.HomeReady) _failures.Add("Native Windows MainWindow/XAML/home is not ready.");
                if (!observation.RealComposition || observation.Degraded) _failures.Add("Startup used degraded or fake composition.");
                if (observation.ProfileCount != 0 || observation.HasActiveEnvironment) _failures.Add("Smoke startup was not an empty profile environment.");
                if (!string.Equals(observation.ProfileDbPath, Path.Combine(DataDirectory, "profile.db"),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) _failures.Add("Profile database escaped the requested smoke data directory.");
                if (ExpectedTools.Except(observation.Tools, StringComparer.Ordinal).Any()) _failures.Add("Expected tools are missing.");
                if (ExpectedAssemblies.Except(observation.Assemblies.Select(a => a.Name), StringComparer.Ordinal).Any()
                    || observation.Assemblies.Any(a => a.Configuration != "Release")) _failures.Add("Runtime assemblies are not all Release-built.");
                if (!observation.WebView2.Compiled || observation.WebView2.Status != "available"
                    || string.IsNullOrWhiteSpace(observation.WebView2.Version)) _failures.Add($"WebView2 capability unavailable: {observation.WebView2.Status}.");
            }
            var success = _failures.Count == 0;
            var resultCode = success ? 0 : 1;
            JsonSerializer.Serialize(_report, new { schemaVersion = 1, success, exitCode = resultCode,
                dataDirectory = DataDirectory, executable = Environment.ProcessPath, observation, failures = _failures.ToArray() },
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            _report.Flush(flushToDisk: true);
            _report.Dispose();
            return resultCode;
        }
    }

    public void Dispose() => _report.Dispose();
}
