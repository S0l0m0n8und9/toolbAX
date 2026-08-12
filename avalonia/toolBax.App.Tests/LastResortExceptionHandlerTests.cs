using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// The last-resort exception net (#163): a dispatcher job that throws — the shape CommunityToolkit's
/// AsyncRelayCommand produces when a command task faults — must be reported and swallowed instead of
/// taking the process (and every tool's unsaved state) down with it.
/// </summary>
public class LastResortExceptionHandlerTests
{
    [AvaloniaFact]
    public void An_unhandled_dispatcher_exception_is_reported_instead_of_killing_the_app()
    {
        var reported = new List<string>();
        using var net = App.InstallLastResortExceptionHandlers(reported.Add);

        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("clipboard is busy"));
        Dispatcher.UIThread.RunJobs(); // must not rethrow — the handler marks it handled

        // Not Assert.Single: the global TaskScheduler event is shared with every other test in the run.
        Assert.Contains(reported, m => m.Contains("clipboard is busy"));
    }

    [AvaloniaFact]
    public void Disposing_the_handle_removes_the_net()
    {
        var stale = new List<string>();
        App.InstallLastResortExceptionHandlers(stale.Add).Dispose();

        var live = new List<string>();
        using var net = App.InstallLastResortExceptionHandlers(live.Add);

        Dispatcher.UIThread.Post(() => throw new InvalidOperationException("only the live sink sees this"));
        Dispatcher.UIThread.RunJobs();

        Assert.DoesNotContain(stale, m => m.Contains("only the live sink sees this"));
        Assert.Contains(live, m => m.Contains("only the live sink sees this"));
    }
}
