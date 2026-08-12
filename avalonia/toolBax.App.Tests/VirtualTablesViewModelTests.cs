using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class VirtualTablesViewModelTests
{
    private static EnvProfile EnvWith(string? dataverseUrl) =>
        new("env1", "Env", "contoso.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: dataverseUrl);

    private sealed class ErrorReader : IVirtualTableReader
    {
        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
            => Task.FromResult(VirtualTableLoadResult.Fail("boom"));
    }

    // Counts reads, so a test can prove a second Initialize did (or didn't) reload.
    private sealed class CountingReader : IVirtualTableReader
    {
        private readonly FakeVirtualTableReader _inner = new();
        public int Calls { get; private set; }

        public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
        {
            Calls++;
            return _inner.GetVirtualTablesAsync(ct);
        }
    }

    // Holds the read open on a gate so an environment switch can be interleaved with an in-flight load.
    // Entered completes once the read is actually parked, so the test never switches before the VM has
    // captured the environment it is loading for.
    private sealed class GatedReader : IVirtualTableReader
    {
        private readonly FakeVirtualTableReader _inner = new();
        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Gate { get; } = new();
        public int Calls { get; private set; }

        public async Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
        {
            Calls++;
            Entered.TrySetResult();
            await Gate.Task;
            return await _inner.GetVirtualTablesAsync(ct);
        }
    }

    // One parked read: Entered completes once the read is actually in flight, Gate releases its response.
    private sealed class PendingRead
    {
        public PendingRead(string envName) => EnvName = envName;

        /// <summary>Name of the environment that was active when this read started.</summary>
        public string EnvName { get; }

        public TaskCompletionSource Entered { get; } = new();
        public TaskCompletionSource Gate { get; } = new();
    }

    // Gates every read SEPARATELY, so two loads can be completed out of order — the newer environment's read
    // released first, the older one afterwards. Each read answers with a single table named after the
    // environment that was active when it started, so the grid's contents identify which load committed.
    private sealed class OutOfOrderReader : IVirtualTableReader
    {
        private readonly Func<EnvProfile?> _activeEnv;

        public OutOfOrderReader(Func<EnvProfile?> activeEnv) => _activeEnv = activeEnv;

        public List<PendingRead> Reads { get; } = new();

        /// <summary>Index of a read that should come back as a failure instead (-1 = all succeed).</summary>
        public int FailingRead { get; set; } = -1;

        public async Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
        {
            var index = Reads.Count;
            var read = new PendingRead(_activeEnv()?.Name ?? "none");
            Reads.Add(read);
            read.Entered.TrySetResult();
            await read.Gate.Task;
            return index == FailingRead
                ? VirtualTableLoadResult.Fail($"{read.EnvName} unreachable")
                : VirtualTableLoadResult.Ok(new[] { FoTableFor(read.EnvName) });
        }

        private static VirtualTableInfo FoTableFor(string envName) =>
            new($"mserp_{envName}entity", $"{envName} table", $"{envName}Entity", $"mserp_{envName}entities",
                "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222",
                IsManaged: true, VirtualTableSource.FinanceAndOperations);
    }

    private static EnvProfile VtEnv(string id, string name) =>
        new(id, name, $"{name}.operations.dynamics.com", "tenant", "USMF", "Tier 1",
            EnvStatus.Connected, DataverseUrl: $"https://{name}.crm.dynamics.com");

    // Mutable active-environment source: the shell switching environments under this cached VM.
    private sealed class EnvSwitch
    {
        public EnvProfile? Current { get; set; } = VtEnv("envA", "contoso");
        public EnvProfile? Get() => Current;
    }

    [Fact]
    public async Task Initialize_lists_only_fo_backed_tables_and_counts_the_rest()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Tables.Count);
        Assert.All(vm.Tables, t => Assert.True(t.IsFinanceAndOperations));
        Assert.Equal(1, vm.OtherVirtualCount);
        Assert.True(vm.HasOtherVirtual);
        Assert.False(vm.ShowEmptyState);
    }

    [Fact]
    public async Task Selecting_a_table_with_a_dataverse_url_builds_an_openable_list_link()
    {
        var launcher = new FakeUrlLauncher();
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader(),
            activeEnv: () => EnvWith("https://contoso.crm.dynamics.com"), launcher: launcher);
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.SelectedTable = vm.Tables.First();

        Assert.True(vm.HasSelectionLink);
        var url = vm.SelectedTableUrl;
        Assert.NotNull(url);
        Assert.Contains("pagetype=entitylist", url!);
        Assert.Contains("etn=" + vm.Tables.First().LogicalName, url);

        await vm.OpenInDataverseCommand.ExecuteAsync(null);
        Assert.Equal(url, launcher.LastUrl);
    }

    [Fact]
    public async Task Without_a_dataverse_url_the_open_link_is_unavailable()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader(), activeEnv: () => EnvWith(null));
        await vm.InitializeCommand.ExecuteAsync(null);
        vm.SelectedTable = vm.Tables.First();

        Assert.False(vm.HasSelectionLink);
        Assert.Null(vm.SelectedTableUrl);
        Assert.False(vm.OpenInDataverseCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_reader_error_surfaces_and_leaves_the_list_empty()
    {
        var vm = new VirtualTablesViewModel(new ErrorReader());

        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.True(vm.HasLoadError);
        Assert.Equal("boom", vm.LoadError);
        Assert.Empty(vm.Tables);
    }

    [Fact]
    public async Task Search_filters_by_name()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader());
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Search = "Vendor";

        Assert.Single(vm.Filtered);
    }

    [Fact]
    public async Task A_search_matching_nothing_shows_the_no_matches_hint()
    {
        var vm = new VirtualTablesViewModel(new FakeVirtualTableReader());
        await vm.InitializeCommand.ExecuteAsync(null);

        vm.Search = "zzz-no-such-table";

        Assert.Empty(vm.Filtered);
        Assert.True(vm.NoSearchMatches);
        Assert.False(vm.ShowEmptyState);
    }

    // ── Environment coherence (#153): the load is stamped with its environment, not one-shot. ─────────

    [Fact]
    public async Task Re_initializing_after_an_environment_switch_reloads_and_relabels()
    {
        var reader = new CountingReader();
        var env = new EnvSwitch();
        var vm = new VirtualTablesViewModel(reader, activeEnv: env.Get, launcher: new FakeUrlLauncher());

        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(1, reader.Calls);
        Assert.Equal("contoso", vm.LoadedEnvName);

        // The shell switched environments and the "Refresh open tools?" prompt was declined; re-activating
        // this screen must reload, or the grid would list env A's tables while the deep link targets env B.
        env.Current = VtEnv("envB", "fabrikam");
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(2, reader.Calls);
        Assert.Equal("fabrikam", vm.LoadedEnvName);
        vm.SelectedTable = vm.Tables.First();
        Assert.Contains("fabrikam.crm.dynamics.com", vm.SelectedTableUrl!);
    }

    [Fact]
    public async Task Re_initializing_under_the_same_environment_does_not_reload()
    {
        var reader = new CountingReader();
        var env = new EnvSwitch();
        var vm = new VirtualTablesViewModel(reader, activeEnv: env.Get);

        await vm.InitializeCommand.ExecuteAsync(null);
        await vm.InitializeCommand.ExecuteAsync(null);

        Assert.Equal(1, reader.Calls);   // re-navigating stays cheap
    }

    [Fact]
    public async Task An_environment_switch_landing_mid_load_discards_the_result_it_arrived_too_late_for()
    {
        var reader = new GatedReader();
        var env = new EnvSwitch();
        var vm = new VirtualTablesViewModel(reader, activeEnv: env.Get);

        var loading = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Entered.Task;          // parked inside the read, with env A captured
        env.Current = VtEnv("envB", "fabrikam");
        reader.Gate.TrySetResult();
        await loading;

        // Env A's tables belong to a screen the shell has already moved off, so nothing is listed or
        // labelled — and the empty state stays off, because this isn't "env B has no virtual tables".
        Assert.Empty(vm.Tables);
        Assert.Equal(string.Empty, vm.LoadedEnvName);
        Assert.False(vm.HasLoadError);
        Assert.False(vm.IsLoading);
        Assert.False(vm.ShowEmptyState);
        Assert.Equal(1, reader.Calls);

        // Nothing was stamped, so the next activation loads env B for real.
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(2, reader.Calls);
        Assert.Equal("fabrikam", vm.LoadedEnvName);
    }

    [Fact]
    public async Task A_stale_load_answering_last_does_not_replace_the_newer_environments_tables()
    {
        var env = new EnvSwitch();
        var reader = new OutOfOrderReader(env.Get);
        var vm = new VirtualTablesViewModel(reader, activeEnv: env.Get);

        // A refresh under env A is still in flight…
        var loadA = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Reads[0].Entered.Task;

        // …when the shell switches to env B and this screen reloads for it. B answers FIRST.
        env.Current = VtEnv("envB", "fabrikam");
        var loadB = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Reads[1].Entered.Task;
        reader.Reads[1].Gate.TrySetResult();
        await loadB;

        Assert.Equal("fabrikam", vm.LoadedEnvName);
        Assert.Equal("fabrikam table", Assert.Single(vm.Tables).Title);

        // A's slower response lands LAST. Left unguarded it would replace env B's tables and stamp with
        // env A's, leaving the grid showing one environment while the deep link targets another.
        reader.Reads[0].Gate.TrySetResult();
        await loadA;

        Assert.Equal("fabrikam", vm.LoadedEnvName);
        Assert.Equal("fabrikam table", Assert.Single(vm.Tables).Title);
        Assert.False(vm.HasLoadError);
        Assert.False(vm.IsLoading);

        // B's stamp survived, so re-activating under env B stays cheap (no third read).
        await vm.InitializeCommand.ExecuteAsync(null);
        Assert.Equal(2, reader.Reads.Count);
    }

    [Fact]
    public async Task A_stale_load_failing_last_does_not_blank_the_newer_environments_tables()
    {
        var env = new EnvSwitch();
        var reader = new OutOfOrderReader(env.Get) { FailingRead = 0 };   // env A's read errors, slowly
        var vm = new VirtualTablesViewModel(reader, activeEnv: env.Get);

        var loadA = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Reads[0].Entered.Task;

        env.Current = VtEnv("envB", "fabrikam");
        var loadB = vm.InitializeCommand.ExecuteAsync(null);
        await reader.Reads[1].Entered.Task;
        reader.Reads[1].Gate.TrySetResult();
        await loadB;

        // The stale FAILURE is just as much a stale result: env A being unreachable says nothing about
        // env B, so it must not clear B's list or raise an error banner over it.
        reader.Reads[0].Gate.TrySetResult();
        await loadA;

        Assert.False(vm.HasLoadError);
        Assert.Equal(string.Empty, vm.LoadError);
        Assert.Equal("fabrikam table", Assert.Single(vm.Tables).Title);
        Assert.Equal("fabrikam", vm.LoadedEnvName);
    }
}
