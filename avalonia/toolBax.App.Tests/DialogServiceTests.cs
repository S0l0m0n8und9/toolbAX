using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ToolBax.App.Services;
using ToolBax.App.Views;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class DialogServiceTests
{
    private static ConfirmRequest Request() => new(
        "Enable debug mode?", "Changes F&O project logging.", new[] { "Customers · project-1" },
        "Enable", IsDanger: false);

    [AvaloniaFact]
    public async Task Off_UI_thread_cancellation_closes_the_actual_dialog_and_leaves_owner_usable()
    {
        var owner = new Window();
        owner.Show();
        ConfirmWindow? shown = null;
        var service = new DialogService(() => owner, () => shown = new ConfirmWindow());
        using var cts = new CancellationTokenSource();
        try
        {
            var pending = service.ConfirmAsync(Request(), cts.Token);
            Dispatcher.UIThread.RunJobs();
            Assert.True(shown!.IsVisible);

            await Task.Run(cts.Cancel);
            Dispatcher.UIThread.RunJobs();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.False(shown.IsVisible);
            Assert.True(owner.IsVisible);
        }
        finally
        {
            shown?.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task Pre_cancel_does_not_create_or_show_a_dialog()
    {
        var owner = new Window();
        var factoryCalls = 0;
        var service = new DialogService(() => owner, () =>
        {
            factoryCalls++;
            return new ConfirmWindow();
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConfirmAsync(Request(), cts.Token));

        Assert.Equal(0, factoryCalls);
    }

    [AvaloniaFact]
    public async Task No_owner_fails_closed_without_creating_a_dialog()
    {
        var factoryCalls = 0;
        var service = new DialogService(() => null, () =>
        {
            factoryCalls++;
            return new ConfirmWindow();
        });

        var confirmed = await service.ConfirmAsync(Request(), TestContext.Current.CancellationToken);

        Assert.False(confirmed);
        Assert.Equal(0, factoryCalls);
    }

    [AvaloniaFact]
    public async Task Normal_approval_returns_true()
    {
        var owner = new Window();
        owner.Show();
        ConfirmWindow? shown = null;
        var service = new DialogService(() => owner, () => shown = new ConfirmWindow());
        try
        {
            var pending = service.ConfirmAsync(Request(), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            shown!.Close(true);

            Assert.True(await pending);
        }
        finally
        {
            shown?.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task Cancellation_wins_a_racing_approval()
    {
        var owner = new Window();
        owner.Show();
        ConfirmWindow? shown = null;
        var service = new DialogService(() => owner, () => shown = new ConfirmWindow());
        using var cts = new CancellationTokenSource();
        try
        {
            var pending = service.ConfirmAsync(Request(), cts.Token);
            Dispatcher.UIThread.RunJobs();

            cts.Cancel();
            shown!.Close(true);
            Dispatcher.UIThread.RunJobs();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        }
        finally
        {
            shown?.Close();
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task Completed_registration_cannot_close_a_later_dialog()
    {
        var owner = new Window();
        owner.Show();
        ConfirmWindow? current = null;
        var service = new DialogService(() => owner, () => current = new ConfirmWindow());
        using var firstCts = new CancellationTokenSource();
        using var secondCts = new CancellationTokenSource();
        try
        {
            var first = service.ConfirmAsync(Request(), firstCts.Token);
            Dispatcher.UIThread.RunJobs();
            current!.Close(true);
            Assert.True(await first);

            var second = service.ConfirmAsync(Request(), secondCts.Token);
            Dispatcher.UIThread.RunJobs();
            var secondWindow = current!;
            firstCts.Cancel();
            Dispatcher.UIThread.RunJobs();

            Assert.True(secondWindow.IsVisible);
            secondWindow.Close(false);
            Assert.False(await second);
        }
        finally
        {
            current?.Close();
            owner.Close();
        }
    }
}
