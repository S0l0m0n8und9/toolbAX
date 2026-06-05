using System.Collections.Generic;

namespace ToolBax.App.ViewModels;

/// <summary>
/// One projected result row in the Query Builder grid. Columns are dynamic (= the selected fields),
/// so the grid binds cells via a string indexer (<c>[FieldName]</c>) rather than fixed properties.
/// </summary>
public sealed class QueryResultRow
{
    private readonly IReadOnlyDictionary<string, string> _cells;

    public QueryResultRow(IReadOnlyDictionary<string, string> cells) => _cells = cells;

    /// <summary>Cell value for a column, or an em-dash when the field is absent/null.</summary>
    public string this[string column] => _cells.TryGetValue(column, out var value) ? value : "—";
}
