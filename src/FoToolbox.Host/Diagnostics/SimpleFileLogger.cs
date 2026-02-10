using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace FoToolbox.Host.Diagnostics;

/// <summary>
/// Minimal file logger (no external deps) intended for local diagnostics.
/// </summary>
internal sealed class SimpleFileLogger : ILogger
{
    private readonly string _category;
    private readonly string _logFilePath;
    private readonly object _sync = new();

    public SimpleFileLogger(string category, string logFilePath)
    {
        _category = category;
        _logFilePath = logFilePath;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        if (formatter is null) return;

        string line;
        try
        {
            var msg = formatter(state, exception);
            line = $"{DateTime.UtcNow:O} [{logLevel}] {_category}: {msg}";

            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }
        }
        catch
        {
            // Never allow logging failures to crash the app.
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            lock (_sync)
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Swallow IO failures.
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

