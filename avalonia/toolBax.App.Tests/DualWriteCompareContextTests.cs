using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class DualWriteCompareContextTests
{
    private sealed class GatedCompare : IDualWriteCompareService
    {
        public int Calls { get; private set; }
        public TaskCompletionSource<IReadOnlyList<DualWriteMapComparisonRow>> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default)
        {
            Calls++;
            return Gate.Task; // deliberately ignores cancellation
        }
    }

    private static DualWriteMapComparisonRow Row() =>
        new("Map", true, true, "1", "1", "Running", "Running", DualWriteComparisonVerdict.Identical);

    private static (FakeProfileStore Store, DualWriteCompareViewModel Vm, GatedCompare Service) Make()
    {
        var store = new FakeProfileStore();
        var service = new GatedCompare();
        return (store, new DualWriteCompareViewModel(store, service), service);
    }

    [Fact]
    public async Task Changing_target_clears_completed_result_immediately()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.SetResult(new[] { Row() });
        await compare;

        vm.SelectedTarget = vm.Environments[0];

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
        Assert.Empty(vm.Summary);
        Assert.Equal(0, vm.ComparedCount);
    }

    [Fact]
    public async Task A_to_b_to_a_rejects_the_original_late_result()
    {
        var (_, vm, service) = Make();
        var original = vm.SelectedTarget!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        vm.SelectedTarget = vm.Environments[0];
        vm.SelectedTarget = original;
        service.Gate.SetResult(new[] { Row() });
        await compare;

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Overlapping_direct_compares_start_only_one_service_call()
    {
        var (_, vm, service) = Make();
        var first = vm.CompareCommand.ExecuteAsync(null);
        var second = vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(1, service.Calls);
        service.Gate.SetResult(new[] { Row() });
        await Task.WhenAll(first, second);
    }
}
