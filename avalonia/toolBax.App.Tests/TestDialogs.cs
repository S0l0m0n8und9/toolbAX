using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Tests;

/// <summary>An <see cref="IDialogService"/> that never confirms — for view/routing tests that should
/// not trigger a mutation.</summary>
internal sealed class StubDialogs : IDialogService
{
    public Task<bool> ConfirmAsync(ConfirmRequest request) => Task.FromResult(false);
}
