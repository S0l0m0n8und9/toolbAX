using System.Collections.Generic;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>Presents a <see cref="ConfirmRequest"/> in the confirm dialog (control-map §3). A thin
/// projection — the request already carries the formatted title/message/targets/caveat.</summary>
public sealed class ConfirmDialogViewModel
{
    public ConfirmDialogViewModel(ConfirmRequest request)
    {
        Title = request.Title;
        Message = request.Message;
        Caveat = request.Caveat;
        Targets = request.Targets;
        ConfirmLabel = request.ConfirmLabel;
        IsDanger = request.IsDanger;
    }

    public string Title { get; }
    public string Message { get; }
    public string? Caveat { get; }
    public bool HasCaveat => Caveat is not null;
    public IReadOnlyList<string> Targets { get; }
    public string ConfirmLabel { get; }
    public bool IsDanger { get; }
}
