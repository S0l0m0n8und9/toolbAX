using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// Writes text to the system clipboard. Behind an interface because the real implementation needs a
/// TopLevel (Avalonia), keeping view models platform-neutral and headless-testable.
/// </summary>
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
