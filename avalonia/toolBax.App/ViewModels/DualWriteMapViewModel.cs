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
    // The environment the currently-displayed maps were loaded from. The shell can switch the active
    // environment under this cached VM (the "Refresh open tools?" prompt is declinable), and the count
    // clients resolve the ACTIVE environment at call time — so counting after a switch would fill
    // environment A's maps with environment B's numbers. Re-stamped by each successful load.
    private string? _loadedEnvId;
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

    /// <summary>
    /// Outcome of the last export / copy-link / open-link attempt on the inspected map — the line the view
    /// renders beside those three buttons. Empty until one is attempted, and cleared when the selection
    /// changes so a stale message can't be read as belonging to a different map.
    /// </summary>
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

        try
        {
            await LoadSolutionsAsync(ct);
            await LoadFoEntityNamesAsync(ct);
            await LoadMapsAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // The view fires InitializeCommand on every Loaded and AsyncRelayCommand cancels the previous
            // token, so navigate-away-and-back cancels an in-flight load: a NORMAL outcome, not an error.
        }
        catch (Exception ex)
        {
            LoadError = $"Couldn't load the dual-write catalogue: {ex.Message}";
        }
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
        try
        {
            var path = await _fileSave.SaveTextAsync(fileName, markdown, SaveFileType.Markdown, ct);
            ExportStatus = path is null ? "Export cancelled." : $"Exported to {path}";
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "Export cancelled.";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
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
    public string MapLinkUnavailableReason
    {
        get
        {
            if (DetailMap is null || HasMapLink)
            {
                return string.Empty;
            }

            // The build fails for exactly one of two reasons. Key off the map id (the unambiguous
            // signal): a valid record id means the Dataverse URL is the culprit — including the case
            // where it's present but host-less (e.g. "/api/data/v9.2"), which normalizes to empty.
            return Guid.TryParse(DetailMap.Id, out _)
                ? "No Dataverse URL is configured for this environment."
                : "This map has no Dataverse record id.";
        }
    }

    // Opens the inspected map's Dataverse record in the browser — the native dual-write map config page.
    [RelayCommand(CanExecute = nameof(HasMapLink))]
    private async Task OpenMapLink()
    {
        try
        {
            await _launcher.OpenAsync(MapRecordUrl);
        }
        catch (Exception ex)
        {
            ExportStatus = $"Couldn't open the link: {ex.Message}";
        }
    }

    // Copies the record link so it's visible/shareable for troubleshooting.
    [RelayCommand(CanExecute = nameof(HasMapLink))]
    private async Task CopyMapLink()
    {
        if (MapRecordUrl is not { } url)
        {
            return;
        }

        try
        {
            await _clipboard.SetTextAsync(url);
            ExportStatus = "Record link copied to the clipboard.";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Couldn't copy to the clipboard: {ex.Message}";
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
        // Captured BEFORE the call, not read again after it: the reader resolves the active environment
        // internally at call time, so a switch landing mid-load would otherwise stamp environment B onto
        // environment A's maps and let the count guard pass on maps the counts can't belong to. This is as
        // atomic as the seam allows without plumbing the environment through the reader API — the residual
        // window is the microseconds between this line and the reader resolving the environment — and it
        // now errs the safe way: a mid-load switch leaves stamp = A while active = B, so counting is
        // blocked until an explicit reload.
        var envId = _activeEnv()?.Id;
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
                // Stamp what these maps belong to — the environment captured before the read, not whatever
                // is active now (a failed load keeps the previous stamp along with the stale-but-useful
                // catalogue, so counting stays blocked until a load actually succeeds).
                _loadedEnvId = envId;
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
        catch (Exception ex)
        {
            // Also the shared body behind ReloadMaps: a reader that throws (rather than returning a failure
            // result) must banner, not fault the command task — that lands on the dispatcher and kills the app.
            LoadError = $"Couldn't load dual-write maps: {ex.Message}";
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

    /// <summary>Message shown when the active environment moved on since the maps were loaded.</summary>
    private const string ReloadBeforeCounting = "Environment changed — reload maps before counting.";

    /// <summary>Status stamped on a count that was abandoned because the environment changed mid-run.</summary>
    private const string CountSkipped = "Skipped — environment changed.";

    // True when the displayed maps came from a different environment than the one now active.
    private bool EnvChangedSinceLoad() =>
        !string.Equals(_activeEnv()?.Id, _loadedEnvId, StringComparison.Ordinal);

    // Counts the F&O and Dataverse (CE) rows for each leg (applying the leg's source / reversed-source
    // filters) and compares them. Concurrent-safe via the cancel command.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task CountAllRows(CancellationToken ct)
    {
        // Checked up front so a run that is already stale never starts, then re-checked before every
        // single request below. Bails before any request so no row is filled with another environment's
        // numbers.
        if (EnvChangedSinceLoad())
        {
            LoadError = ReloadBeforeCounting;
            return;
        }

        // Snapshot before any await: a map change rebuilds CountRows on the UI thread, which would
        // otherwise invalidate a live enumerator mid-iteration.
        var rows = CountRows.ToList();
        try
        {
            foreach (var row in rows)
            {
                // Re-checked immediately before EACH count: the environment can move between rows and even
                // between one row's two legs (they are separate awaits, and _reader / _odata each resolve
                // the active environment at call time). Tripping stops the run instead of letting the rest
                // of the grid fill from a second environment — no count request is ever issued after the
                // active environment diverges from the stamp, and no row shows two environments' numbers.
                if (StopCountIfEnvChanged(row, ceStillPending: true))
                {
                    return;
                }

                await CountCeAsync(row, ct);

                if (StopCountIfEnvChanged(row, ceStillPending: false))
                {
                    return;
                }

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

    // Reports a mid-run environment switch on the row whose count was about to be issued: banners the
    // reload message and marks the side(s) not yet taken as explicitly skipped, so a half-counted row reads
    // as abandoned rather than as a count of zero. Counts already taken keep their numbers — they were
    // consistent with the loaded environment at the moment they were read.
    private bool StopCountIfEnvChanged(MapLegCountRow row, bool ceStillPending)
    {
        if (!EnvChangedSinceLoad())
        {
            return false;
        }

        LoadError = ReloadBeforeCounting;
        if (ceStillPending)
        {
            row.CeStatus = CountSkipped;
        }

        row.FoStatus = CountSkipped;
        return true;
    }

    // Per-leg counts. Only reachable through CountAllRows, which owns the environment gate — any new
    // single-leg entry point must call EnvChangedSinceLoad() first, since _reader/_odata resolve the
    // active environment at call time and would otherwise count a different environment than is displayed.
    private async Task CountCeAsync(MapLegCountRow row, CancellationToken ct)
    {
        row.CeStatus = "Counting…";
        var filter = string.IsNullOrWhiteSpace(row.CeFilter) ? null : row.CeFilter;
        var result = await _reader.GetCeRowCountAsync(row.DestinationSchema, filter, ct);
        if (result.IsSuccess)
        {
            // Set the cap flag first so the count label/verdict never renders an uncapped-looking total
            // for a capped count, not even transiently.
            row.CeCountCapped = result.Capped;
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
            // #204: without an entity the path would be "/data/?…" — the service document, which answers
            // 200 and carries no count at all, so the row silently showed nothing instead of a reason.
            row.FoStatus = "F&O entity not resolved — set one to count.";
            return;
        }

        row.FoStatus = "Counting…";
        var filter = string.IsNullOrWhiteSpace(row.FoFilter) ? null : row.FoFilter;

        // #204: X++ source filters name fields in staging case (ISONETIMECUSTOMER) and F&O's OData property
        // lookup is case-sensitive PascalCase, so the converted filter has to be reconciled with the
        // entity's real property names before the request goes out. #207: a quoted string compared against
        // an enum-typed property is also a 400 (F&O wants the qualified enum literal instead) — the same
        // field metadata fixes both, so FoFilterFieldCaser upgrades that literal in the same pass.
        if (filter is not null)
        {
            var fields = await FoFieldsAsync(row.FoEntity, ct);

            // The environment can move DURING that fetch — an await which both the run's entry guard and
            // the caller's per-leg re-check predate, so neither covers it. Re-checked the instant it
            // returns, before any casing or request: the fetch resolves the ACTIVE environment, so
            // continuing would case this map's filter against a different environment's field names and
            // then count there, stamping environment B's number onto environment A's row.
            if (StopCountIfEnvChanged(row, ceStillPending: false))
            {
                return;
            }

            if (fields is { Count: > 0 })
            {
                var cased = FoFilterFieldCaser.Correct(filter, fields, _metadata.GetEnumMembers);
                if (cased.UnknownFields.Count > 0)
                {
                    // A field the entity doesn't have is a guaranteed 400 whose message the user can't act
                    // on (#159): name the fields instead of firing a known-doomed count.
                    row.FoStatus = $"field(s) not on {row.FoEntity}: {string.Join(", ", cased.UnknownFields)}";
                    return;
                }

                filter = cased.Filter;
            }
        }

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

        // No cap flag on this side: F&O's $count is a true total (the 5,000 ceiling is a Dataverse limit).
        row.FoCount = count.Count;
        row.FoStatus = string.Empty;
    }

    // The F&O fields of one entity, or null when they can't be had (no F&O auth, an entity the environment
    // doesn't have, a fetch failure). Same cache-then-load discipline as
    // EntityCatalogLoader.EnsureFieldsAsync, and a failure is deliberately non-fatal: the count then goes
    // out with whatever the converter produced — better than before #204 for the enum half, never worse.
    // #207: the full fields (not just names) are needed so FoFilterFieldCaser can also type a quoted
    // literal against an enum property's qualified type.
    private async Task<IReadOnlyList<EntityField>?> FoFieldsAsync(string entity, CancellationToken ct)
    {
        if (_metadata.GetFields(entity) is null)
        {
            try
            {
                await _metadata.LoadFieldsAsync(entity, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        return _metadata.GetFields(entity);
    }
}
