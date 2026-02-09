using FoToolbox.Core.OData;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

public sealed record EnumFieldInfo(string TypeName, IReadOnlyList<string> Members);

public sealed class FilterConditionViewModel : FilterNodeViewModel
{
    private string _field = string.Empty;
    private string _operator = "eq";
    private string _value = string.Empty;
    private readonly ObservableCollection<string> _enumValues = new();
    private Func<string, EnumFieldInfo?>? _enumProvider;
    private string? _enumTypeName;

    public string Field
    {
        get => _field;
        set
        {
            if (_field != value)
            {
                _field = value;
                OnPropertyChanged();
                RefreshEnumValues();
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

    public ObservableCollection<string> EnumValues => _enumValues;

    public bool HasEnumValues => _enumValues.Count > 0;

    public string? EnumTypeName
    {
        get => _enumTypeName;
        private set
        {
            if (_enumTypeName != value)
            {
                _enumTypeName = value;
                OnPropertyChanged();
            }
        }
    }

    public void ConfigureEnumProvider(Func<string, EnumFieldInfo?> provider)
    {
        _enumProvider = provider;
        RefreshEnumValues();
    }

    public override FilterNode ToAst() => new FilterCondition(Field, Operator, FormatValue());

    private void RefreshEnumValues()
    {
        _enumValues.Clear();
        EnumTypeName = null;
        if (_enumProvider is null || string.IsNullOrWhiteSpace(Field))
        {
            OnPropertyChanged(nameof(HasEnumValues));
            return;
        }

        var info = _enumProvider(Field);
        if (info is null || info.Members.Count == 0)
        {
            OnPropertyChanged(nameof(HasEnumValues));
            return;
        }

        EnumTypeName = info.TypeName;
        foreach (var member in info.Members)
        {
            _enumValues.Add(member);
        }
        OnPropertyChanged(nameof(HasEnumValues));
    }

    private string FormatValue()
    {
        var raw = Value ?? string.Empty;
        var trimmed = raw.Trim();
        var sanitized = trimmed.Replace("'", "''");
        if (Operator is "startswith" or "endswith" or "contains")
        {
            return $"'{sanitized}'";
        }
        if (trimmed.StartsWith("'") && trimmed.EndsWith("'"))
        {
            return trimmed;
        }
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase))
        {
            return "null";
        }
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "true";
        }
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "false";
        }
        if (decimal.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
        {
            return trimmed;
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
