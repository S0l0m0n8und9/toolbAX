using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace ODataPostBuilderPlugin;

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
    public string Display => $"{Name} ({PropertyCount} fields, {NavigationCount} nav)";
}

public enum PostFieldEditorKind
{
    Text,
    Boolean,
    Enum
}

public sealed class PostFieldItem : INotifyPropertyChanged
{
    private bool _include;
    private string? _textValue;
    private bool? _boolValue;
    private string? _enumValue;

    public PostFieldItem(string name, string type, bool nullable, bool mandatory, PostFieldEditorKind editorKind, ObservableCollection<string>? enumMembers)
    {
        Name = name;
        Type = type;
        Nullable = nullable;
        Mandatory = mandatory;
        EditorKind = editorKind;
        EnumMembers = enumMembers ?? new ObservableCollection<string>();
    }

    public string Name { get; }
    public string Type { get; }
    public bool Nullable { get; }
    public bool Mandatory { get; }
    public string MandatoryText => Mandatory ? "Yes" : string.Empty;
    public PostFieldEditorKind EditorKind { get; }
    public ObservableCollection<string> EnumMembers { get; }

    public bool Include
    {
        get => _include;
        set { if (_include != value) { _include = value; OnPropertyChanged(); } }
    }

    public string? TextValue
    {
        get => _textValue;
        set { if (_textValue != value) { _textValue = value; OnPropertyChanged(); } }
    }

    public bool? BoolValue
    {
        get => _boolValue;
        set { if (_boolValue != value) { _boolValue = value; OnPropertyChanged(); } }
    }

    public string? EnumValue
    {
        get => _enumValue;
        set { if (_enumValue != value) { _enumValue = value; OnPropertyChanged(); } }
    }

    public string GetEffectiveValueText()
    {
        return EditorKind switch
        {
            PostFieldEditorKind.Boolean => BoolValue is null ? string.Empty : (BoolValue.Value ? "true" : "false"),
            PostFieldEditorKind.Enum => EnumValue ?? string.Empty,
            _ => TextValue ?? string.Empty
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BatchOperationItem : INotifyPropertyChanged
{
    private string _method = "POST";
    private string _url = string.Empty;
    private string? _bodyJson;

    public string Method
    {
        get => _method;
        set { if (_method != value) { _method = value; OnPropertyChanged(); } }
    }

    public string Url
    {
        get => _url;
        set { if (_url != value) { _url = value; OnPropertyChanged(); } }
    }

    public string? BodyJson
    {
        get => _bodyJson;
        set { if (_bodyJson != value) { _bodyJson = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

