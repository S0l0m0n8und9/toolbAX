using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// Request to confirm a mutating action before any gateway call (confirm-on-mutation). Generic so any
/// screen can use it: <see cref="Targets"/> are pre-formatted lines (e.g. the affected maps) and
/// <see cref="Caveat"/> carries an optional danger note.
/// </summary>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    IReadOnlyList<string> Targets,
    string ConfirmLabel,
    bool IsDanger,
    string? Caveat = null);

/// <summary>
/// Abstracts the confirm dialog so ViewModels stay headless-testable. The real implementation shows a
/// Fluent ContentDialog; tests use a fake that returns a fixed decision.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(ConfirmRequest request);

    /// <summary>
    /// Confirms with caller cancellation. Legacy/headless providers inherit a cancellable await around the
    /// one-argument contract; UI providers must override this overload to dismiss their actual dialog.
    /// </summary>
    async Task<bool> ConfirmAsync(ConfirmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await ConfirmAsync(request).WaitAsync(ct).ConfigureAwait(false);
    }
}
