using Avalonia.Controls;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class MetadataView : UserControl
{
    public MetadataView()
    {
        InitializeComponent();
        // Kick off the live $metadata fetch when the view is shown; the cached VM only refetches if
        // the catalogue actually changes, so re-navigating is cheap.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        (DataContext as MetadataViewModel)?.InitializeCommand.Execute(null);
}
