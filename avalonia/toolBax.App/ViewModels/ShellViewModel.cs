using CommunityToolkit.Mvvm.ComponentModel;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Root shell view model. Phase 1 carries only the app identity; the nav rail, tool selection,
/// command palette and status strip land in the shell PR (control-map §0).
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "toolBax";

    [ObservableProperty]
    private string _subtitle = "FO Toolbox 2.0";
}
