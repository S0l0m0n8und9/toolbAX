using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace QueryBuilderPlugin;

public sealed class EntityItem
{
    public EntityItem(string name, int propertyCount, int navigationCount)
    {
        Name = name;
        PropertyCount = propertyCount;
        NavigationCount = navigationCount;
    }

    public string Name { get; }
    public int PropertyCount { get; }
    public int NavigationCount { get; }
    public int TotalCount => PropertyCount + NavigationCount;
    public string Summary => $"{PropertyCount} fields, {NavigationCount} nav";
}

public sealed class FieldItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public FieldItem(
        string name,
        string type,
        string kind,
        bool nullable,
        string? enumValues,
        bool isKey = false,
        bool isMandatory = false,
        string? maxLength = null,
        string? precision = null,
        string? scale = null,
        string? minValue = null,
        string? maxValue = null)
    {
        Name = name;
        Type = type;
        Kind = kind;
        Nullable = nullable;
        EnumValues = enumValues;
        IsKey = isKey;
        IsMandatory = isMandatory;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    public string Name { get; }
    public string Type { get; }
    public string Kind { get; }
    public bool Nullable { get; }
    public bool IsKey { get; }
    public bool IsMandatory { get; }
    public bool Mandatory => IsKey || IsMandatory;
    public string? EnumValues { get; }
    public string? MaxLength { get; }
    public string? Precision { get; }
    public string? Scale { get; }
    public string? MinValue { get; }
    public string? MaxValue { get; }
    public string? PrecisionScale => string.IsNullOrWhiteSpace(Precision) && string.IsNullOrWhiteSpace(Scale)
        ? null
        : $"{(string.IsNullOrWhiteSpace(Precision) ? "-" : Precision)}/{(string.IsNullOrWhiteSpace(Scale) ? "-" : Scale)}";
    public string? Range => string.IsNullOrWhiteSpace(MinValue) && string.IsNullOrWhiteSpace(MaxValue)
        ? null
        : $"{(string.IsNullOrWhiteSpace(MinValue) ? "-" : MinValue)} .. {(string.IsNullOrWhiteSpace(MaxValue) ? "-" : MaxValue)}";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public event EventHandler? SelectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
