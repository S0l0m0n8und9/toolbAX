using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using ToolBax.App.Models;
using ToolBax.App.ViewModels;

namespace ToolBax.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Double-clicking a palette result navigates to it.
    private void OnPaletteResultActivated(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: NavTool tool })
        {
            Invoke(tool);
        }
    }

    // Enter in the palette search box runs the highlighted result, falling back to the top result.
    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ShellViewModel shell)
        {
            var target = PaletteResults.SelectedItem as NavTool
                         ?? shell.Palette.FilteredCommands.FirstOrDefault();
            if (target is not null)
            {
                Invoke(target);
                e.Handled = true;
            }
        }
    }

    private void Invoke(NavTool tool)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.Palette.InvokeCommand.Execute(tool);
        }
    }
}
