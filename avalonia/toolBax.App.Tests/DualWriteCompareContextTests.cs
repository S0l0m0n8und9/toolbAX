using System;
using System.Collections.Generic;
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

public class DualWriteCompareContextTests
{
    private sealed class GatedCompare : IDualWriteCompareService
    {
        public int Calls { get; private set; }
        public List<(EnvProfile Source, EnvProfile Target, CancellationToken Token)> Inputs { get; } = new();
        public TaskCompletionSource<IReadOnlyList<DualWriteMapComparisonRow>> Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(EnvProfile source, EnvProfile target, CancellationToken ct = default)
        {
            Calls++;
            Inputs.Add((source, target, ct));
            return Gate.Task; // deliberately ignores cancellation
        }
    }

    private static Task Watch(Task task) =>
        task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    private static async Task ReleaseAndAwait(GatedCompare service, params Task[] tasks)
    {
        service.Gate.TrySetResult(new[] { Row() });
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

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
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Late_error_after_target_change_does_not_publish_error()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        vm.SelectedTarget = vm.Environments[0];
        service.Gate.TrySetException(new InvalidOperationException("late failure"));
        await Watch(compare);

        Assert.Null(vm.Error);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task Changing_source_clears_completed_result_and_error()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);
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
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

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
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Same_id_store_url_edit_refresh_clears_completed_result()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);
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
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Deleted_selection_at_entry_makes_zero_calls_and_never_falls_back()
    {
        var (store, vm, service) = Make();
        store.Delete(vm.SelectedSource!.Id);

        var compare = vm.CompareCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(0, service.Calls);
            await Watch(compare);
            Assert.DoesNotContain(vm.Environments, profile => profile.Id == "dev-usmf");
        }
        finally
        {
            await ReleaseAndAwait(service, compare);
        }
    }

    [Fact]
    public async Task Store_drift_at_entry_makes_zero_calls_and_requires_explicit_rerun()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        store.Save(source with { Tenant = "changed.onmicrosoft.com" });

        var compare = vm.CompareCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(0, service.Calls);
            await Watch(compare);
            Assert.NotEqual(source, vm.SelectedSource);
        }
        finally
        {
            await ReleaseAndAwait(service, compare);
        }
    }

    [Fact]
    public async Task Overlapping_direct_compares_start_only_one_service_call()
    {
        var (_, vm, service) = Make();
        var first = vm.CompareCommand.ExecuteAsync(null);
        var acceptedToken = Assert.Single(service.Inputs).Token;
        var second = vm.CompareCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(1, service.Calls);
            Assert.False(acceptedToken.IsCancellationRequested);
            Assert.True(vm.IsBusy);

            vm.CompareCancelCommand.Execute(null);

            Assert.True(acceptedToken.IsCancellationRequested);
            Assert.True(vm.IsBusy);
        }
        finally
        {
            await ReleaseAndAwait(service, first, second);
        }
        Assert.Single(service.Inputs);
        Assert.True(service.Inputs.Single().Token.CanBeCanceled);
        Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Direct_execute_with_same_or_missing_host_makes_zero_service_calls(bool sameHost)
    {
        var (_, vm, service) = Make();
        vm.SelectedTarget = sameHost
            ? vm.SelectedSource
            : vm.SelectedTarget! with { Url = "   " };
        Assert.False(vm.CanCompare);

        var compare = vm.CompareCommand.ExecuteAsync(null);
        try
        {
            Assert.Equal(0, service.Calls);
            await Watch(compare);
        }
        finally
        {
            await ReleaseAndAwait(service, compare);
        }
    }

    [Fact]
    public async Task Completed_result_keeps_captured_attribution_across_cosmetic_refresh()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        var target = vm.SelectedTarget!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        service.Gate.TrySetResult(new[] { Row() });
        await Watch(compare);

        var context = Assert.IsType<CompareResultContext>(vm.ResultContext);
        Assert.Equal(source.Name, context.SourceName);
        Assert.Equal(source.Url, context.SourceUrl);
        Assert.Equal(target.Name, context.TargetName);
        Assert.Equal(target.Url, context.TargetUrl);
        Assert.NotEqual(default, context.CompletedAt);

        store.Save(source with { Name = "Cosmetic rename" });
        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.True(vm.HasResult);
        Assert.Same(context, vm.ResultContext);
        Assert.Equal(source.Name, vm.ResultContext!.SourceName);
        Assert.Equal("Cosmetic rename", vm.SelectedSource!.Name);
    }

    [Fact]
    public async Task Selection_change_cancels_owner_but_busy_releases_only_after_owner_finishes()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        var token = Assert.Single(service.Inputs).Token;

        vm.SelectedTarget = vm.Environments[0];

        Assert.True(token.IsCancellationRequested);
        Assert.True(vm.IsBusy);
        Assert.False(vm.HasResult);

        await ReleaseAndAwait(service, compare);

        Assert.False(vm.IsBusy);
        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task Explicit_cancel_discards_cancellation_ignoring_result_and_releases_owner()
    {
        var (_, vm, service) = Make();
        var compare = vm.CompareCommand.ExecuteAsync(null);
        var token = Assert.Single(service.Inputs).Token;

        vm.CompareCancelCommand.Execute(null);

        Assert.True(token.IsCancellationRequested);
        Assert.True(vm.IsBusy);
        await ReleaseAndAwait(service, compare);

        Assert.False(vm.IsBusy);
        Assert.False(vm.HasResult);
        Assert.Empty(vm.DiffRows);
    }

    [Fact]
    public async Task Deleted_store_selection_during_compare_discards_completion_and_refreshes_choices()
    {
        var (store, vm, service) = Make();
        var sourceId = vm.SelectedSource!.Id;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        store.Delete(sourceId);

        await ReleaseAndAwait(service, compare);

        Assert.False(vm.HasResult);
        Assert.DoesNotContain(vm.Environments, profile => profile.Id == sourceId);
        Assert.Contains("changed", vm.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Store_drift_then_late_service_error_refreshes_choices_without_publishing_stale_error()
    {
        var (store, vm, service) = Make();
        var source = vm.SelectedSource!;
        var compare = vm.CompareCommand.ExecuteAsync(null);
        store.Save(source with { Tenant = "changed.onmicrosoft.com" });
        service.Gate.TrySetException(new InvalidOperationException("late failure"));
        await Watch(compare);

        Assert.False(vm.HasResult);
        Assert.DoesNotContain("late failure", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("changed", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("changed.onmicrosoft.com", vm.SelectedSource!.Tenant);
    }
}
