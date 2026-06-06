using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// Prompts the user for a save location and writes text to it. Behind an interface because the real
/// implementation needs a TopLevel (Avalonia file picker), keeping view models platform-neutral and
/// headless-testable.
/// </summary>
public interface IFileSaveService
{
    /// <summary>
    /// Shows a save dialog seeded with <paramref name="suggestedFileName"/> and writes
    /// <paramref name="content"/> (UTF-8). Returns the saved path, or <c>null</c> if the user cancels.
    /// </summary>
    Task<string?> SaveTextAsync(string suggestedFileName, string content, CancellationToken ct = default);
}
