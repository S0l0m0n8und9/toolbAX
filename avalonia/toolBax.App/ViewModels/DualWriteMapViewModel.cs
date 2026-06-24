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

/// <summary>
/// Dual-Write Map Browser (control-map §4): a read-only inspector over the <c>msdyn_dualwriteentitymap</c>
/// records in Dataverse. The master list + search drive a selected map; the detail pane shows the parsed
/// <c>msdyn_mapping</c> (summary, legs, field mappings, value transforms) and <c>msdyn_properties</c>.
/// A solution picker (optionally narrowed by publisher) filters the catalogue to one solution's maps.
/// No mutations — acting on a map is the Operations screen's job. Maps load on first view (Initialize)
/// and reload on filter change / Refresh. A load/auth failure surfaces in <see cref="LoadError"/>.
/// </summary>
public partial class DualWriteMapViewModel : ObservableObject
{
    private readonly IDualWriteMapReader _reader;
    private readonly IFileSaveService _fileSave;
    private readonly IODataClient _odata;
    private readonly IMetadataService _metadata;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly IClipboardService _clipboard;
    private readonly IUrlLauncher _launcher;
    private IReadOnlyList<string> _foEntityNames = Array.Empty<string>();
    private bool _loaded;
    private bool _suppressReload;          // guards the initial selection setup from triggering reloads
    private int _activeLoads;              // overlapping reloads in flight; the last to finish clears IsLoading
    private List<DwSolution> _allSolutions = new();

    public ObservableCollection<DwMapRecord> Maps { get; } = new();

    /// <summary>Per-leg Dataverse (CE) row-count rows for the inspected map (filled on demand).</summary>
    public ObservableCollection<MapLegCountRow> CountRows { get; } = new();

    /// <summary>Solutions for the picker (an "All" sentinel first, then the publisher-filtered list).</summary>
    public ObservableCollection<DwSolution> Solutions { get; } = new();

    /// <summary>Publishers for the secondary filter ("All" first, then distinct publishers with counts).</summary>
    public ObservableCollection<DwPublisher> Publishers { get; } = new();

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
    [NotifyPropertyChangedFor(nameof(MapRecordUrl))]
    [NotifyPropertyChangedFor(nameof(HasMapLink))]
    [NotifyPropertyChangedFor(nameof(MapLinkUnavailableReason))]
    [NotifyCanExecuteChangedFor(nameof(ExportMarkdownCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenMapLinkCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyMapLinkCommand))]
    private DwMapRecord? _detailMap;

    /// <summary>Outcome of the last Markdown export (empty until one is attempted).</summary>
    [ObservableProperty]
    private string _exportStatus = string.Empty;

    [ObservableProperty]
    private DwSolution? _selectedSolution;

    [ObservableProperty]
    private DwPublisher? _selectedPublisher;

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

    public DualWriteMapViewModel(IDualWriteMapReader reader, IFileSaveService? fileSave = null,
        IODataClient? odata = null, IMetadataService? metadata = null,
        Func<EnvProfile?>? activeEnv = null, IClipboardService? clipboard = null, IUrlLauncher? launcher = null)
    {
        _reader = reader;
        _fileSave = fileSave ?? new FakeFileSaveService();
        _odata = odata ?? new FakeODataClient();
        _metadata = metadata ?? new FakeMetadataService();
        _activeEnv = activeEnv ?? (() => null);
        _clipboard = clipboard ?? new FakeClipboardService();
        _launcher = launcher ?? new FakeUrlLauncher();
    }

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

    /// <summary>"Select a map" prompt — only when there's nothing else to show.</summary>
    public bool ShowSelectPrompt => !HasSelection && !HasLoadError && !ShowEmptyState && !IsLoading;

    // Loads the catalogue (solutions + maps) when the view first appears; the cached VM only reloads on
    // an explicit Refresh or a filter change, so re-navigating is cheap.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        if (_loaded)
        {
            return;
        }

