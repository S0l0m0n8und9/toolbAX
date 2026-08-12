using System.Collections.Generic;

namespace ToolBax.App.ViewModels;

/// <summary>
/// One projected result row in the Query Builder grid. Columns are dynamic (= the selected fields),
/// so the grid binds cells via a string indexer (<c>[FieldName]</c>) rather than fixed properties.
/// </summary>
/// <remarks>
/// Cells hold the raw value — <c>null</c> for a null or absent field — and the em-dash placeholder is
/// derived at the display point (<see cref="this[string]"/>). Nullness is therefore structural: it is
/// never inferred by comparing a cell against the placeholder, so a genuine <c>—</c> value stays data
/// and exports as <c>—</c> rather than silently becoming an empty field (PR #193 review).
/// </remarks>
public sealed class QueryResultRow
{
    /// <summary>What the grid shows for a null or absent cell. Display-only — never exported.</summary>
    public const string NullDisplay = "—";

    private readonly IReadOnlyDictionary<string, string?> _cells;

    public QueryResultRow(IReadOnlyDictionary<string, string?> cells) => _cells = cells;

    /// <summary>
    /// Display text for a column: the raw value, or <see cref="NullDisplay"/> when it is null or absent.
    /// This is what the result grid's generated columns bind to.
    /// </summary>
    public string this[string column] => Raw(column) ?? NullDisplay;

    /// <summary>
    /// The column's raw value, or <c>null</c> when the field was null or absent in the payload. Exporters
    /// read this (see <see cref="QueryCsv"/>) so a null becomes an empty CSV field on its own evidence.
    /// </summary>
    public string? Raw(string column) => _cells.TryGetValue(column, out var value) ? value : null;
}
