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
