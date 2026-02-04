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

    public FieldItem(string name, string type, string kind, bool nullable)
    {
        Name = name;
        Type = type;
        Kind = kind;
        Nullable = nullable;
    }

    public string Name { get; }
    public string Type { get; }
    public string Kind { get; }
    public bool Nullable { get; }

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
