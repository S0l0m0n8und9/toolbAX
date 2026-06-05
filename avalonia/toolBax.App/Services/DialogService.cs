using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
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
    public async Task<bool> ConfirmAsync(ConfirmRequest request)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var dialog = new ConfirmWindow { DataContext = new ConfirmDialogViewModel(request) };

        // No owner (e.g. not a desktop lifetime) → don't mutate silently.
        return owner is not null && await dialog.ShowDialog<bool>(owner);
    }
}
