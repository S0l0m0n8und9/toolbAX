using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Data;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Captures WPF data-binding trace output (PresentationTraceSources.DataBindingSource)
/// for the lifetime of the scope. Any captured message indicates a binding failure.
/// </summary>
/// <remarks>
/// Always use inside a <c>using</c> block. If Dispose is skipped the listener leaks into
/// the process-global DataBindingSource, so later scopes accumulate listeners and report
/// duplicate/false errors. Tests run non-parallel, so exactly one live scope is the
/// invariant. At Warning level, legitimate FallbackValue/TargetNullValue diagnostics can
/// also surface — if a real view trips a false positive, filter by message content or
/// quarantine that case rather than lowering the trace level globally.
/// </remarks>
internal sealed class BindingErrorScope : IDisposable
{
    private readonly CollectingTraceListener _listener = new();
    private readonly SourceLevels _previousLevel;

    public BindingErrorScope()
    {
        PresentationTraceSources.Refresh();
        _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        // SourceLevels.Warning already includes the Error and Critical bits.
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
    }

    public IReadOnlyList<string> Errors => _listener.Messages;

    public void Dispose()
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
        _listener.Dispose();
    }

    // WPF emits binding failures via TraceSource.TraceEvent. Capturing there records one
    // clean entry per failure, instead of the Write(header)+WriteLine(body) split that
    // would record each failure twice.
    private sealed class CollectingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void TraceEvent(
            TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        {
            if (IsFailure(eventType) && !string.IsNullOrWhiteSpace(message))
            {
                Messages.Add(message!);
            }
        }

        public override void TraceEvent(
            TraceEventCache? eventCache, string source, TraceEventType eventType, int id,
            string? format, params object?[]? args)
        {
            if (!IsFailure(eventType)) return;

            var text = format is not null && args is { Length: > 0 }
                ? string.Format(format, args!)
                : format;
            if (!string.IsNullOrWhiteSpace(text)) Messages.Add(text!);
        }

        public override void Write(string? message) { }

        public override void WriteLine(string? message) { }

        private static bool IsFailure(TraceEventType eventType) =>
            eventType is TraceEventType.Warning or TraceEventType.Error or TraceEventType.Critical;
    }
}
