using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using ToolBax.App.Models;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;

namespace ToolBax.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    // Header environment switcher: its SelectedItem is bound OneWay, so the view model stays the single
    // source of truth and a user pick has to be handed to the deliberate-switch funnel by hand (a TwoWay
    // binding would move ActiveEnvironment without persisting the choice or offering the tool refresh).
    // Things other than the user move the selection too — the first binding push, the funnel's rollback
    // when the profile store rejects the active-id write, and a Profiles rename/delete reshaping
    // Environments — and none of them may re-enter the funnel, or a rejected switch would retry itself on
    // every rollback and a rename/deletion would raise the "refresh open tools?" prompt they deliberately
    // skip. The two guards below reject all of those; ShellRenderTests pins each case.
    private void OnEnvironmentSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not ShellViewModel shell || sender is not ComboBox box)
        {
            return;
        }

        // Selection cleared: a list mutation (rename replaces the record, delete removes it) dropped
        // whatever was selected, so there is nothing to switch to. The shell finishes the job by assigning
        // ActiveEnvironment, which re-syncs the box through the binding.
        if (box.SelectedItem is not EnvProfile picked)
        {
            return;
        }

        // The view model pushed this value in (first bind, Profiles' "Set active", or a rollback): it has
        // already settled on this environment, so echoing it back would re-fire the switch.
        if (ReferenceEquals(picked, shell.ActiveEnvironment))
        {
            return;
        }

        shell.SetActiveEnvironmentCommand.Execute(picked);
    }

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
