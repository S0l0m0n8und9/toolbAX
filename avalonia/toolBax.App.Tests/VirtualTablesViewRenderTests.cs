using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Headless render smoke for Virtual tables — the one tool screen that had view-model tests but no render
/// test (#167), so nothing caught a binding that no longer resolves or a state panel wired to the wrong
/// flag. Shows the view, lets Loaded drive InitializeCommand, and asserts on the rendered controls.
/// A collapsed control stays in the visual tree, so every state assertion checks <c>IsVisible</c> rather
/// than mere presence.
/// </summary>
public class VirtualTablesViewRenderTests
{
    private sealed class ErrorReader : IVirtualTableReader
    {
        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
            => Task.FromResult(VirtualTableLoadResult.Fail("Dataverse rejected the metadata query (403)."));
    }

    private sealed class EmptyReader : IVirtualTableReader
    {
        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
            => Task.FromResult(VirtualTableLoadResult.Ok(Array.Empty<VirtualTableInfo>()));
    }

    private static EnvProfile Env() =>
        new("env1", "contoso", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: "https://contoso.crm.dynamics.com");

    private static (VirtualTablesView view, Window window) Show(IVirtualTableReader reader)
    {
        var view = new VirtualTablesView { DataContext = new VirtualTablesViewModel(reader, activeEnv: Env) };
        var window = new Window { Content = view, Width = 1100, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();   // fires Loaded → InitializeCommand (loads the catalogue)
        return (view, window);
    }

    private static VirtualTablesViewModel Vm(VirtualTablesView view) => (VirtualTablesViewModel)view.DataContext!;

    private static DataGrid Grid(VirtualTablesView view) =>
        view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "TablesGrid");

    /// <summary>The one TextBlock whose text starts with <paramref name="prefix"/>; fails if absent.</summary>
    private static TextBlock Block(VirtualTablesView view, string prefix)
    {
        var block = view.GetVisualDescendants().OfType<TextBlock>()
            .FirstOrDefault(t => t.Text?.StartsWith(prefix, StringComparison.Ordinal) == true);
        Assert.NotNull(block);
        return block!;
    }

    /// <summary>The "+N non-F&amp;O virtual table(s) not shown" hint, whatever N currently is.</summary>
    private static TextBlock OtherCountHint(VirtualTablesView view) =>
        view.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.Text?.EndsWith("not shown", StringComparison.Ordinal) == true);

    [AvaloniaFact]
    public void Renders_the_table_grid_the_search_box_and_the_action_buttons()
    {
        var (view, window) = Show(new FakeVirtualTableReader());
        try
        {
            var grid = Grid(view);
            Assert.True(grid.IsVisible);
            Assert.Same(Vm(view).Filtered, grid.ItemsSource);
            Assert.NotEmpty(grid.GetVisualDescendants().OfType<DataGridRow>());

            Assert.NotNull(view.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.PlaceholderText?.StartsWith("Search", StringComparison.Ordinal) == true));

            var buttons = view.GetVisualDescendants().OfType<Button>().Select(b => b.Content as string).ToList();
            Assert.Contains("Refresh", buttons);
            Assert.Contains("Open in Dataverse", buttons);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_loaded_environment_label_names_the_environment_the_rows_came_from()
    {
        // The shell can switch the active environment while this screen stays open, so the grid has to say
        // which environment it is showing — a collapsed or unformatted label is the bug this pins.
        var (view, window) = Show(new FakeVirtualTableReader());
        try
        {
            var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "LoadedEnvLabel");
            Assert.True(label.IsVisible);
            Assert.Equal("Loaded from contoso", label.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void The_non_fo_count_hint_shows_only_when_something_was_filtered_out()
    {
        // The catalogue has one non-F&O virtual table, so the hint is on and counts it…
        var (view, window) = Show(new FakeVirtualTableReader());
        try
        {
            var hint = OtherCountHint(view);
            Assert.True(hint.IsVisible);
            Assert.StartsWith("+1 ", hint.Text);
        }
        finally
        {
            window.Close();
        }

        // …and collapsed when the environment has nothing that was filtered out.
        var (emptyView, emptyWindow) = Show(new EmptyReader());
        try
        {
            Assert.False(OtherCountHint(emptyView).IsVisible);
        }
        finally
        {
            emptyWindow.Close();
        }
    }

    [AvaloniaFact]
    public void A_reader_error_renders_the_banner_and_no_grid_or_empty_state()
    {
        var (view, window) = Show(new ErrorReader());
        try
        {
            Assert.True(Block(view, "Dataverse rejected the metadata query").IsVisible);
            Assert.False(Grid(view).IsVisible);                       // no grid over an error
            Assert.False(Block(view, "No finance & operations").IsVisible);   // nor "this env has none"
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void An_environment_with_no_fo_tables_renders_the_empty_state()
    {
        var (view, window) = Show(new EmptyReader());
        try
        {
            Assert.True(Block(view, "No finance & operations").IsVisible);
            Assert.False(Grid(view).IsVisible);
            Assert.False(Block(view, "No tables match your search.").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_search_matching_nothing_empties_the_grid_and_shows_the_no_matches_hint()
    {
        var (view, window) = Show(new FakeVirtualTableReader());
        try
        {
            Vm(view).Search = "zzz-no-such-table";
            Dispatcher.UIThread.RunJobs();

            Assert.True(Block(view, "No tables match your search.").IsVisible);
            // Tables did load, so the grid stays up — it just has nothing to show under the filter.
            Assert.Empty(Grid(view).GetVisualDescendants().OfType<DataGridRow>());
            Assert.False(Block(view, "No finance & operations").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
