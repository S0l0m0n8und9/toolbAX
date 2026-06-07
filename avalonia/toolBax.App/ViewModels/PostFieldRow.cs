using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolBax.App.ViewModels;

/// <summary>
/// One row of the POST Builder's metadata-driven field grid: an entity property the user can include
/// in the request body and give a value. <see cref="Include"/> and <see cref="Value"/> are editable;
/// the rest describe the property. The owning view-model rebuilds the JSON payload whenever an
/// editable field changes.
/// </summary>
public partial class PostFieldRow : ObservableObject
{
    public string Name { get; }
    public string Type { get; }
    public bool Mandatory { get; }
    public bool IsKey { get; }

    /// <summary>Include this field in the generated payload.</summary>
    [ObservableProperty]
    private bool _include;

    /// <summary>The (string) value the user typed; coerced to the property's type when the payload builds.</summary>
    [ObservableProperty]
    private string _value = string.Empty;

    public PostFieldRow(string name, string type, bool mandatory, bool isKey, bool include)
    {
        Name = name;
        Type = type;
        Mandatory = mandatory;
        IsKey = isKey;
        _include = include;
    }
}
