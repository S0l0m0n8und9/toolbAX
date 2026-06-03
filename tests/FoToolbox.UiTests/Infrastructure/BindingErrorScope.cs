using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Data;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Captures WPF data-binding trace output (PresentationTraceSources.DataBindingSource)
/// for the lifetime of the scope. Any captured message indicates a binding failure.
/// </summary>
internal sealed class BindingErrorScope : IDisposable
{
    private readonly CollectingTraceListener _listener = new();
    private readonly SourceLevels _previousLevel;

    public BindingErrorScope()
    {
        PresentationTraceSources.Refresh();
        _previousLevel = PresentationTraceSources.DataBindingSource.Switch.Level;
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning | SourceLevels.Error;
        PresentationTraceSources.DataBindingSource.Listeners.Add(_listener);
    }

    public IReadOnlyList<string> Errors => _listener.Messages;

    public void Dispose()
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = _previousLevel;
    }

    private sealed class CollectingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = new();

        public override void Write(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message!);
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message!);
        }
    }
}
