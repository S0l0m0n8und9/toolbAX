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
/// show a per-map diff grid + verdict summary chips. Compare is enabled only when the two differ.
/// </summary>
public partial class DualWriteCompareViewModel : ObservableObject
{
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
        _service = service;
        Environments = new ObservableCollection<EnvProfile>(store.GetAll());
        _selectedSource = Environments.FirstOrDefault();
        _selectedTarget = Environments.Skip(1).FirstOrDefault() ?? _selectedSource;
    }

    public bool CanCompare =>
        SelectedSource is not null && SelectedTarget is not null && !ReferenceEquals(SelectedSource, SelectedTarget);

    /// <summary>Empty-state prompt when both picks are the same environment.</summary>
    public bool ShowSamePrompt =>
        SelectedSource is not null && ReferenceEquals(SelectedSource, SelectedTarget);

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
