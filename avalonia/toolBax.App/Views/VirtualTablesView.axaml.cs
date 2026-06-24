using Avalonia.Controls;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class VirtualTablesView : UserControl
{
    public VirtualTablesView()
    {
        InitializeComponent();
        // Load the virtual-table catalogue when the view is first shown; the VM caches it.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        (DataContext as VirtualTablesViewModel)?.InitializeCommand.Execute(null);
}
