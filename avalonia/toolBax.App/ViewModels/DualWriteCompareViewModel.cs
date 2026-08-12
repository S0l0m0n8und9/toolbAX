using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>Friendly labels for a comparison verdict (used in chips + the diff column).</summary>
public static class CompareVerdict
{
    public static string Label(DualWriteComparisonVerdict verdict) => verdict switch
    {
        DualWriteComparisonVerdict.Identical => "identical",
        DualWriteComparisonVerdict.OnlyInLeft => "only in source",
        DualWriteComparisonVerdict.OnlyInRight => "only in target",
        DualWriteComparisonVerdict.VersionMismatch => "version mismatch",
        DualWriteComparisonVerdict.StateMismatch => "state mismatch",
        _ => verdict.ToString(),
    };
}

/// <summary>A diff bucket summary chip: a count of maps with a given verdict.</summary>
public sealed record DiffBucket(DualWriteComparisonVerdict Verdict, int Count)
{
    public string Label => $"{Count} {CompareVerdict.Label(Verdict)}";
}

/// <summary>
/// Dual-Write Compare (control-map §5): pick a source and target environment, compare their dual-write
/// maps (connect each gateway → load maps → diff via the Core <see cref="DualWriteMapComparer"/>), and
/// show a per-map diff grid + verdict summary chips. Compare is enabled only when the two picks resolve
/// to different F&amp;O environments.
/// </summary>
public partial class DualWriteCompareViewModel : ObservableObject
{
    private readonly IProfileStore _store;
    private readonly IDualWriteCompareService _service;

    public ObservableCollection<EnvProfile> Environments { get; }
    public ObservableCollection<DualWriteMapComparisonRow> DiffRows { get; } = new();
    public ObservableCollection<DiffBucket> Summary { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(ShowSamePrompt))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private EnvProfile? _selectedSource;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    [NotifyPropertyChangedFor(nameof(ShowSamePrompt))]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private EnvProfile? _selectedTarget;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private bool _isBusy;

    /// <summary>A compare failure (e.g. a gateway connect error) for the error banner; null when fine.</summary>
    [ObservableProperty]
    private string? _error;

    public DualWriteCompareViewModel(IProfileStore store, IDualWriteCompareService service)
    {
        _store = store;
        _service = service;
        Environments = new ObservableCollection<EnvProfile>();
        RefreshEnvironments();
    }

    /// <summary>
    /// Re-reads the profile store into the pickers. Fired on every view activation (not just at
    /// construction): this VM is cached by the shell and <see cref="EnvProfile"/> is an immutable record
    /// that the Profiles screen REPLACES on save, so a construction-time snapshot would keep comparing a
    /// stale copy — a URL corrected in Profiles would never reach the compare, and added/deleted profiles
    /// would only appear after an app restart. Selections are preserved by <see cref="EnvProfile.Id"/> and
    /// rebound to the new record instances so their URL/tenant are current; a selection whose profile is
    /// gone falls back to the defaults. In-flight/previous compare RESULTS are deliberately untouched —
    /// this only refreshes the pickers.
    /// </summary>
    [RelayCommand]
    private void RefreshEnvironments()
    {
        var sourceId = SelectedSource?.Id;
        var targetId = SelectedTarget?.Id;

        Environments.Clear();
        foreach (var env in _store.GetAll())
        {
            Environments.Add(env);
        }

        // Rebind by id (the record instance changed), else fall back to the default picks.
        SelectedSource = ById(sourceId) ?? Environments.FirstOrDefault();
        SelectedTarget = ById(targetId)
            ?? Environments.Skip(1).FirstOrDefault()
            ?? Environments.FirstOrDefault();
    }

    private EnvProfile? ById(string? id) =>
        id is null ? null : Environments.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    public bool CanCompare =>
        SelectedSource is not null && SelectedTarget is not null && !SameGateway(SelectedSource, SelectedTarget);

    /// <summary>Empty-state prompt when both picks resolve to the same F&amp;O environment.</summary>
    public bool ShowSamePrompt =>
        SelectedSource is not null && SelectedTarget is not null && SameGateway(SelectedSource, SelectedTarget);

    // Two picks are the same environment when they resolve to the same gateway host — NOT when they are the
    // same record. Distinct profiles for one environment are normal (an interactive profile and an SPN
    // profile for the same F&O host), and both sides then connect the same gateway and resolve the same cid:
    // every row comes back Identical and the screen reads as "these two environments are in sync". A profile
    // with no URL has no resolvable gateway, so it can't be compared against anything (including another
    // URL-less profile) — the same guard covers that.
    private static bool SameGateway(EnvProfile? a, EnvProfile? b) =>
        string.Equals(GatewayHost(a), GatewayHost(b), StringComparison.Ordinal);

    // Normalized host for an F&O environment URL. Profile URLs are entered either bare
    // ("contoso.operations.dynamics.com") or with a scheme (and sometimes a trailing slash/path), so both
    // forms must normalize to the same host before they're compared.
    private static string GatewayHost(EnvProfile? env)
    {
        var trimmed = env?.Url?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return string.Empty;
        }

        var lowered = trimmed.ToLowerInvariant();
        var candidate = lowered.Contains("://", StringComparison.Ordinal) ? lowered : "https://" + lowered;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : lowered;
    }

    private bool CanRunCompare() => CanCompare && !IsBusy;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunCompare))]
    private async Task Compare(CancellationToken ct)
    {
        if (SelectedSource is null || SelectedTarget is null)
        {
            return;
        }

        IsBusy = true;
        Error = null;
        try
        {
            var rows = await _service.CompareAsync(SelectedSource, SelectedTarget, ct);

            DiffRows.Clear();
            foreach (var row in rows)
            {
                DiffRows.Add(row);
            }

            Summary.Clear();
            foreach (var bucket in Buckets(rows))
            {
                Summary.Add(bucket);
            }

            HasResult = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled — leave the prior result (if any) untouched.
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            HasResult = false;
            DiffRows.Clear();
            Summary.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // Counts per verdict, in the enum's canonical order, omitting empty buckets.
    private static IEnumerable<DiffBucket> Buckets(IReadOnlyList<DualWriteMapComparisonRow> rows) =>
        rows.GroupBy(r => r.Verdict)
            .Select(g => new DiffBucket(g.Key, g.Count()))
            .OrderBy(b => b.Verdict);
}
