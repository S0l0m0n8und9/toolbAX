using Avalonia.Controls;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class DualWriteMapView : UserControl
{
    public DualWriteMapView()
    {
        InitializeComponent();
        // Load the dual-write map catalogue from Dataverse when the view first appears; the cached VM
        // only reloads on explicit Refresh, so re-navigating is cheap.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        (DataContext as DualWriteMapViewModel)?.InitializeCommand.Execute(null);
}
