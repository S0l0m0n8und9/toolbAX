using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolBax.App.ViewModels;

/// <summary>How a field's value is edited in the POST Builder grid.</summary>
public enum PostFieldEditor
{
    /// <summary>Free-text box (the default for strings, numbers, dates, GUIDs).</summary>
    Text,
    /// <summary>Three-state checkbox (Boolean fields).</summary>
    Bool,
    /// <summary>Dropdown of the enum's members.</summary>
    Enum,
}

/// <summary>
/// One row of the POST Builder's metadata-driven field grid: an entity property the user can include
/// in the request body and give a value. <see cref="Include"/> and <see cref="Value"/> are editable;
/// the rest describe the property. <see cref="Editor"/> + <see cref="EnumMembers"/> drive which inline
/// editor the Value cell shows (text / checkbox / dropdown). The owning view-model rebuilds the JSON
/// payload whenever an editable field changes.
/// </summary>
public partial class PostFieldRow : ObservableObject
{
    public string Name { get; }
    public string Type { get; }
    public bool Mandatory { get; }
    public bool IsKey { get; }
    public PostFieldEditor Editor { get; }
    public IReadOnlyList<string> EnumMembers { get; }

    public bool IsText => Editor == PostFieldEditor.Text;
    public bool IsBool => Editor == PostFieldEditor.Bool;
    public bool IsEnum => Editor == PostFieldEditor.Enum;

    /// <summary>Include this field in the generated payload.</summary>
    [ObservableProperty]
    private bool _include;

    /// <summary>The (string) value the user typed; coerced to the property's type when the payload builds.</summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>The Boolean view over <see cref="Value"/> for the checkbox editor: "true"/"false"/blank.</summary>
    public bool? BoolValue
    {
        get => Value switch
        {
            "true" => true,
            "false" => false,
            _ => null,
        };
        set
        {
            var text = value switch
            {
                true => "true",
                false => "false",
                _ => string.Empty,
            };
            if (!string.Equals(text, Value, StringComparison.Ordinal))
            {
                Value = text;
            }
        }
    }

    public PostFieldRow(string name, string type, bool mandatory, bool isKey, bool include,
        PostFieldEditor editor = PostFieldEditor.Text, IReadOnlyList<string>? enumMembers = null)
    {
        Name = name;
        Type = type;
        Mandatory = mandatory;
        IsKey = isKey;
        Editor = editor;
        EnumMembers = enumMembers ?? Array.Empty<string>();
        _include = include;
    }

    // Keep the checkbox in sync when the value is set programmatically (e.g. a payload rebuild).
    partial void OnValueChanged(string value) => OnPropertyChanged(nameof(BoolValue));
}
