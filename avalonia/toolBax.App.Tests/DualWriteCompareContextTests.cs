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
    public async Task Late_error_after_target_change_does_not_publish_error()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        vm.SelectedTarget = vm.Environments[0];
        service.Gate.SetException(new InvalidOperationException("late failure"));
        await compare;

        Assert.Null(vm.Error);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task Changing_source_clears_completed_result_and_error()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.SetResult(new[] { Row() });
        await compare;
        vm.Error = "old error";

        vm.SelectedSource = vm.Environments[1];

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
        Assert.Empty(vm.Summary);
        Assert.Equal(0, vm.ComparedCount);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Unchanged_refresh_preserves_completed_result()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.SetResult(new[] { Row() });
        await compare;

        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.True(vm.HasResult);
        Assert.Single(vm.DiffRows);
    }

    [Fact]
    public async Task Disposed_compare_rejects_late_publication()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        vm.Dispose();
        service.Gate.SetResult(new[] { Row() });
        await compare;

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Same_id_store_url_edit_refresh_clears_completed_result()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.SetResult(new[] { Row() });
        await compare;
        store.Save(source with { Url = "https://changed.operations.dynamics.com" });

        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Same_id_store_auth_edit_refresh_rejects_held_result()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        store.Save(source with { ClientId = "changed-client" });
        vm.RefreshEnvironmentsCommand.Execute(null);
        service.Gate.SetResult(new[] { Row() });
        await compare;

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Deleted_selection_at_entry_makes_zero_calls_and_never_falls_back()
    {
        var (store, vm, service) = Make();
        store.Delete(vm.SelectedSource!.Id);

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal(0, service.Calls);
        Assert.DoesNotContain(vm.Environments, profile => profile.Id == "dev-usmf");
    }

    [Fact]
    public async Task Store_drift_at_entry_makes_zero_calls_and_requires_explicit_rerun()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        store.Save(source with { Tenant = "changed.onmicrosoft.com" });

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal(0, service.Calls);
        Assert.NotEqual(source, vm.SelectedSource);
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
