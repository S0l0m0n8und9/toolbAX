using FoToolbox.Host.Diagnostics;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FoToolbox.Host;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDiagnostics.Initialize();
        AppDiagnostics.Logger.LogWarning(
            "The WPF host (FoToolbox.Host) is deprecated and no longer released. " +
            "The maintained app is the cross-platform Avalonia build (avalonia/toolBax.App).");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var crashPath = AppDiagnostics.WriteCrashReport(e.Exception, "DispatcherUnhandledException");
        AppDiagnostics.Logger.LogError(e.Exception, "Unhandled UI exception. Crash report: {CrashPath}", crashPath);

        try
        {
            var msg = string.IsNullOrWhiteSpace(crashPath)
                ? "An unexpected error occurred and the app must close."
                : $"An unexpected error occurred and the app must close.\n\nCrash report:\n{crashPath}\n\nLog:\n{AppDiagnostics.LogFilePath}";
            MessageBox.Show(msg, "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch
        {
            // Ignore UI failures during shutdown.
        }

        e.Handled = true;
        Shutdown(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            var crashPath = AppDiagnostics.WriteCrashReport(ex, "AppDomain.CurrentDomain.UnhandledException");
            AppDiagnostics.Logger.LogError(ex, "Unhandled exception. IsTerminating={IsTerminating}. Crash report: {CrashPath}", e.IsTerminating, crashPath);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        var crashPath = AppDiagnostics.WriteCrashReport(e.Exception, "TaskScheduler.UnobservedTaskException");
        AppDiagnostics.Logger.LogError(e.Exception, "Unobserved task exception. Crash report: {CrashPath}", crashPath);
        e.SetObserved();
    }
}

