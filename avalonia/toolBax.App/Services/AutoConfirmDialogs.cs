using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// An <see cref="IDialogService"/> that always confirms — the default when a view-model is constructed
/// without a real dialog service (design-mode / headless tests that aren't exercising the confirm gate).
/// The composition root injects the real <see cref="DialogService"/> so live mutations are still gated.
/// </summary>
public sealed class AutoConfirmDialogs : IDialogService
{
    public Task<bool> ConfirmAsync(ConfirmRequest request) => Task.FromResult(true);
}