        await LoadSolutionsAsync(ct);
        await LoadFoEntityNamesAsync(ct);
        await LoadMapsAsync(ct);
    }

    private async Task LoadFoEntityNamesAsync(CancellationToken ct)
    {
        // Best-effort: the F&O entity catalogue only sharpens the auto-guessed count entity. If it can't
        // be loaded (e.g. no F&O auth while Dataverse works), the Row counts tab still works with the
        // simple fallback guess + manual edit, so a failure here is non-fatal.
        try
        {
            await _metadata.LoadEntitiesAsync(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // keep whatever entity names are already cached
        }

        _foEntityNames = _metadata.GetEntities().Select(e => e.Name).ToList();
    }

    // Reloads the maps for the current solution filter. Triggered by Refresh and by filter changes;
    // concurrent + cancellable so a newer filter selection isn't gated by an in-flight load.
    [RelayCommand(IncludeCancelCommand = true, AllowConcurrentExecutions = true)]
    private async Task ReloadMaps(CancellationToken ct) => await LoadMapsAsync(ct);

    // Exports the inspected map to a Markdown file (the screen's one "write" — to disk, not Dataverse).
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ExportMarkdown(CancellationToken ct)
    {
        var map = DetailMap;
        if (map is null)
        {
            return;
        }

        var markdown = DualWriteMapMarkdownExporter.Export(map);
        var fileName = DualWriteMapMarkdownExporter.SuggestedFileName(map);
        var path = await _fileSave.SaveTextAsync(fileName, markdown, ct);
        ExportStatus = path is null ? "Export cancelled." : $"Exported to {path}";
    }

    /// <summary>
    /// Deterministic deep link to the inspected map's <c>msdyn_dualwriteentitymap</c> record in the
    /// model-driven app, or null when it can't be built (no Dataverse URL on the active environment, or
    /// no/invalid map id).
    /// </summary>
    public string? MapRecordUrl =>
        DualWriteMapLink.BuildMapRecordUrl(_activeEnv()?.DataverseUrl, DetailMap?.Id);

    /// <summary>True when the inspected map has an openable/copyable Dataverse record link.</summary>
    public bool HasMapLink => MapRecordUrl is not null;

    /// <summary>When a map is selected but no link can be built, explains which input is missing.</summary>
    public string MapLinkUnavailableReason =>
        DetailMap is null || HasMapLink
            ? string.Empty
            : string.IsNullOrWhiteSpace(_activeEnv()?.DataverseUrl)
                ? "No Dataverse URL is configured for this environment."
                : "This map has no Dataverse record id.";

    // Opens the inspected map's Dataverse record in the browser — the native dual-write map config page.
    [RelayCommand(CanExecute = nameof(HasMapLink))]
    private async Task OpenMapLink() => await _launcher.OpenAsync(MapRecordUrl);

    // Copies the record link so it's visible/shareable for troubleshooting.
    [RelayCommand(CanExecute = nameof(HasMapLink))]
    private async Task CopyMapLink()
    {
        if (MapRecordUrl is { } url)
        {
            await _clipboard.SetTextAsync(url);
        }
    }

    private async Task LoadSolutionsAsync(CancellationToken ct)
    {
        var result = await _reader.GetSolutionsAsync(ct);
        // A solutions failure shouldn't block the maps; just leave the picker with only "All".
        _allSolutions = result.IsSuccess ? result.Solutions.ToList() : new List<DwSolution>();

        _suppressReload = true;
        RebuildPublishers();
        SelectedPublisher = Publishers.FirstOrDefault();
        RebuildSolutions();
        SelectedSolution = Solutions.FirstOrDefault();
        _suppressReload = false;
    }

    private async Task LoadMapsAsync(CancellationToken ct)
    {
        var solutionName = CurrentSolutionFilter();
        _activeLoads++;
        IsLoading = true;
        try
        {
            var result = await _reader.GetMapsAsync(solutionName, ct);

            // Drop a stale result if the solution filter moved on while this load was in flight.
            if (CurrentSolutionFilter() != solutionName)
            {
                return;
            }

            if (result.IsSuccess)
            {
                // Preserve the inspected map across a reload when it's still present (by id).
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
                // A failed load keeps the stale-but-useful catalogue + selection and shows the banner.
                LoadError = result.Error ?? "Couldn't load dual-write maps.";
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled reload leaves the current list + selection intact.
        }
        finally
        {
            // Only the last overlapping load clears the indicator, so a cancelled/stale load finishing
            // first doesn't switch it off while a newer load is still running.
            if (--_activeLoads == 0)
            {
                IsLoading = false;
            }
        }
    }

    private string? CurrentSolutionFilter() =>
        SelectedSolution is { IsAll: false } solution ? solution.UniqueName : null;

    private void RebuildPublishers()
    {
        Publishers.Clear();
        Publishers.Add(DwPublisher.All);
        foreach (var publisher in _allSolutions
                     .Where(s => !string.IsNullOrWhiteSpace(s.PublisherUniqueName))
                     .GroupBy(s => s.PublisherUniqueName, StringComparer.OrdinalIgnoreCase)
                     .Select(g => new DwPublisher(g.Key, StablePublisherName(g), g.Count()))
                     .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            Publishers.Add(publisher);
        }
    }

    // Deterministic display name for a publisher whose solutions might carry slightly different
    // friendlyname casings/values — the alphabetically-first non-empty one, falling back to the key.
    private static string StablePublisherName(IEnumerable<DwSolution> group) =>
        group.Select(s => s.PublisherDisplayName)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .OrderBy(d => d, StringComparer.Ordinal)
            .FirstOrDefault()
        ?? group.First().PublisherUniqueName;

    private void RebuildSolutions()
    {
        Solutions.Clear();
        Solutions.Add(DwSolution.All);

        var publisher = SelectedPublisher;
        IEnumerable<DwSolution> visible = _allSolutions;
        if (publisher is { IsAll: false })
        {
            visible = visible.Where(s =>
                string.Equals(s.PublisherUniqueName, publisher.UniqueName, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var solution in visible
                     .OrderBy(s => s.FriendlyName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(s => s.UniqueName, StringComparer.OrdinalIgnoreCase))
        {
            Solutions.Add(solution);
        }
    }

    partial void OnSelectedPublisherChanged(DwPublisher? value)
    {
        if (_suppressReload)
        {
            return;
        }

        var previous = SelectedSolution;

        // Suppress while we rebuild the list + restore the selection, so the transient null the bound
        // picker emits when its items are cleared doesn't fire an intermediate reload.
        _suppressReload = true;
        RebuildSolutions();
        var restored = previous is not null && Solutions.Contains(previous) ? previous : Solutions.FirstOrDefault();
        SelectedSolution = restored;
        _suppressReload = false;

        // Reload only if the effective solution filter actually changed (e.g. the prior solution was
        // hidden by the new publisher and fell back to "All").
        if (!Equals(previous, restored))
        {
            ReloadMapsCommand.Cancel();
            ReloadMapsCommand.Execute(null);
        }
    }

    partial void OnSelectedSolutionChanged(DwSolution? value)
    {
        if (_suppressReload)
        {
            return;
        }

        ReloadMapsCommand.Cancel();
        ReloadMapsCommand.Execute(null);
    }

    partial void OnSelectedMapChanged(DwMapRecord? value)
    {
        // Ignore the null the ListBox emits when filtering hides the current row — keep the detail.
        if (value is not null)
        {
            DetailMap = value;
        }
    }

    partial void OnDetailMapChanged(DwMapRecord? value)
    {
        // A stale "Exported to …" message shouldn't linger once a different map is inspected.
        ExportStatus = string.Empty;

        // Stop any in-flight count before mutating the collection it iterates, then rebuild the
        // (un-counted) row-count rows for the newly inspected map.
        CountAllRowsCommand.Cancel();
        CountRows.Clear();
        if (value is not null)
        {
            foreach (var leg in value.Legs)
            {
                var resolved = DualWriteFoEntityResolver.Resolve(leg.SourceSchema, leg.SourceSchemaDistinctName, _foEntityNames);
                CountRows.Add(new MapLegCountRow(leg, resolved));
            }
        }
    }

    // Counts the F&O and Dataverse (CE) rows for each leg (applying the leg's source / reversed-source
    // filters) and compares them. Concurrent-safe via the cancel command.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CountAllRows(CancellationToken ct)
    {
        // Snapshot before any await: a map change rebuilds CountRows on the UI thread, which would
        // otherwise invalidate a live enumerator mid-iteration.
        var rows = CountRows.ToList();
        try
        {
            foreach (var row in rows)
            {
                await CountCeAsync(row, ct);
                await CountFoAsync(row, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Clear any "Counting…" placeholders left on rows that hadn't finished.
            foreach (var row in rows)
            {
                if (row.CeStatus == "Counting…")
                {
                    row.CeStatus = string.Empty;
                }

                if (row.FoStatus == "Counting…")
                {
                    row.FoStatus = string.Empty;
                }
            }
        }
    }

    private async Task CountCeAsync(MapLegCountRow row, CancellationToken ct)
    {
        row.CeStatus = "Counting…";
        var filter = string.IsNullOrWhiteSpace(row.CeFilter) ? null : row.CeFilter;
        var result = await _reader.GetCeRowCountAsync(row.DestinationSchema, filter, ct);
        if (result.IsSuccess)
        {
            row.CeCount = result.Count;
            row.CeStatus = string.Empty;
        }
        else
        {
            row.CeStatus = result.Error ?? "Count failed.";
        }
    }

    private async Task CountFoAsync(MapLegCountRow row, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(row.FoEntity))
        {
            row.FoStatus = "Set an F&O entity to count.";
            return;
        }

        row.FoStatus = "Counting…";
        var filter = string.IsNullOrWhiteSpace(row.FoFilter) ? null : row.FoFilter;
        var response = await _odata.SendAsync("GET", DualWriteMapParser.FoCountPath(row.FoEntity, filter), null, ct);
        if (!response.IsSuccess)
        {
            row.FoStatus = $"{response.StatusCode} {response.ReasonPhrase}";
            return;
        }

        var count = DualWriteMapParser.ParseCount(response.Body);
        if (count is null)
        {
            row.FoStatus = "F&O returned no count.";
            return;
        }

        row.FoCount = count;
        row.FoStatus = string.Empty;
    }
}
