using System.Collections.Generic;
using System.Threading.Tasks;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>A target map shown in the confirm dialog (name + current state).</summary>
public sealed record ConfirmTarget(string FoEntity, string DvEntity, DwDirection Direction, MapState State);

/// <summary>Request to confirm a mutating action before any gateway call (confirm-on-mutation).</summary>
public sealed record ConfirmRequest(
    DwAction Action,
    string GatewayCName,
    IReadOnlyList<ConfirmTarget> Targets);

/// <summary>
/// Abstracts the confirm dialog so ViewModels stay headless-testable. The real implementation shows a
/// Fluent ContentDialog; tests use a fake that returns a fixed decision.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(ConfirmRequest request);
}
