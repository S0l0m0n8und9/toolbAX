using Avalonia.Controls;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class PostBuilderView : UserControl
{
    public PostBuilderView()
    {
        InitializeComponent();
        // Kick off the live $metadata fetch when the view is shown, so opening the POST Builder first in a
        // session still gets a populated entity picker. The cached VM only rebuilds the catalogue when it
        // actually changed, so re-navigating is cheap — and it refreshes the list after an environment switch.
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e) =>
        (DataContext as PostBuilderViewModel)?.InitializeCommand.Execute(null);
}
