using System.Collections.Generic;
using System.Linq;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>Presents a <see cref="ConfirmRequest"/> in the confirm dialog (control-map §3).</summary>
public sealed class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(ConfirmRequest request)
    {
        Title = $"{request.Action.Label} {request.Targets.Count} map(s)?";
        Message = $"Sends action={request.Action.Code} to the Dual-Write gateway for {request.GatewayCName}.";
        Caveat = request.Action.Id switch
        {
            "stop" => "This halts replication for the selected maps.",
            "initial" => "This re-syncs all data and can run for a long time.",
            _ => null,
        };
        Targets = request.Targets
            .Select(t => $"{t.FoEntity} {Arrow(t.Direction)} {t.DvEntity}  ·  {t.State}")
            .ToList();
        ConfirmLabel = request.Action.Label;
        IsDanger = request.Action.Danger;
    }

    public string Title { get; }
    public string Message { get; }
    public string? Caveat { get; }
    public bool HasCaveat => Caveat is not null;
    public IReadOnlyList<string> Targets { get; }
    public string ConfirmLabel { get; }
    public bool IsDanger { get; }

    private static string Arrow(DwDirection direction) => direction switch
    {
        DwDirection.Both => "↔",
        DwDirection.FoToDv => "→",
        _ => "←",
    };
}
