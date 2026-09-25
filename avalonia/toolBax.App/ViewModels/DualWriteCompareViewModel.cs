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
        DualWriteComparisonVerdict.Identical => "reported values match",
        DualWriteComparisonVerdict.OnlyInLeft => "only in source",
        DualWriteComparisonVerdict.OnlyInRight => "only in target",
        DualWriteComparisonVerdict.VersionMismatch => "version mismatch",
        DualWriteComparisonVerdict.StateMismatch => "state mismatch",
        // #160: the row could not be paired — two maps in one environment share a name + CE target, a map
        // has neither, or one gateway omitted the CE target and no unique match exists. No verdict to show.
        DualWriteComparisonVerdict.Ambiguous => "cannot compare",
        DualWriteComparisonVerdict.Unknown => "unknown",
        _ => verdict.ToString(),
    };
}

/// <summary>A diff bucket summary chip: a count of maps with a given verdict.</summary>
public sealed record DiffBucket(DualWriteComparisonVerdict Verdict, int Count)
{
    public string Label => $"{Count} {CompareVerdict.Label(Verdict)}";
}

/// <summary>Immutable attribution for the profiles that produced the currently displayed result.</summary>
public sealed record CompareResultContext(
    string SourceName,
    string SourceUrl,
    string TargetName,
    string TargetUrl,
    DateTimeOffset CompletedAt)
{
    public string Summary =>
        $"{SourceName} ({SourceUrl}) → {TargetName} ({TargetUrl}) · completed {CompletedAt:yyyy-MM-dd HH:mm:ss zzz}";
}

/// <summary>
/// Dual-Write Compare (control-map §5): pick a source and target environment, compare their dual-write
/// maps (connect each gateway → load maps → diff via the Core <see cref="DualWriteMapComparer"/>), and
/// show a per-map diff grid + verdict summary chips. Compare is enabled only when the two picks resolve
/// to different F&amp;O environments.
/// </summary>
public partial class DualWriteCompareViewModel : ObservableObject, IDisposable
{
    private readonly IProfileStore _store;
    private readonly IDualWriteCompareService _service;
    private bool _disposed;
    private int _selectionGeneration;
    private int _operationLeaseHeld;
    private int _ownerSequence;
    private int _activeOwner;
    private CancellationTokenSource? _activeCompareCts;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private bool _batchingSelectionChanges;

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
    [NotifyPropertyChangedFor(nameof(ShowEmptyResult))]
    [NotifyPropertyChangedFor(nameof(ShowDiffGrid))]
    private bool _hasResult;

    /// <summary>
    /// How many map rows the last compare returned, including incomplete and unpaired evidence. An empty
    /// result used to render exactly like a compare with no differences — no grid rows, chips or count.
    /// The count is the one thing that separates them, so it is always on screen with a result.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComparedSummary))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyResult))]
    [NotifyPropertyChangedFor(nameof(ShowDiffGrid))]
    private int _comparedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private bool _isBusy;

    /// <summary>A compare failure (e.g. a gateway connect error) for the error banner; null when fine.</summary>
    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private CompareResultContext? _resultContext;

