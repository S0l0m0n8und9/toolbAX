using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>
/// The kind of file a save dialog should offer: the extension it defaults to, and the human-readable
/// name shown in its file-type dropdown.
/// </summary>
/// <remarks>
/// One <see cref="IFileSaveService"/> serves several exporters — the Map Browser writes Markdown, the
/// Query Builder writes CSV — so the type has to travel with the call. Baking it into the
/// implementation offered a CSV export nothing but <c>*.md</c> and saved it as
/// <c>CustomersV3.csv.md</c> (#168).
/// </remarks>
/// <param name="Extension">Default extension, without a leading dot (e.g. <c>csv</c>).</param>
/// <param name="DisplayName">Label for the picker's type entry (e.g. <c>CSV</c>).</param>
public sealed record SaveFileType(string Extension, string DisplayName)
{
    /// <summary>Markdown (<c>*.md</c>) — the Map Browser's export.</summary>
    public static SaveFileType Markdown { get; } = new("md", "Markdown");

    /// <summary>Comma-separated values (<c>*.csv</c>) — the Query Builder's exports.</summary>
    public static SaveFileType Csv { get; } = new("csv", "CSV");

    /// <summary>The picker glob for this type (e.g. <c>*.csv</c>).</summary>
    public string Pattern => $"*.{Extension}";
}

/// <summary>
/// Prompts the user for a save location and writes text to it. Behind an interface because the real
/// implementation needs a TopLevel (Avalonia file picker), keeping view models platform-neutral and
/// headless-testable.
/// </summary>
public interface IFileSaveService
{
    /// <summary>
    /// Shows a save dialog seeded with <paramref name="suggestedFileName"/> and offering
    /// <paramref name="fileType"/> as its file type, then writes <paramref name="content"/> (UTF-8).
    /// Returns the saved path, or <c>null</c> if the user cancels.
    /// </summary>
    Task<string?> SaveTextAsync(string suggestedFileName, string content, SaveFileType fileType,
        CancellationToken ct = default);
}
