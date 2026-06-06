using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>In-memory <see cref="IClipboardService"/> for design-mode + tests; records the last text.</summary>
public sealed class FakeClipboardService : IClipboardService
{
    public string? LastText { get; private set; }

    public Task SetTextAsync(string text)
    {
        LastText = text;
        return Task.CompletedTask;
    }
}
