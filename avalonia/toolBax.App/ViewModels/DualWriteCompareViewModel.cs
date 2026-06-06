using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>A diff bucket summary chip: a count of maps with a given <see cref="DiffKind"/>.</summary>
public sealed record DiffBucket(DiffKind Kind, int Count)
{
    public string Label => $"{Count} {DiffClassifier.Label(Kind)}";
}

/// <summary>
/// Dual-Write Compare (control-map §5): pick a source and target environment, compare their maps,
/// and show a per-map diff grid + summary chips. Compare is enabled only when the two differ.
/// </summary>
public partial class DualWriteCompareViewModel : ObservableObject
{
    private readonly IDualWriteCompareService _service;

    public ObservableCollection<EnvProfile> Environments { get; }
    public ObservableCollection<CompareRow> DiffRows { get; } = new();
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
    private bool _isBusy;

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

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task Compare(CancellationToken ct)
    {
        if (SelectedSource is null || SelectedTarget is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var rows = await _service.CompareAsync(SelectedSource.Id, SelectedTarget.Id, ct);

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
        finally
        {
            IsBusy = false;
        }
    }

    // Counts per diff kind, in the canonical bucket order, omitting empty buckets.
    private static IEnumerable<DiffBucket> Buckets(IReadOnlyList<CompareRow> rows) =>
        rows.GroupBy(r => r.Diff)
            .Select(g => new DiffBucket(g.Key, g.Count()))
            .OrderBy(b => b.Kind);
}
