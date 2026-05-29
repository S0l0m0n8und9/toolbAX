using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Selects which template version to apply to a map. Mirrors the MS tool's
/// "latest by author" logic (<c>DWMapEngine.getLatestTemplate</c>): optionally filter the
/// map's available templates by author, then pick the highest version. Pure and testable.
/// </summary>
public static class TemplateSelector
{
    /// <summary>Author token meaning "any author" — disables author filtering.</summary>
    public const string AnyAuthor = "ANY";

    public static DualWriteTemplate? SelectLatest(
        IReadOnlyList<DualWriteTemplate> templates,
        IReadOnlyCollection<string>? authors = null)
    {
        if (templates is null || templates.Count == 0)
        {
            return null;
        }

        IEnumerable<DualWriteTemplate> candidates = templates;
        if (authors is not null &&
            authors.Count > 0 &&
            !authors.Any(a => string.Equals(a, AnyAuthor, StringComparison.OrdinalIgnoreCase)))
        {
            candidates = templates.Where(t =>
                authors.Any(a => string.Equals(a, t.Author, StringComparison.OrdinalIgnoreCase)));
        }

        return candidates
            .OrderByDescending(t => Version.TryParse(t.Version, out var v) ? v : new Version(0, 0, 0, 0))
            .ThenByDescending(t => t.Version, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>Parses a comma/semicolon-separated author filter into tokens (empty = any).</summary>
    public static IReadOnlyCollection<string> ParseAuthorFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }
}
