using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class DualWriteCompareViewModelTests
{
    private static DualWriteCompareViewModel MakeVm(IDualWriteCompareService? service = null) =>
        new(new FakeProfileStore(), service ?? new FakeDualWriteCompareService());

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
    public async Task Compare_produces_diff_rows_and_a_verdict_summary()
    {
        var vm = MakeVm();

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);
        Assert.NotEmpty(vm.DiffRows);
        Assert.NotEmpty(vm.Summary);
        Assert.Equal(vm.DiffRows.Count, vm.Summary.Sum(b => b.Count));
        // The seed exercises presence + version + state verdicts.
        Assert.Contains(vm.DiffRows, r => r.Verdict == DualWriteComparisonVerdict.OnlyInLeft);
        Assert.Contains(vm.DiffRows, r => r.Verdict == DualWriteComparisonVerdict.OnlyInRight);
        Assert.Contains(vm.DiffRows, r => r.Verdict == DualWriteComparisonVerdict.VersionMismatch);
        Assert.False(vm.IsBusy);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task A_compare_failure_surfaces_an_error_and_clears_results()
    {
        var vm = MakeVm(new ThrowingCompareService());

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
        Assert.Contains("gateway unreachable", vm.Error);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(DualWriteComparisonVerdict.Identical, "identical")]
    [InlineData(DualWriteComparisonVerdict.VersionMismatch, "version mismatch")]
    [InlineData(DualWriteComparisonVerdict.StateMismatch, "state mismatch")]
    [InlineData(DualWriteComparisonVerdict.OnlyInLeft, "only in source")]
    [InlineData(DualWriteComparisonVerdict.OnlyInRight, "only in target")]
    public void Verdict_labels_are_friendly(DualWriteComparisonVerdict verdict, string expected) =>
        Assert.Equal(expected, CompareVerdict.Label(verdict));

    [Fact]
    public async Task Core_compare_service_connects_both_environments_and_diffs()
    {
        // Both envs resolve to the same seeded maps via the fake connector, so every map is identical —
        // this proves the service connects each side and runs the Core comparer over both map sets.
        var service = new CoreDualWriteCompareService(new FakeDualWriteConnector());
        var env = new EnvProfile("e", "Env", "https://x", "t", "USMF", "Tier", EnvStatus.Connected);

        var rows = await service.CompareAsync(env, env, CancellationToken.None);

        Assert.Equal(FakeDualWriteConnector.SeedMaps().Count, rows.Count);
        Assert.All(rows, r => Assert.Equal(DualWriteComparisonVerdict.Identical, r.Verdict));
    }

    private sealed class ThrowingCompareService : IDualWriteCompareService
    {
        public Task<System.Collections.Generic.IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(
            EnvProfile source, EnvProfile target, CancellationToken ct = default)
            => Task.FromException<System.Collections.Generic.IReadOnlyList<DualWriteMapComparisonRow>>(
                new System.InvalidOperationException("gateway unreachable"));
    }
}
