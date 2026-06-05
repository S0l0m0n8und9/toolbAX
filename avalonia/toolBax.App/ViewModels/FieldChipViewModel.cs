using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolBax.App.ViewModels;

/// <summary>A selectable $select field chip in the Query Builder (control-map §2).</summary>
public partial class FieldChipViewModel : ObservableObject
{
    public string Name { get; }
    public bool IsKey { get; }

    [ObservableProperty]
    private bool _isSelected;

    public FieldChipViewModel(string name, bool isKey, bool isSelected)
    {
        Name = name;
        IsKey = isKey;
        _isSelected = isSelected;
    }
}
