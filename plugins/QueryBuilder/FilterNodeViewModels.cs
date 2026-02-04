using FoToolbox.Core.OData;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace QueryBuilderPlugin;

public abstract class FilterNodeViewModel : INotifyPropertyChanged
{
    public FilterGroupViewModel? Parent { get; set; }
    public abstract FilterNode ToAst();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class FilterConditionViewModel : FilterNodeViewModel
{
    private string _field = string.Empty;
    private string _operator = "eq";
    private string _value = string.Empty;

    public string Field
    {
        get => _field;
        set
        {
            if (_field != value)
            {
                _field = value;
                OnPropertyChanged();
            }
        }
    }

    public string Operator
    {
        get => _operator;
        set
        {
            if (_operator != value)
            {
                _operator = value;
                OnPropertyChanged();
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (_value != value)
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

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
    private string _logicalOperator = "and";

    public string LogicalOperator
    {
        get => _logicalOperator;
        set
        {
            if (_logicalOperator != value)
            {
                _logicalOperator = value;
                OnPropertyChanged();
            }
        }
    }
    public ObservableCollection<FilterNodeViewModel> Children { get; } = new();

    public override FilterNode ToAst() => new FilterGroup(LogicalOperator, Children.Select(c => c.ToAst()).ToList());
}
