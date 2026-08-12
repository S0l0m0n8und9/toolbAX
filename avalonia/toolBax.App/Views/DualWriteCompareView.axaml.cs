using Avalonia.Controls;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class DualWriteCompareView : UserControl
{
    public DualWriteCompareView()
    {
        InitializeComponent();
        // Re-read the profile store into the pickers every time this view appears. The shell caches the VM
        // and EnvProfile is an immutable record replaced on save, so a construction-time snapshot would go
        // stale the moment a profile is edited, added, or deleted in Profiles.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        (DataContext as DualWriteCompareViewModel)?.RefreshEnvironmentsCommand.Execute(null);
}
