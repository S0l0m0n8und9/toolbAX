using FoToolbox.Core.OData;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace QueryBuilderPlugin;

public abstract class FilterNodeViewModel
{
    public FilterGroupViewModel? Parent { get; set; }
    public abstract FilterNode ToAst();
}

public sealed class FilterConditionViewModel : FilterNodeViewModel
{
    public string Field { get; set; } = string.Empty;
    public string Operator { get; set; } = "eq";
    public string Value { get; set; } = string.Empty;

    public override FilterNode ToAst() => new FilterCondition(Field, Operator, FormatValue());

    private string FormatValue()
    {
        var raw = Value ?? string.Empty;
        var sanitized = raw.Replace("'", "''");
        if (Operator is "startswith" or "endswith" or "contains")
        {
            return $"'{sanitized}'";
        }
        if (raw.StartsWith("'") && raw.EndsWith("'"))
        {
            return raw;
        }
        return $"'{sanitized}'";
    }
}

public sealed class FilterGroupViewModel : FilterNodeViewModel
{
    public string LogicalOperator { get; set; } = "and";
    public ObservableCollection<FilterNodeViewModel> Children { get; } = new();

    public override FilterNode ToAst() => new FilterGroup(LogicalOperator, Children.Select(c => c.ToAst()).ToList());
}
