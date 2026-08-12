using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Converters;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>Headless render smoke for Dual-Write Compare (control-map §5).</summary>
public class DualWriteCompareViewRenderTests
{
    [AvaloniaFact]
    public void Renders_two_pickers_and_a_compare_button()
    {
        var view = new DualWriteCompareView
        {
            DataContext = new DualWriteCompareViewModel(new FakeProfileStore(), new FakeDualWriteCompareService()),
        };
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(view.GetVisualDescendants().OfType<ComboBox>().Count() >= 2);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Compare");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Comparing_populates_the_diff_grid()
    {
        var vm = new DualWriteCompareViewModel(new FakeProfileStore(), new FakeDualWriteCompareService());
        var view = new DualWriteCompareView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.CompareCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var grid = view.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "DiffGrid");
            Assert.Same(vm.DiffRows, grid.ItemsSource);
            Assert.NotEmpty(vm.DiffRows);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The Diff column and the summary chips bind the raw verdict through these converters, so a verdict
    /// member with no mapping renders as the bare enum name in the grey fallback brush. #160 added
    /// <see cref="DualWriteComparisonVerdict.Ambiguous"/>; this covers every member, including future ones.
    /// </summary>
    [AvaloniaFact]
    public void Every_verdict_renders_a_friendly_label_and_a_themed_brush()
    {
        foreach (var verdict in Enum.GetValues<DualWriteComparisonVerdict>())
        {
            var label = VerdictToLabelConverter.Instance.Convert(
                verdict, typeof(string), null, CultureInfo.InvariantCulture) as string;
            Assert.False(string.IsNullOrWhiteSpace(label));
            Assert.NotEqual(verdict.ToString(), label);

            Assert.NotSame(Brushes.Gray, Brush(verdict));   // the "no such resource" fallback
        }

        // Unpairable rows are colour-coded as an error (ErrBrush, as OnlyInLeft is), not left on the
        // neutral Text2Brush default branch.
        Assert.Equal(Colour(DualWriteComparisonVerdict.OnlyInLeft), Colour(DualWriteComparisonVerdict.Ambiguous));
        Assert.NotEqual(Colour(DualWriteComparisonVerdict.Identical), Colour(DualWriteComparisonVerdict.Ambiguous));
    }

    /// <summary>
    /// #178 gave an unpairable row a <see cref="DualWriteMapComparisonRow.Note"/> saying which unpairable
    /// case it is, but the Diff column showed only "cannot compare" — the reason was computed, tested, and
    /// then dropped on the floor. It has to reach the rendered row.
    /// </summary>
    [AvaloniaFact]
    public void An_unpairable_row_carries_its_reason_and_an_ordinary_row_carries_none()
    {
        var vm = new DualWriteCompareViewModel(new FakeProfileStore(), new AmbiguityCompareService());
        var view = new DualWriteCompareView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            vm.CompareCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            var (ambiguous, note) = Row(view, DualWriteComparisonVerdict.Ambiguous);
            Assert.NotEmpty(ambiguous.Note);                 // the fixture is only meaningful if it has one
            Assert.True(note.IsVisible);
            Assert.Equal(ambiguous.Note, ToolTip.GetTip(note) as string);

            // An ordinary row has no reason to show, so the note host stays collapsed: no empty tooltip.
            var (identical, none) = Row(view, DualWriteComparisonVerdict.Identical);
            Assert.Empty(identical.Note);
            Assert.False(none.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>The rendered row for a verdict, plus the note host inside its Diff cell.</summary>
    private static (DualWriteMapComparisonRow Data, TextBlock NoteHost) Row(
        DualWriteCompareView view, DualWriteComparisonVerdict verdict)
    {
        var row = view.GetVisualDescendants().OfType<DataGridRow>()
            .First(r => (r.DataContext as DualWriteMapComparisonRow)?.Verdict == verdict);

        // Found by carrying a tip rather than by its glyph: the note host is the only tooltip in the row.
        var host = Assert.Single(
            row.GetVisualDescendants().OfType<TextBlock>(), t => ToolTip.GetTip(t) is not null);

        return ((DualWriteMapComparisonRow)row.DataContext!, host);
    }

    private static object Brush(DualWriteComparisonVerdict verdict) =>
        VerdictToBrushConverter.Instance.Convert(verdict, typeof(IBrush), null, CultureInfo.InvariantCulture);

    private static Color Colour(DualWriteComparisonVerdict verdict) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Brush(verdict)).Color;

    /// <summary>
    /// Rows from the real <see cref="DualWriteMapComparer"/>, so the note the view renders is the note Core
    /// actually produces: two same-identity maps in source against one in target is unpairable (a Note-bearing
    /// Ambiguous row per map), while a second map lines up cleanly (an ordinary row, no note).
    /// </summary>
    private sealed class AmbiguityCompareService : IDualWriteCompareService
    {
        public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(
            EnvProfile source, EnvProfile target, CancellationToken ct = default)
        {
            var left = new[]
            {
                Map("Customers V3", "accounts", "1.0.0.12"),
                Map("Customers V3", "accounts", "1.0.0.13"),
                Map("Exchange rates", "exchangerates", "1.0.0.2"),
            };
            var right = new[]
            {
                Map("Customers V3", "accounts", "1.0.0.12"),
                Map("Exchange rates", "exchangerates", "1.0.0.2"),
            };

            return Task.FromResult(DualWriteMapComparer.Compare(left, right));
        }

        private static DualWriteMap Map(string name, string ceEntity, string version) =>
            new(name, name, name, "project", "Running",
                new DualWriteTemplate("t", version, "Microsoft"), Array.Empty<DualWriteTemplate>())
            {
                RightEntityName = ceEntity,
            };
    }
}
