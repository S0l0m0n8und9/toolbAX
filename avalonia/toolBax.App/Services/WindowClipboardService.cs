using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// <see cref="IClipboardService"/> backed by a window's clipboard (the production implementation).
/// No-ops if the platform exposes no clipboard (e.g. a headless host).
/// </summary>
public sealed class WindowClipboardService : IClipboardService
{
    private readonly TopLevel _topLevel;

    public WindowClipboardService(TopLevel topLevel) => _topLevel = topLevel;

    public Task SetTextAsync(string text) => _topLevel.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
