using System.Linq;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public class DualWriteCompareViewModelTests
{
    private static DualWriteCompareViewModel MakeVm() =>
        new(new FakeProfileStore(), new FakeDualWriteCompareService());

    [Fact]
    public void Defaults_pick_two_different_environments()
    {
        var vm = MakeVm();

        Assert.NotNull(vm.SelectedSource);
        Assert.NotNull(vm.SelectedTarget);
        Assert.NotEqual(vm.SelectedSource, vm.SelectedTarget);
        Assert.True(vm.CanCompare);
        Assert.False(vm.ShowSamePrompt);
    }

    [Fact]
    public void Same_source_and_target_disables_compare()
    {
        var vm = MakeVm();

        vm.SelectedTarget = vm.SelectedSource;

        Assert.False(vm.CanCompare);
        Assert.True(vm.ShowSamePrompt);
        Assert.False(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public async Task Compare_produces_diff_rows_and_a_summary()
    {
        var vm = MakeVm();

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.NotEmpty(vm.DiffRows);
        Assert.NotEmpty(vm.Summary);
        Assert.Equal(vm.DiffRows.Count, vm.Summary.Sum(b => b.Count));
        // The seeded set exercises every diff bucket, including an absent target.
        Assert.Contains(vm.DiffRows, r => r.Diff == DiffKind.OnlyInSource);
    }

    [Theory]
    [InlineData("1.0.0.1", MapState.Running, 1000, "1.0.0.1", MapState.Running, 1000, DiffKind.InSync)]
    [InlineData("1.0.0.1", MapState.Running, 1000, "1.0.0.2", MapState.Running, 1000, DiffKind.VersionDrift)]
    [InlineData("1.0.0.1", MapState.Running, 1000, "1.0.0.1", MapState.Paused, 1000, DiffKind.StateDiffers)]
    [InlineData("1.0.0.1", MapState.Running, 1000, "1.0.0.1", MapState.Running, 1500, DiffKind.RowDelta)]
    public void Classifier_buckets_a_present_pair(
        string sv, MapState ss, long sr, string tv, MapState ts, long tr, DiffKind expected)
    {
        var source = new DiffSide(ss, sv, sr);
        var target = new DiffSide(ts, tv, tr);

        Assert.Equal(expected, DiffClassifier.Classify(source, target));
    }

    [Fact]
    public void Classifier_handles_absent_sides()
    {
        var side = new DiffSide(MapState.Running, "1.0.0.1", 100);

        Assert.Equal(DiffKind.OnlyInSource, DiffClassifier.Classify(side, null));
        Assert.Equal(DiffKind.OnlyInTarget, DiffClassifier.Classify(null, side));
    }
}
