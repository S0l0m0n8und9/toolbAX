using System;
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

/// <summary>
/// Dual-Write Map Browser (control-map §4): a read-only inspector over the <c>msdyn_dualwriteentitymap</c>
/// records in Dataverse. The master list + search drive a selected map; the detail pane shows the parsed
/// <c>msdyn_mapping</c> (summary, legs, field mappings, value transforms) and <c>msdyn_properties</c>.
/// No mutations — acting on a map is the Operations screen's job. Maps load on first view (Initialize)
/// and can be reloaded (Refresh). A load/auth failure surfaces in <see cref="LoadError"/> rather than a
/// silently blank list.
/// </summary>
public partial class DualWriteMapViewModel : ObservableObject
{
    private readonly IDualWriteMapReader _reader;
    private bool _loaded;

    public ObservableCollection<DwMapRecord> Maps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    // Bound to the ListBox. Filtering can null this out when the selected item leaves the result set;
    // DetailMap (below) is what actually drives the detail pane so the panel doesn't get wiped.
    [ObservableProperty]
    private DwMapRecord? _selectedMap;

    // The map whose detail is shown. Only ever advanced by a real (non-null) selection, so a search
    // that hides the current row leaves the detail intact.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(ShowSelectPrompt))]
    private DwMapRecord? _detailMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowSelectPrompt))]
    private bool _isLoading;

    // Surfaces a Dataverse load/auth failure so the view shows it instead of a silently blank list.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    [NotifyPropertyChangedFor(nameof(ShowSelectPrompt))]
    private string _loadError = string.Empty;

    public DualWriteMapViewModel(IDualWriteMapReader reader) => _reader = reader;

    public IEnumerable<DwMapRecord> Filtered =>
        string.IsNullOrWhiteSpace(Search) ? Maps : Maps.Where(Matches);

    private bool Matches(DwMapRecord m)
    {
        var s = Search;
        return m.Title.Contains(s, StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains(s, StringComparison.OrdinalIgnoreCase)
            || m.PrimarySource.Contains(s, StringComparison.OrdinalIgnoreCase)
            || m.PrimaryDestination.Contains(s, StringComparison.OrdinalIgnoreCase)
            || m.State.Contains(s, StringComparison.OrdinalIgnoreCase);
    }

    public bool HasMaps => Maps.Count > 0;

    public bool HasLoadError => !string.IsNullOrEmpty(LoadError);

    public bool HasSelection => DetailMap is not null;

    /// <summary>Shown only after a successful load that returned nothing (not while loading or on error).</summary>
    public bool ShowEmptyState => _loaded && !IsLoading && !HasLoadError && Maps.Count == 0;

    /// <summary>"Select a map" prompt — only when there's nothing else to show (no selection/error/empty/loading).</summary>
    public bool ShowSelectPrompt => !HasSelection && !HasLoadError && !ShowEmptyState && !IsLoading;

    // Loads the catalogue when the view first appears; the cached VM only reloads on explicit Refresh,
    // so re-navigating is cheap. With the fake reader this resolves synchronously over seeded data.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await LoadAsync(ct);
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private Task Refresh(CancellationToken ct) => LoadAsync(ct);

    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        try
        {
            var result = await _reader.GetMapsAsync(ct);

            if (result.IsSuccess)
            {
                // Preserve the inspected map across a refresh when it's still present (by id), so a
                // reload doesn't yank the user back to the first row.
                var previousId = DetailMap?.Id;

                Maps.Clear();
                foreach (var map in result.Maps)
                {
                    Maps.Add(map);
                }

                LoadError = string.Empty;
                _loaded = true;
                OnPropertyChanged(nameof(Filtered));
                OnPropertyChanged(nameof(HasMaps));

                DetailMap = (previousId is not null ? Maps.FirstOrDefault(m => m.Id == previousId) : null)
                    ?? Maps.FirstOrDefault();
                SelectedMap = DetailMap;
            }
            else
            {
                // A failed load (e.g. expired token) keeps the stale-but-useful catalogue + selection
                // and just shows the error banner, rather than wiping the list.
                LoadError = result.Error ?? "Couldn't load dual-write maps.";
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled refresh leaves the current list + selection intact.
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedMapChanged(DwMapRecord? value)
    {
        // Ignore the null the ListBox emits when filtering hides the current row — keep the detail.
        if (value is not null)
        {
            DetailMap = value;
        }
    }
}
