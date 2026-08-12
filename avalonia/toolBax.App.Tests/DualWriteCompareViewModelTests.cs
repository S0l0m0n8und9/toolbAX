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

    // ── Picker freshness (#153): the shell caches this VM and EnvProfile is an immutable record the
    // Profiles screen replaces on save, so the pickers are re-read on every activation. ──────────────

    [Fact]
    public void RefreshEnvironments_rebinds_a_selection_to_the_edited_profile_record()
    {
        var store = new FakeProfileStore();
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());
        var original = vm.SelectedSource!;

        // A profile edit replaces the record (same Id, corrected URL) — e.g. fixing a typo'd F&O host.
        store.Save(original with { Url = "contoso-dev-corrected.operations.dynamics.com" });
        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.Equal(original.Id, vm.SelectedSource!.Id);
        Assert.Equal("contoso-dev-corrected.operations.dynamics.com", vm.SelectedSource!.Url);
        Assert.NotSame(original, vm.SelectedSource);
        Assert.Contains(vm.Environments, e => ReferenceEquals(e, vm.SelectedSource));
    }

    [Fact]
    public void RefreshEnvironments_falls_back_when_the_selected_profile_was_deleted()
    {
        var store = new FakeProfileStore();
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());
        var deleted = vm.SelectedSource!;

        store.Delete(deleted.Id);
        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.DoesNotContain(vm.Environments, e => e.Id == deleted.Id);
        Assert.NotNull(vm.SelectedSource);
        Assert.Equal(store.GetAll()[0].Id, vm.SelectedSource!.Id);   // fell back to the default first pick
    }

    [Fact]
    public void RefreshEnvironments_picks_up_a_profile_added_after_construction()
    {
        var store = new FakeProfileStore();
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());
        var added = new EnvProfile("new-env", "New Env", "contoso-new.operations.dynamics.com",
            "contoso.onmicrosoft.com", "USMF", "Tier 2", EnvStatus.Connected);

        store.Save(added);
        Assert.DoesNotContain(vm.Environments, e => e.Id == added.Id);   // construction was only a snapshot

        vm.RefreshEnvironmentsCommand.Execute(null);

        Assert.Contains(vm.Environments, e => e.Id == added.Id);
    }

    // ── Same-environment guard (#153): identity is the gateway host, not the record reference. ────────

    [Fact]
    public void Two_profiles_for_one_fo_environment_cannot_be_compared()
    {
        // The normal case this guards: an interactive profile and an SPN profile for the SAME environment.
        // Distinct records (so the old ReferenceEquals guard passed), one URL with a scheme + trailing
        // slash + upper case, the other bare — both must normalize to the same host.
        var store = new FakeProfileStore(new[]
        {
            new EnvProfile("pgw-interactive", "PGW (interactive)", "HTTPS://PGW.operations.dynamics.com/",
                "pgw.onmicrosoft.com", "PGW", "Tier 1", EnvStatus.Connected),
            new EnvProfile("pgw-spn", "PGW (SPN)", "pgw.operations.dynamics.com",
                "pgw.onmicrosoft.com", "PGW", "Tier 1", EnvStatus.Connected),
        });
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());

        Assert.NotSame(vm.SelectedSource, vm.SelectedTarget);
        Assert.False(vm.CanCompare);
        Assert.True(vm.ShowSamePrompt);
        Assert.False(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public void Profiles_for_different_fo_hosts_can_be_compared()
    {
        var store = new FakeProfileStore(new[]
        {
            new EnvProfile("dev", "Dev", "https://contoso-dev.operations.dynamics.com",
                "contoso.onmicrosoft.com", "USMF", "Tier 1", EnvStatus.Connected),
            new EnvProfile("uat", "UAT", "contoso-uat.operations.dynamics.com",
                "contoso.onmicrosoft.com", "USMF", "Tier 2", EnvStatus.Connected),
        });
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());

        Assert.True(vm.CanCompare);
        Assert.False(vm.ShowSamePrompt);
        Assert.True(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public void A_profile_with_no_url_cannot_be_compared_against_a_configured_one()
    {
        // Blank vs configured is NOT the same-environment case — the hosts differ (empty vs a real host) —
        // so the same-host guard leaves it enabled on its own. There is no gateway to connect to on one
        // side, so Compare must be disabled outright, with no "same environment" prompt.
        var store = new FakeProfileStore(new[]
        {
            new EnvProfile("not-configured", "Not configured yet", string.Empty,
                "contoso.onmicrosoft.com", "USMF", "Tier 1", EnvStatus.Disconnected),
            new EnvProfile("uat", "UAT", "contoso-uat.operations.dynamics.com",
                "contoso.onmicrosoft.com", "USMF", "Tier 2", EnvStatus.Connected),
        });
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());

        Assert.False(vm.CanCompare);
        Assert.False(vm.ShowSamePrompt);
        Assert.False(vm.CompareCommand.CanExecute(null));

        // Blocked whichever side the URL-less profile sits on (a whitespace-only URL is just as unusable).
        (vm.SelectedSource, vm.SelectedTarget) = (vm.SelectedTarget, vm.SelectedSource);
        Assert.False(vm.CanCompare);
        Assert.False(vm.ShowSamePrompt);

        vm.SelectedTarget = vm.SelectedTarget! with { Url = "   " };
        Assert.False(vm.CanCompare);
        Assert.False(vm.ShowSamePrompt);
    }

    [Fact]
    public void Two_profiles_with_no_url_still_read_as_the_same_environment()
    {
        // Both resolve to an empty host, so this stays the same-environment case: Compare is off and the
        // empty-state prompt (rather than a silently dead button) explains why.
        var store = new FakeProfileStore(new[]
        {
            new EnvProfile("blank-a", "Blank A", string.Empty,
                "contoso.onmicrosoft.com", "USMF", "Tier 1", EnvStatus.Disconnected),
            new EnvProfile("blank-b", "Blank B", string.Empty,
                "contoso.onmicrosoft.com", "USMF", "Tier 2", EnvStatus.Disconnected),
        });
        var vm = new DualWriteCompareViewModel(store, new FakeDualWriteCompareService());

        Assert.False(vm.CanCompare);
        Assert.True(vm.ShowSamePrompt);
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
    // #160: the unpairable verdict needs a label of its own, or the grid falls back to the enum name.
    [InlineData(DualWriteComparisonVerdict.Ambiguous, "cannot compare")]
    public void Verdict_labels_are_friendly(DualWriteComparisonVerdict verdict, string expected) =>
        Assert.Equal(expected, CompareVerdict.Label(verdict));

    // ── Result scale + the empty result (#168 lows): an empty grid with no chips and no count was
    // indistinguishable from "every map is identical", i.e. it read as parity. ────────────────────────

    [Fact]
    public async Task An_empty_compare_result_is_reported_as_empty_rather_than_as_parity()
    {
        var vm = MakeVm(new StubCompareService());

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.True(vm.HasResult);              // it completed: this is not the error state
        Assert.Null(vm.Error);
        Assert.Empty(vm.DiffRows);
        Assert.Empty(vm.Summary);               // no verdict occurred, so there are no chips to read
        Assert.Equal(0, vm.ComparedCount);
        Assert.Equal("0 maps compared", vm.ComparedSummary);
        Assert.True(vm.ShowEmptyResult);
        Assert.False(vm.ShowDiffGrid);          // the bare grid is exactly what looked like "all in sync"
    }

    [Fact]
    public async Task A_non_empty_result_counts_its_rows_and_shows_the_grid()
    {
        var vm = MakeVm();

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.DiffRows);
        Assert.Equal(vm.DiffRows.Count, vm.ComparedCount);
        Assert.Equal($"{vm.DiffRows.Count} maps compared", vm.ComparedSummary);
        Assert.True(vm.ShowDiffGrid);
        Assert.False(vm.ShowEmptyResult);
    }

    [Fact]
    public async Task A_single_compared_map_reads_in_the_singular()
    {
        var vm = MakeVm(new StubCompareService(
            new DualWriteMapComparisonRow("Customers V3", true, true, "1.0.0.12", "1.0.0.12",
                "Running", "Running", DualWriteComparisonVerdict.Identical)));

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal("1 map compared", vm.ComparedSummary);
        Assert.True(vm.ShowDiffGrid);
        Assert.False(vm.ShowEmptyResult);
    }

    [Fact]
    public async Task A_failed_compare_is_not_reported_as_an_empty_result()
    {
        // The error banner owns the failure case; an empty-result message on top of it would be noise.
        var vm = MakeVm(new ThrowingCompareService());

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.ComparedCount);
        Assert.False(vm.ShowEmptyResult);
        Assert.False(vm.ShowDiffGrid);
    }

    [Fact]
    public async Task Core_compare_service_connects_the_two_environments_one_at_a_time()
    {
        // Connecting can open a MODAL browser sign-in, so the two connects must NOT overlap: two modal
        // windows stack on the same owner, closing either re-enables the main window while the other is
        // still up, and a manually-closed window's best-effort capture can be attributed to the wrong
        // environment. The recorder yields inside ConnectAsync, so an overlapping caller is caught
        // deterministically rather than by timing. Once both sides are signed in, the map loads are plain
        // HTTP and are explicitly allowed to overlap (#168 P2) — this test only pins down that the
        // connects stay strictly ordered and that both loads still complete and feed the diff.
        var connector = new SequenceRecordingConnector();
        var service = new CoreDualWriteCompareService(connector);

        var rows = await service.CompareAsync(EnvNamed("src"), EnvNamed("tgt"), CancellationToken.None);

        Assert.Equal(new[] { "start:src", "finish:src", "start:tgt", "finish:tgt" }, connector.Log);
        Assert.Equal(1, connector.MaxConcurrent);
        Assert.Equal(FakeDualWriteConnector.SeedMaps().Count, rows.Count);   // both loads completed and diffed
    }

    [Fact]
    public async Task A_map_load_failure_on_one_side_still_disposes_both_gateways()
    {
        // The loads run in parallel (Task.WhenAll) — a failure on one side must not leave the other's
        // gateway (and its HttpClient) undisposed just because Task.WhenAll rethrows before both
        // finallys have necessarily been observed by the caller.
        var sourceGateway = new DisposableStubGateway(FakeDualWriteConnector.SeedMaps());
        var targetGateway = new DisposableStubGateway(new InvalidOperationException("map load failed"));
        var connector = new FixedGatewayConnector(sourceGateway, targetGateway);
        var service = new CoreDualWriteCompareService(connector);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompareAsync(EnvNamed("src"), EnvNamed("tgt"), CancellationToken.None));

        Assert.True(sourceGateway.Disposed);   // the healthy side's load still finished and disposed
        Assert.True(targetGateway.Disposed);
    }

    [Fact]
    public async Task A_target_sign_in_failure_disposes_the_already_connected_source_gateway()
    {
        // Source signs in first and its gateway is live before target's sign-in is even attempted. If
        // target's sign-in then fails, source never reaches its own load (and its dispose) — unlike the
        // old fully-sequential flow, splitting connect from load means this must be disposed explicitly
        // or it leaks.
        var sourceGateway = new DisposableStubGateway(FakeDualWriteConnector.SeedMaps());
        var connector = new FailsSecondConnectConnector(sourceGateway, new InvalidOperationException("sign-in failed"));
        var service = new CoreDualWriteCompareService(connector);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CompareAsync(EnvNamed("src"), EnvNamed("tgt"), CancellationToken.None));

        Assert.True(sourceGateway.Disposed);
    }

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

    private static EnvProfile EnvNamed(string id) =>
        new(id, id, $"https://{id}.operations.dynamics.com", "contoso.onmicrosoft.com", "USMF", "Tier 2",
            EnvStatus.Connected);

    private sealed class ThrowingCompareService : IDualWriteCompareService
    {
        public Task<System.Collections.Generic.IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(
            EnvProfile source, EnvProfile target, CancellationToken ct = default)
            => Task.FromException<System.Collections.Generic.IReadOnlyList<DualWriteMapComparisonRow>>(
                new System.InvalidOperationException("gateway unreachable"));
    }

    /// <summary>Returns exactly the rows it was given — no rows at all for the empty-result case.</summary>
    private sealed class StubCompareService : IDualWriteCompareService
    {
        private readonly IReadOnlyList<DualWriteMapComparisonRow> _rows;

        public StubCompareService(params DualWriteMapComparisonRow[] rows) => _rows = rows;

        public Task<IReadOnlyList<DualWriteMapComparisonRow>> CompareAsync(
            EnvProfile source, EnvProfile target, CancellationToken ct = default) => Task.FromResult(_rows);
    }

    /// <summary>
    /// Records each connect's start and finish (and the peak number in flight), yielding in between so any
    /// overlap is observed deterministically: the second connect of a parallel caller starts at the first
    /// yield. Hands back a session over the shared in-memory gateway so the compare can complete.
    /// </summary>
    private sealed class SequenceRecordingConnector : IDualWriteConnector
    {
        private readonly List<string> _log = new();
        private int _inFlight;

        public IReadOnlyList<string> Log
        {
            get { lock (_log) { return _log.ToList(); } }
        }

        public int MaxConcurrent { get; private set; }

        public async Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
        {
            lock (_log)
            {
                _log.Add($"start:{env.Id}");
                MaxConcurrent = Math.Max(MaxConcurrent, ++_inFlight);
            }

            // Each yield is an opportunity for another connect to begin — which is what the browser sign-in
            // inside a real connect gives the caller in abundance.
            for (var i = 0; i < 3; i++)
            {
                await Task.Yield();
            }

            lock (_log)
            {
                _inFlight--;
                _log.Add($"finish:{env.Id}");
            }

            return new DualWriteSession(
                new FakeCoreDualWriteGateway(FakeDualWriteConnector.SeedMaps()),
                "fake-cid", "Contoso", env.Id, "https://fake-gateway.dual-write.example");
        }
    }

    /// <summary>Hands back a fixed, caller-supplied gateway per environment id ("src"/"tgt") — lets a test
    /// arrange independent success/failure behaviour for each side's load.</summary>
    private sealed class FixedGatewayConnector : IDualWriteConnector
    {
        private readonly IDualWriteGateway _sourceGateway;
        private readonly IDualWriteGateway _targetGateway;

        public FixedGatewayConnector(IDualWriteGateway sourceGateway, IDualWriteGateway targetGateway)
        {
            _sourceGateway = sourceGateway;
            _targetGateway = targetGateway;
        }

        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default)
        {
            var gateway = env.Id == "src" ? _sourceGateway : _targetGateway;
            return Task.FromResult(
                new DualWriteSession(gateway, "fake-cid", "Contoso", env.Id, "https://fake-gateway.dual-write.example"));
        }
    }

    /// <summary>Connects "src" normally but fails signing in to "tgt" — for proving the already-connected
    /// source gateway is disposed rather than leaked when the second sign-in fails.</summary>
    private sealed class FailsSecondConnectConnector : IDualWriteConnector
    {
        private readonly IDualWriteGateway _sourceGateway;
        private readonly Exception _targetFailure;

        public FailsSecondConnectConnector(IDualWriteGateway sourceGateway, Exception targetFailure)
        {
            _sourceGateway = sourceGateway;
            _targetFailure = targetFailure;
        }

        public Task<DualWriteSession> ConnectAsync(EnvProfile env, CancellationToken ct = default) =>
            env.Id == "src"
                ? Task.FromResult(new DualWriteSession(
                    _sourceGateway, "fake-cid", "Contoso", env.Id, "https://fake-gateway.dual-write.example"))
                : Task.FromException<DualWriteSession>(_targetFailure);
    }

    /// <summary>Minimal disposable <see cref="IDualWriteGateway"/>: returns fixed maps, or throws, from
    /// <see cref="GetMapsAsync"/>, and tracks whether it was disposed.</summary>
    private sealed class DisposableStubGateway : IDualWriteGateway, IDisposable
    {
        private readonly IReadOnlyList<DualWriteMap>? _maps;
        private readonly Exception? _failWith;

        public bool Disposed { get; private set; }

        public DisposableStubGateway(IReadOnlyList<DualWriteMap> maps) => _maps = maps;

        public DisposableStubGateway(Exception failWith) => _failWith = failWith;

        public void Dispose() => Disposed = true;

        public Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default) =>
            _failWith is not null
                ? Task.FromException<IReadOnlyList<DualWriteMap>>(_failWith)
                : Task.FromResult(_maps!);

        public Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DualWriteActionResponse> StartActionAsync(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DualWriteActionResponse> SwitchActiveTemplateAsync(string cid, string projectId, string templateId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task ResetLinksAsync(string cid, DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities, bool forceReset, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task ApplyIntegrationKeysAsync(string datasetName, string ceEntityName, IReadOnlyList<string> keyFields, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
