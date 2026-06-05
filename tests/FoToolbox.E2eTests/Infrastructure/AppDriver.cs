using System;
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.UIA3;

namespace FoToolbox.E2eTests.Infrastructure;

/// <summary>
/// Launches the built FoToolbox.Host.exe with a deterministic, offline, isolated
/// environment and exposes its main window for FlaUI-driven tests.
/// </summary>
internal sealed class AppDriver : IDisposable
{
    private readonly string _tempLocalAppData;

    public Application App { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; }

    private AppDriver(Application app, UIA3Automation automation, Window mainWindow, string tempLocalAppData)
    {
        App = app;
        Automation = automation;
        MainWindow = mainWindow;
        _tempLocalAppData = tempLocalAppData;
    }

    /// <summary>
    /// Launches the host with an isolated, offline data dir. <paramref name="seedAppDataDir"/>, if
    /// supplied, runs against that throwaway dir <b>before</b> launch — e.g. to seed a profile into
    /// <c>profile.db</c> so the plugin tabs load (plugins only load when a profile is active).
    /// </summary>
    public static AppDriver Launch(Action<string>? seedAppDataDir = null)
    {
        var exe = LocateHostExe();
        var tmp = Path.Combine(Path.GetTempPath(), "fotoolbox-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        seedAppDataDir?.Invoke(tmp);

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        psi.Environment["LOCALAPPDATA"] = tmp;
        // LOCALAPPDATA alone does not isolate the app: Environment.SpecialFolder.LocalApplicationData
        // is resolved by the Windows shell and ignores the process env override, so the app would
        // otherwise read the real user's %LOCALAPPDATA%\FoToolbox profile.db (including any stale
        // Dataverse token that pops a blocking "sign-in required" dialog on launch). FOTOOLBOX_APPDATA_DIR
        // forces ProfilePaths to use this throwaway dir, guaranteeing a clean no-profile launch.
        psi.Environment["FOTOOLBOX_APPDATA_DIR"] = tmp;
        psi.Environment["FOTOOLBOX_UPDATE_MANIFEST"] = "";

        var app = Application.Launch(psi);
        var automation = new UIA3Automation();
        var main = app.GetMainWindow(automation, TimeSpan.FromSeconds(30))
            ?? throw new InvalidOperationException("Main window did not appear within 30s.");

        return new AppDriver(app, automation, main, tmp);
    }

    /// <summary>Best-effort screenshot for failure triage.</summary>
    public void CaptureScreenshot(string name)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "e2e-screenshots");
            Directory.CreateDirectory(dir);
            Capture.Screen().ToFile(Path.Combine(dir, name + ".png"));
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Copies the host's log dir out of the throwaway data dir (which is about to be deleted) into a
    /// persistent <c>e2e-logs</c> folder under the test output, so a failed launch can be triaged.
    /// </summary>
    private void PreserveHostLogs()
    {
        try
        {
            var src = Path.Combine(_tempLocalAppData, "logs");
            if (!Directory.Exists(src)) return;
            var dst = Path.Combine(AppContext.BaseDirectory, "e2e-logs");
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*.log"))
            {
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), overwrite: true);
            }
        }
        catch { /* best-effort */ }
    }

    private static string LocateHostExe()
    {
        const string config =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FoToolbox.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Could not locate repo root (no FoToolbox.sln found walking up from '{AppContext.BaseDirectory}').");
        }

        var exe = Path.Combine(dir.FullName, "src", "FoToolbox.Host", "bin", config, "net10.0-windows", "FoToolbox.Host.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"FoToolbox.Host.exe not found at '{exe}'. Build the solution in {config} first.");
        }
        return exe;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { App?.Close(); } catch { /* ignore */ }

        // Let graceful WPF shutdown (clean SQLite close in MainWindow.OnClosed) finish
        // before forcing — important once flows write profile data.
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (App is { HasExited: false } && DateTime.UtcNow < deadline)
            {
                System.Threading.Thread.Sleep(100);
            }
        }
        catch { /* ignore */ }

        try { if (App is { HasExited: false }) App.Kill(); } catch { /* ignore */ }
        try { Automation?.Dispose(); } catch { /* ignore */ }
        PreserveHostLogs();
        try { if (Directory.Exists(_tempLocalAppData)) Directory.Delete(_tempLocalAppData, recursive: true); }
        catch { /* best-effort */ }
    }
}
