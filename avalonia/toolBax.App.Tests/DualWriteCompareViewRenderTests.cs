using System;
using System.Globalization;
using System.Linq;
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

    private static object Brush(DualWriteComparisonVerdict verdict) =>
        VerdictToBrushConverter.Instance.Convert(verdict, typeof(IBrush), null, CultureInfo.InvariantCulture);

    private static Color Colour(DualWriteComparisonVerdict verdict) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(Brush(verdict)).Color;
}
