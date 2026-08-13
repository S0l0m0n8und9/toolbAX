using System.Diagnostics;
using System.Text;

namespace ToolBax.App.Tests;

/// <summary>
/// Captures everything written to <see cref="Trace"/> while it is alive, so a test can assert what a code
/// path reports — and, for the secrecy checks, what it deliberately does not.
/// <para>
/// <see cref="Trace.Listeners"/> is process-wide and test classes run in parallel, so another class's
/// traces can land in this capture: assert with <c>Contains</c>/<c>DoesNotContain</c> over
/// <see cref="Text"/>, never an exact transcript. Appends under its own lock because those writes arrive
/// from other threads.
/// </para>
/// </summary>
internal sealed class TraceCapture : TraceListener
{
    private readonly StringBuilder _text = new();
    private readonly object _gate = new();

    public TraceCapture() => Trace.Listeners.Add(this);

    public string Text
    {
        get
        {
            Trace.Flush();
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    public override void Write(string? message)
    {
        lock (_gate)
        {
            _text.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_gate)
        {
            _text.Append(message).AppendLine();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Trace.Listeners.Remove(this);
        }

        base.Dispose(disposing);
    }
}
