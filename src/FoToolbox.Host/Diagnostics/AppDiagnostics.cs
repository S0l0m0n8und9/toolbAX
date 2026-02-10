using FoToolbox.Core.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;

namespace FoToolbox.Host.Diagnostics;

internal static class AppDiagnostics
{
    private static readonly object InitLock = new();
    private static bool _initialized;

    internal static ILogger Logger { get; private set; } = NullLogger.Instance;
    internal static string LogFilePath { get; private set; } = string.Empty;

    internal static void Initialize()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            _initialized = true;

            var logsDir = ProfilePaths.ResolveAppDataPath("logs");
            Directory.CreateDirectory(logsDir);

            LogFilePath = Path.Combine(logsDir, $"FoToolbox-{DateTime.UtcNow:yyyyMMdd}.log");
            Logger = new SimpleFileLogger("FoToolbox", LogFilePath);

            var crashesDir = ProfilePaths.ResolveAppDataPath("crashes");
            Directory.CreateDirectory(crashesDir);

            TryCleanupOldFiles(logsDir, "FoToolbox-*.log", daysToKeep: 14);
            TryCleanupOldFiles(crashesDir, "crash-*.txt", daysToKeep: 30);

            Logger.LogInformation("Diagnostics initialized. Log: {LogFile}", LogFilePath);
        }
    }

    internal static string WriteCrashReport(Exception ex, string? context = null)
    {
        try
        {
            var crashesDir = ProfilePaths.ResolveAppDataPath("crashes");
            Directory.CreateDirectory(crashesDir);

            var file = Path.Combine(crashesDir, $"crash-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
            var text =
                $"UTC: {DateTime.UtcNow:O}{Environment.NewLine}" +
                $"Context: {context}{Environment.NewLine}{Environment.NewLine}" +
                ex;
            File.WriteAllText(file, text);
            return file;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryCleanupOldFiles(string directory, string searchPattern, int daysToKeep)
    {
        try
        {
            var cutoffUtc = DateTime.UtcNow.AddDays(-daysToKeep);
            foreach (var file in Directory.GetFiles(directory, searchPattern))
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < cutoffUtc)
                {
                    info.Delete();
                }
            }
        }
        catch
        {
            // Best-effort retention only.
        }
    }
}