    public DualWriteCompareViewModel(IProfileStore store, IDualWriteCompareService service)
    {
        _store = store;
        _service = service;
        Environments = new ObservableCollection<EnvProfile>();
        RefreshEnvironments();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _selectionGeneration);
        ClearResult();
        Error = null;
        _lifetimeCts.Cancel();
        _activeCompareCts?.Cancel();
        _lifetimeCts.Dispose();
    }

    /// <summary>
    /// Re-reads the profile store into the pickers. Fired on every view activation (not just at
    /// construction): this VM is cached by the shell and <see cref="EnvProfile"/> is an immutable record
    /// that the Profiles screen REPLACES on save, so a construction-time snapshot would keep comparing a
    /// stale copy — a URL corrected in Profiles would never reach the compare, and added/deleted profiles
    /// would only appear after an app restart. Selections are preserved by <see cref="EnvProfile.Id"/> and
    /// rebound to the new record instances so their URL/tenant are current; a selection whose profile is
    /// gone falls back to the defaults. Selection assignment is batched so transient nulls never invalidate
    /// an otherwise unchanged result; complete identities are compared once after the rebind.
    /// </summary>
    [RelayCommand]
    private void RefreshEnvironments()
    {
        if (_disposed)
        {
            return;
        }
        var oldSource = SelectedSource;
        var oldTarget = SelectedTarget;
        var sourceId = oldSource?.Id;
        var targetId = oldTarget?.Id;

        _batchingSelectionChanges = true;
        try
        {
            Environments.Clear();
            foreach (var env in _store.GetAll())
            {
                Environments.Add(env);
            }

            // Rebind by exact id (the record instance changed), else fall back to the default picks.
            SelectedSource = ById(sourceId) ?? Environments.FirstOrDefault();
            SelectedTarget = ById(targetId)
                ?? Environments.Skip(1).FirstOrDefault()
                ?? Environments.FirstOrDefault();
        }
        finally
        {
            _batchingSelectionChanges = false;
        }

        if (!SameIdentity(oldSource, SelectedSource) || !SameIdentity(oldTarget, SelectedTarget))
        {
            InvalidateSelectionScope();
        }
    }

    partial void OnSelectedSourceChanged(EnvProfile? oldValue, EnvProfile? newValue) =>
        OnSelectionChanged(oldValue, newValue);

    partial void OnSelectedTargetChanged(EnvProfile? oldValue, EnvProfile? newValue) =>
        OnSelectionChanged(oldValue, newValue);

    private void OnSelectionChanged(EnvProfile? oldValue, EnvProfile? newValue)
    {
        if (!_batchingSelectionChanges && !SameIdentity(oldValue, newValue))
        {
            InvalidateSelectionScope();
        }
    }

    private static bool SameIdentity(EnvProfile? left, EnvProfile? right) =>
        Equals(EnvironmentIdentity.TryCreate(left), EnvironmentIdentity.TryCreate(right));

    private void InvalidateSelectionScope()
    {
        Interlocked.Increment(ref _selectionGeneration);
        ClearResult();
        Error = null;
        _activeCompareCts?.Cancel();
    }

    private void ClearResult()
    {
        HasResult = false;
        ComparedCount = 0;
        DiffRows.Clear();
        Summary.Clear();
        ResultContext = null;
    }

    private EnvProfile? ById(string? id) =>
        id is null ? null : Environments.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    // Both picks must resolve to a gateway host of their own AND to two DIFFERENT hosts. The non-empty
    // requirement is not implied by the same-host guard: a URL-less profile resolves to an empty host, so
    // pairing it with a configured one yields two different hosts and would enable a compare that can only
    // fail at connect time. There is nothing to connect to, so the button stays disabled.
    public bool CanCompare =>
        SelectedSource is not null && SelectedTarget is not null
        && !string.IsNullOrEmpty(GatewayHost(SelectedSource))
        && !string.IsNullOrEmpty(GatewayHost(SelectedTarget))
        && !SameGateway(SelectedSource, SelectedTarget);

    /// <summary>Result-scale caption counts rows, without claiming every row could be compared.</summary>
    public string ComparedSummary =>
        ComparedCount == 1 ? "1 map row" : $"{ComparedCount} map rows";

    /// <summary>
    /// A compare that completed but had nothing to compare — neither gateway returned a usable map. Not a
    /// failure (no exception, so no error banner) and emphatically not parity, which is what a bare empty
    /// grid implied. It gets its own message instead.
    /// </summary>
    public bool ShowEmptyResult => HasResult && ComparedCount == 0;

    /// <summary>The diff grid, hidden for an empty result so its bare headers can't read as "all in sync".</summary>
    public bool ShowDiffGrid => HasResult && ComparedCount > 0;

    /// <summary>Empty-state prompt when both picks resolve to the same F&amp;O environment.</summary>
    public bool ShowSamePrompt =>
        SelectedSource is not null && SelectedTarget is not null && SameGateway(SelectedSource, SelectedTarget);

    // Two picks are the same environment when they resolve to the same gateway host — NOT when they are the
    // same record. Distinct profiles for one environment are normal (an interactive profile and an SPN
    // profile for the same F&O host), and both sides then connect the same gateway and resolve the same cid:
    // every row comes back Identical and the screen reads as "these two environments are in sync". Two
    // URL-less profiles both resolve to an empty host, so this reads them as "same" and the screen shows the
    // same-environment prompt; a URL-less profile paired with a configured one is NOT "same" (the hosts
    // differ) — that case is blocked by CanCompare's explicit non-empty-host requirement instead.
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

    private bool CanRunCompare() =>
        !_disposed && CanCompare && !IsBusy && Volatile.Read(ref _operationLeaseHeld) == 0;

    [RelayCommand(CanExecute = nameof(CanRunCompare))]
    private async Task Compare()
    {
        // ICommand.CanExecute is a UI affordance; direct ExecuteAsync callers still pass through here.
        if (_disposed || !CanCompare || !TryBeginCompare(out var owner, out var operationCts))
        {
            return;
        }

        var source = SelectedSource;
        var target = SelectedTarget;
        var generation = Volatile.Read(ref _selectionGeneration);
        var sourceIdentity = EnvironmentIdentity.TryCreate(source);
        var targetIdentity = EnvironmentIdentity.TryCreate(target);
        try
        {
            if (source is null || target is null || sourceIdentity is null || targetIdentity is null) return;

            if (!StoreMatches(source.Id, sourceIdentity) || !StoreMatches(target.Id, targetIdentity))
            {
                RefreshEnvironments();
                if (!_disposed) Error = "Environment selections changed. Check the selections and run Compare again.";
                return;
            }

            Error = null;
            var rows = await _service.CompareAsync(source, target, operationCts.Token);
            if (!CanCommit(owner, generation, source.Id, sourceIdentity, target.Id, targetIdentity, operationCts))
            {
                if (!_disposed && (!StoreMatches(source.Id, sourceIdentity) || !StoreMatches(target.Id, targetIdentity)))
                {
                    RefreshEnvironments();
                    Error = "Environment selections changed. Check the selections and run Compare again.";
                }
                return;
            }

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

            ComparedCount = rows.Count;
            ResultContext = new CompareResultContext(
                source.Name, source.Url, target.Name, target.Url, DateTimeOffset.Now);
            HasResult = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled — leave the prior result (if any) untouched.
        }
        catch (Exception ex)
        {
            if (!IsOwnerCurrent(owner) || operationCts.IsCancellationRequested || _disposed) return;
            if (source is null || target is null || sourceIdentity is null || targetIdentity is null
                || generation != Volatile.Read(ref _selectionGeneration)
                || !SelectionMatches(SelectedSource, source.Id, sourceIdentity)
                || !SelectionMatches(SelectedTarget, target.Id, targetIdentity))
            {
                return;
            }
            if (!StoreMatches(source.Id, sourceIdentity) || !StoreMatches(target.Id, targetIdentity))
            {
                RefreshEnvironments();
                Error = "Environment selections changed. Check the selections and run Compare again.";
                return;
            }
            Error = ex.Message;
            ClearResult();
        }
        finally
        {
            EndCompare(owner, operationCts);
        }
    }

    [RelayCommand]
    private void CompareCancel() => _activeCompareCts?.Cancel();

    private bool TryBeginCompare(out int owner, out CancellationTokenSource operationCts)
    {
        owner = 0;
        operationCts = null!;
        if (_disposed || Interlocked.CompareExchange(ref _operationLeaseHeld, 1, 0) != 0) return false;
        if (_disposed || IsBusy)
        {
            Volatile.Write(ref _operationLeaseHeld, 0);
            return false;
        }

        owner = Interlocked.Increment(ref _ownerSequence);
        operationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        Volatile.Write(ref _activeOwner, owner);
        _activeCompareCts = operationCts;
        IsBusy = true;
        return true;
    }

    private bool CanCommit(int owner, int generation, string sourceId, EnvironmentIdentity sourceIdentity,
        string targetId, EnvironmentIdentity targetIdentity, CancellationTokenSource operationCts) =>
        IsOwnerCurrent(owner) && !_disposed && !operationCts.IsCancellationRequested
        && generation == Volatile.Read(ref _selectionGeneration)
        && SelectionMatches(SelectedSource, sourceId, sourceIdentity)
        && SelectionMatches(SelectedTarget, targetId, targetIdentity)
        && StoreMatches(sourceId, sourceIdentity)
        && StoreMatches(targetId, targetIdentity);

    private static bool SelectionMatches(EnvProfile? profile, string id, EnvironmentIdentity identity) =>
        profile is not null && string.Equals(profile.Id, id, StringComparison.Ordinal)
        && Equals(EnvironmentIdentity.Create(profile), identity);

    private bool StoreMatches(string id, EnvironmentIdentity identity)
    {
        var current = _store.GetAll().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        return current is not null && Equals(EnvironmentIdentity.Create(current), identity);
    }

    private bool IsOwnerCurrent(int owner) => Volatile.Read(ref _activeOwner) == owner;

    private void EndCompare(int owner, CancellationTokenSource operationCts)
    {
        if (Interlocked.CompareExchange(ref _activeOwner, 0, owner) != owner)
        {
            operationCts.Dispose();
            return;
        }

        _activeCompareCts = null;
        operationCts.Dispose();
        Volatile.Write(ref _operationLeaseHeld, 0);
        IsBusy = false;
    }

    // Counts per verdict, in the enum's canonical order, omitting empty buckets.
    private static IEnumerable<DiffBucket> Buckets(IReadOnlyList<DualWriteMapComparisonRow> rows) =>
        rows.GroupBy(r => r.Verdict)
            .Select(g => new DiffBucket(g.Key, g.Count()))
            .OrderBy(b => b.Verdict);
}
