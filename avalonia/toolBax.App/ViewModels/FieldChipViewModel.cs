using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolBax.App.ViewModels;

/// <summary>A selectable $select field chip in the Query Builder (control-map §2).</summary>
public partial class FieldChipViewModel : ObservableObject
{
    public string Name { get; }
    public bool IsKey { get; }

    /// <summary>Required (non-nullable, non-key) — surfaces a "REQ" marker in the field list.</summary>
    public bool IsMandatory { get; }

    /// <summary>Human type, e.g. "Enum&lt;NoYes&gt;", "String(20)", "Decimal" — shown under the name.</summary>
    public string TypeDisplay { get; }

    [ObservableProperty]
    private bool _isSelected;

    public FieldChipViewModel(string name, bool isKey, bool isSelected,
        bool isMandatory = false, string typeDisplay = "")
    {
        Name = name;
        IsKey = isKey;
        IsMandatory = isMandatory;
        TypeDisplay = typeDisplay;
        _isSelected = isSelected;
    }

    /// <summary>Show the "REQ" marker only for required non-key fields (keys get the "PK" marker).</summary>
    public bool ShowReq => IsMandatory && !IsKey;

    /// <summary>Whether there's a type string to display under the field name.</summary>
    public bool HasTypeDisplay => !string.IsNullOrEmpty(TypeDisplay);
}
