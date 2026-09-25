using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDialogService"/>: shows a modal <see cref="ConfirmWindow"/> over the main window.
/// Thin windowing adapter — the confirm contract/logic is covered by <see cref="ConfirmDialogViewModel"/>
/// tests and the fake <c>IDialogService</c> in the view-model tests.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _ownerProvider;
    private readonly Func<ConfirmWindow> _dialogFactory;

    public DialogService() : this(
        () => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow,
        () => new ConfirmWindow())
    {
    }

    internal DialogService(Func<Window?> ownerProvider, Func<ConfirmWindow> dialogFactory)
    {
        _ownerProvider = ownerProvider;
        _dialogFactory = dialogFactory;
    }

    public Task<bool> ConfirmAsync(ConfirmRequest request) =>
        ConfirmAsync(request, CancellationToken.None);

    public async Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var owner = _ownerProvider();

        // No owner (e.g. not a desktop lifetime) → don't mutate silently.
        if (owner is null) return false;

        var dialog = _dialogFactory();
        dialog.DataContext = new ConfirmDialogViewModel(request);
        using var registration = ct.Register(static state =>
        {
            var window = (ConfirmWindow)state!;
            Dispatcher.UIThread.Post(() =>
            {
                if (window.IsVisible) window.Close(false);
            });
        }, dialog);

        // Covers cancellation between the initial check and registration without showing a window.
        ct.ThrowIfCancellationRequested();
        var confirmed = await dialog.ShowDialog<bool>(owner);
        // Cancellation wins a racing approval; callers must never dispatch after their token is cancelled.
        ct.ThrowIfCancellationRequested();
        return confirmed;
    }
}
