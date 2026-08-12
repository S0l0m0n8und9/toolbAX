using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Query Builder (control-map §2): pick an entity, toggle $select fields, see the computed OData URL,
/// and run a GET to preview rows. Entity/field metadata comes from <see cref="IMetadataService"/>;
/// rows come from <see cref="IODataClient"/>. Entities without cached fields show a "run once" hint.
/// </summary>
public partial class QueryBuilderViewModel : ObservableObject
{
    private readonly IMetadataService _metadata;
    private readonly EntityCatalogLoader _loader;
    private readonly IODataClient _client;
    private readonly IClipboardService _clipboard;
    private readonly IFileSaveService _fileSave;

    // Hard cap on pages an "export all" will follow, so a misbehaving nextLink can't loop forever.
    private const int MaxExportPages = 500;

    /// <summary>Zero-based index of the Results tab — Fields(0) · Filter(1) · Joins(2) · Results(3).</summary>
    public const int ResultsTabIndex = 3;

    // True only while RefreshEntityFilter is rebuilding FilteredEntities, so the transient selection
    // null a bound ListBox emits during Clear() doesn't run OnSelectedEntityChanged's side-effects
    // (which would wipe the field selection + query URL on every keystroke in the entity search box).
    private bool _refreshingEntities;

    // Set when a new entity is selected and cleared once its fields arrive, so the cross-company default
    // is applied exactly once per selection (company-awareness isn't knowable until the fields load) and a
    // later manual toggle isn't stomped when the same entity's fields are refetched.
    private bool _applyCrossCompanyDefault;

    // Which set of results the screen is currently showing. Bumped by ClearResults (i.e. on every real
    // entity change), captured by Run / LoadMore before they await, and re-checked after — see
    // IsStillCurrent. A monotonic stamp rather than a comparison of the entity name, because switching
    // away and back mid-flight (A→B→A) leaves the name matching while the results have already been
    // invalidated; only a counter discards that too.
    private int _resultsGeneration;

    // True while a bulk field operation (Select all / Clear) flips many chips at once, so each chip's
    // PropertyChanged doesn't rebuild the URL per-field (O(n) churn on entities with hundreds of fields);
    // the URL + labels are refreshed once when the bulk op completes.
    private bool _bulkUpdatingFields;

    public ObservableCollection<EntitySet> Entities { get; }
    public ObservableCollection<FieldChipViewModel> Fields { get; } = new();
    public ObservableCollection<QueryResultRow> ResultRows { get; } = new();

    /// <summary>The selected entity's navigation properties as toggle chips — ticking one adds it to
    /// <c>$expand</c> (a join to the related entity). Empty when the entity has none.</summary>
    public ObservableCollection<FieldChipViewModel> Navigations { get; } = new();

    /// <summary>The navigation chips as shown, after applying <see cref="JoinSearch"/> (the view binds
    /// to this; <see cref="Navigations"/> stays the full master so selections aren't affected).</summary>
    public ObservableCollection<FieldChipViewModel> FilteredNavigations { get; } = new();

    /// <summary>True when the selected entity exposes navigation properties to expand.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinsTabHeader))]
    private bool _hasNavigations;

    /// <summary>Joins are secondary to $select, so the panel is collapsed until the user opens it.</summary>
    [ObservableProperty]
    private bool _isJoinsExpanded;

    /// <summary>Case-insensitive substring filter over the navigation-property names.</summary>
    [ObservableProperty]
    private string _joinSearch = string.Empty;

    /// <summary>"Joins ($expand) · N of M" — the collapsible section's header.</summary>
    public string JoinsHeader =>
        $"Joins ($expand) · {Navigations.Count(n => n.IsSelected)} of {Navigations.Count}";

    /// <summary>The entity list as shown, after applying <see cref="EntitySearch"/>. The view binds
    /// to this; <see cref="Entities"/> stays the full master so selections/loads aren't affected.</summary>
    public ObservableCollection<EntitySet> FilteredEntities { get; } = new();

    /// <summary>The field chips as shown, after applying <see cref="FieldSearch"/>. Filtering only hides
    /// chips from the view — <see cref="Fields"/> keeps every chip (and its $select selection).</summary>
    public ObservableCollection<FieldChipViewModel> FilteredFields { get; } = new();

    /// <summary>Case-insensitive substring filter over the entity-list names.</summary>
    [ObservableProperty]
    private string _entitySearch = string.Empty;

    /// <summary>Case-insensitive substring filter over the field-chip names.</summary>
    [ObservableProperty]
    private string _fieldSearch = string.Empty;

    [ObservableProperty]
    private EntitySet? _selectedEntity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsTabHeader))]
    private bool _hasFields;

    // Surfaces a $metadata load/auth failure so the view shows it instead of a silently blank list.
    [ObservableProperty]
    private string? _loadError;

    [ObservableProperty]
    private string _queryUrl = string.Empty;

    // --- Query options (all feed the computed URL) ---

    /// <summary>Raw OData <c>$filter</c> expression (the user owns the syntax).</summary>
    [ObservableProperty]
    private string _filter = string.Empty;

    /// <summary>OData <c>$orderby</c> clause, e.g. "Name desc".</summary>
    [ObservableProperty]
    private string _orderBy = string.Empty;

    // $top / $skip are free-text (not int) so a non-numeric keystroke is shown verbatim rather than
    // silently dropped by a failed binding conversion; BuildPath parses them (blank/≤0/invalid omits).
    [ObservableProperty]
    private string _top = "50";

    [ObservableProperty]
    private string _skip = string.Empty;

    /// <summary>Include <c>$count=true</c> (total matching rows).</summary>
    [ObservableProperty]
    private bool _count;

    /// <summary>Query across all legal entities (<c>cross-company=true</c>).</summary>
    [ObservableProperty]
    private bool _crossCompany;

    // --- Filter builder (nested AND/OR tree) ---

    // Field/enum metadata the condition rows use to populate dropdowns + pick value editors. Rebuilt
    // whenever the selected entity's fields (re)load.
    private QueryFilterContext _filterContext = new(Array.Empty<EntityField>(), _ => Array.Empty<string>());

    private QueryFilterGroup _filterRoot = null!;

    /// <summary>Root AND/OR group of the visual filter builder (Builder mode).</summary>
    public QueryFilterGroup FilterRoot
    {
        get => _filterRoot;
        private set => SetProperty(ref _filterRoot, value);
    }

    /// <summary>Builder (visual tree) vs Raw ($filter text). Raw overrides the builder when non-empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuilderMode))]
    [NotifyPropertyChangedFor(nameof(FilterTabHeader))]
    private bool _isRawFilterMode;

    public bool IsBuilderMode => !IsRawFilterMode;

    /// <summary>Legal entity used to scope a company-aware query when cross-company is off (dataAreaId).</summary>
    [ObservableProperty]
    private string _company = "usmf";

    /// <summary>
    /// True when the selected entity's loaded fields include a <c>dataAreaId</c> property — the signal that
    /// scoping it to a legal entity is meaningful. Drives the "company-aware" badge and the
    /// <c>dataAreaId</c> clause in <see cref="EffectiveFilter"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT read from <c>EntitySet.CompanyAware</c>: the real catalogue projects the OData
    /// entity <em>index</em>, which carries no field data, so that flag is hardcoded false in production
    /// and every company-scoping path built on it was dead (#161). The fields are already loaded on entity
    /// selection, so gating on them works against a live environment as well as the seeded fake.
    /// </remarks>
    [ObservableProperty]
    private bool _isCompanyAware;

    /// <summary>The builder tree rendered to an OData <c>$filter</c> expression ("" when no conditions).</summary>
    public string BuilderFilter => FilterRoot.Render() ?? string.Empty;

    // Raw text only takes effect in Raw mode (and only when non-blank), mirroring the prototype.
    private bool UsingRawFilter => IsRawFilterMode && !string.IsNullOrWhiteSpace(Filter);

    /// <summary>
    /// The actual <c>$filter</c> that will be sent: the raw text (Raw mode) or the builder expression,
    /// with a <c>dataAreaId eq '{company}'</c> clause prepended for a company-aware entity when
    /// cross-company is off.
    /// </summary>
    public string EffectiveFilter
    {
        get
        {
            var baseFilter = UsingRawFilter ? Filter.Trim() : BuilderFilter;
            if (!CrossCompany && IsCompanyAware && !string.IsNullOrWhiteSpace(Company))
            {
                var clause = $"dataAreaId eq '{Company.Trim().Replace("'", "''")}'";
                return string.IsNullOrEmpty(baseFilter) ? clause : $"({clause}) and ({baseFilter})";
            }

            return baseFilter;
        }
    }

    public bool HasEffectiveFilter => !string.IsNullOrEmpty(EffectiveFilter);

    /// <summary>Header caption for the Filter section ("N conditions" / "raw $filter").</summary>
    public string FilterSummary => IsRawFilterMode
        ? "raw $filter"
        : $"{FilterRoot.ConditionCount} condition{(FilterRoot.ConditionCount == 1 ? string.Empty : "s")}";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportAllCsvCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTabHeader))]
    private bool _hasRun;

    /// <summary>Active workspace tab (two-way bound to the TabControl). Run / Load more jump to Results.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>True only when the last run returned a 2xx — gates the success badge.</summary>
    [ObservableProperty]
    private bool _runSucceeded;

    /// <summary>The last run's "{code} {reason}" (e.g. "200 OK", "404 Not Found").</summary>
    [ObservableProperty]
    private string _statusBadge = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTabHeader))]
    private int _rowCount;

    [ObservableProperty]
    private string _statusText = "Not run yet.";

    /// <summary>Columns of the last run, in selection order (drives the dynamic result grid).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _resultColumns = Array.Empty<string>();

    // Deliberately NOT given the "5,000 means ≥5,000" treatment the dual-write row counts needed
    // (#159/#177): the Dataverse @odata.count cap cannot reach this screen, which only ever counts F&O.
    // The Query Builder is constructed with the shell's F&O client (ShellViewModel "query" →
    // CoreODataClient), so every request goes to {env.Url}/data/… with a token scoped to {env.Url}, and
    // F&O OData caps server-driven paging at 10,000 rows per page — not $count. Nor does pointing a
    // profile's F&O Url at a Dataverse org get a Dataverse count in here: the entity list comes only from
    // {env.Url}/data/$metadata (CatalogService), which 404s on a Dataverse host, so SelectedEntity stays
    // null and Run returns before it sends anything; the Web API lives under /api/data/v9.x, so
    // /data/{EntitySet} is not a Dataverse route; $select=* and cross-company are not valid Dataverse
    // query options; and Load more's absolute nextLink is origin-guarded to env.Url. A non-JSON or error
    // body leaves ParseMeta returning null, so no capped total can land here.
    /// <summary>Total matching rows from <c>@odata.count</c> (only when $count was requested).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTotalCount))]
    private long? _totalCount;

    /// <summary>Server-driven next-page link (<c>@odata.nextLink</c>); enables "Load more".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private string? _nextLink;

    public bool HasMore => !string.IsNullOrEmpty(NextLink);

    public bool HasTotalCount => TotalCount is not null;

    /// <summary>"N of M selected" — the $select field count, so large field lists stay legible.</summary>
    public string FieldSelectionLabel =>
        HasFields ? $"{Fields.Count(f => f.IsSelected)} of {Fields.Count} selected" : string.Empty;

    /// <summary>Fields tab header: "Fields · {selected}/{total}" (plain "Fields" when not cached).</summary>
    public string FieldsTabHeader =>
        HasFields ? $"Fields · {Fields.Count(f => f.IsSelected)}/{Fields.Count}" : "Fields";

    /// <summary>Filter tab header: "Filter · {N}" (builder), "Filter · raw" (raw mode), or "Filter".</summary>
    public string FilterTabHeader => IsRawFilterMode
        ? "Filter · raw"
        : FilterRoot.ConditionCount > 0 ? $"Filter · {FilterRoot.ConditionCount}" : "Filter";

    /// <summary>Joins tab header: "Joins · {selected}/{total}" (plain "Joins" when the entity has none).</summary>
    public string JoinsTabHeader =>
        HasNavigations ? $"Joins · {Navigations.Count(n => n.IsSelected)}/{Navigations.Count}" : "Joins";

    /// <summary>Results tab header: "Results · {rowCount}" after a run, otherwise plain "Results".</summary>
    public string ResultsTabHeader => HasRun ? $"Results · {RowCount}" : "Results";

    /// <summary>"Entities · N" (or "M of N" while a search is narrowing the list).</summary>
    public string EntityCountLabel =>
        FilteredEntities.Count == Entities.Count
            ? $"Entities · {Entities.Count}"
            : $"Entities · {FilteredEntities.Count} of {Entities.Count}";

    public QueryBuilderViewModel(IMetadataService metadata, IODataClient client,
        IClipboardService? clipboard = null, IFileSaveService? fileSave = null)
    {
        _metadata = metadata;
        _loader = new EntityCatalogLoader(metadata);
        _client = client;
        _clipboard = clipboard ?? new FakeClipboardService();
        _fileSave = fileSave ?? new FakeFileSaveService();
        // An empty root until the first entity's fields load (rebuilt by RebuildFilterContext).
        _filterRoot = new QueryFilterGroup(_filterContext, OnFilterTreeChanged, isRoot: true);
        // The fake seeds its catalogue synchronously; the real service starts empty and fills in via
        // InitializeAsync (triggered by the view on load).
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
        RefreshEntityFilter();
        SelectedEntity = Entities.FirstOrDefault();
    }

    public string NotCachedMessage => SelectedEntity is null
        ? string.Empty
        : $"$metadata for {SelectedEntity.Name} isn't cached — run once to populate the field list.";

    // Fetches the entity list (and the selected entity's fields) from the active environment's live
    // $metadata. The view calls this on load; with the fake it's a no-op over already-seeded data.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        var loaded = await _loader.LoadEntitiesAsync(Entities.Select(e => e.Name).ToList(), ct);
        LoadError = _loader.LastError;
        if (loaded is not null)
        {
            var previous = SelectedEntity?.Name;
            Entities.Clear();
            foreach (var e in loaded)
            {
                Entities.Add(e);
            }

            RefreshEntityFilter();
            SelectedEntity = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
        }

        await LoadSelectedFieldsAsync(ct);
    }

    partial void OnSelectedEntityChanged(EntitySet? value)
    {
        if (_refreshingEntities)
        {
            return; // a transient null/restore from rebuilding the filtered list — not a real selection change
        }

        // Company-awareness is only knowable once the entity's fields are loaded (see IsCompanyAware), so
        // start from "not company-aware" and let LoadFields apply the real cross-company default when they
        // arrive. The user can still override it afterwards.
        IsCompanyAware = false;
        CrossCompany = false;
        _applyCrossCompanyDefault = true;
        // A fresh entity starts in Builder mode with no raw text (the tree is rebuilt in LoadFields).
        IsRawFilterMode = false;
        Filter = string.Empty;
        ClearResults();                            // the previous entity's rows don't describe this one
        LoadFields();                              // show what's cached immediately
        OnPropertyChanged(nameof(NotCachedMessage));
        ExportAllCsvCommand.NotifyCanExecuteChanged();
        LoadSelectedFieldsCommand.Execute(null);   // then fetch from $metadata if not cached yet
    }

    /// <summary>
    /// Drops everything the last run produced. Results belong to the entity that was queried, so a real
    /// entity change invalidates them: left in place, "Load more" followed the previous entity's
    /// <c>@odata.nextLink</c> and appended its rows to a grid the header now labelled the new entity,
    /// and a "Save CSV" named the old entity's data <c>{newEntity}.csv</c> (#168).
    /// </summary>
    private void ClearResults()
    {
        // Invalidate any in-flight Run / LoadMore so its completion is discarded instead of repopulating
        // the grid the switch just emptied (PR #193 review).
        _resultsGeneration++;
        ResultRows.Clear();
        ResultColumns = Array.Empty<string>();
        NextLink = null;                 // also disables Load more (NotifyCanExecuteChangedFor)
        TotalCount = null;
        RowCount = 0;
        HasRun = false;
        RunSucceeded = false;
        StatusBadge = string.Empty;
        StatusText = "Not run yet.";
        // Neither ResultRows nor ResultColumns drives CanExecute on its own, so nudge the CSV commands.
        ExportCsvCommand.NotifyCanExecuteChanged();
        ExportCsvFileCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// True when <paramref name="generation"/> is still the generation of results on screen — i.e. no
    /// entity change has invalidated them since the caller captured it. Mirrors
    /// <c>CoreMetadataService.IsStillCurrent</c> and <c>DualWriteMapViewModel.LoadMapsAsync</c>'s
    /// discard-on-completion guard: an in-flight read is left to finish and its result dropped, rather
    /// than being cancelled on switch (simpler, and it needs no token plumbing per result field).
    /// </summary>
    private bool IsStillCurrent(int generation) => generation == _resultsGeneration;

    // The two search boxes only re-filter what's displayed; they never touch the master lists.
    partial void OnEntitySearchChanged(string value) => RefreshEntityFilter();
    partial void OnFieldSearchChanged(string value) => RefreshFieldFilter();
    partial void OnJoinSearchChanged(string value) => RefreshNavigationFilter();

    // Rebuilds FilteredEntities from Entities applying the (trimmed, case-insensitive) EntitySearch.
    // The currently-selected entity is always kept in the list (even when it doesn't match the term),
    // and the selection is snapshot/restored, so typing in the search box can't make the bound ListBox
    // null the selection and wipe the field selection + query URL. See OnSelectedEntityChanged's guard.
    private void RefreshEntityFilter()
    {
        var term = EntitySearch?.Trim();
        var saved = SelectedEntity;
        _refreshingEntities = true;
        try
        {
            FilteredEntities.Clear();
            foreach (var e in Entities)
            {
                if (string.IsNullOrEmpty(term)
                    || e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || ReferenceEquals(e, saved))
                {
                    FilteredEntities.Add(e);
                }
            }

            if (saved is not null && !ReferenceEquals(SelectedEntity, saved))
            {
                SelectedEntity = saved; // restore if the bound ListBox nulled it during Clear()
            }
        }
        finally
        {
            _refreshingEntities = false;
        }

        OnPropertyChanged(nameof(EntityCountLabel));
    }

    // Rebuilds FilteredFields from Fields applying the (trimmed, case-insensitive) FieldSearch.
    private void RefreshFieldFilter()
    {
        var term = FieldSearch?.Trim();
        FilteredFields.Clear();
        foreach (var f in Fields)
        {
            if (string.IsNullOrEmpty(term)
                || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || f.TypeDisplay.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFields.Add(f);
            }
        }
    }

    // Rebuilds FilteredNavigations from Navigations applying the (trimmed, case-insensitive) JoinSearch.
    private void RefreshNavigationFilter()
    {
        var term = JoinSearch?.Trim();
        FilteredNavigations.Clear();
        foreach (var n in Navigations)
        {
            if (string.IsNullOrEmpty(term) || n.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredNavigations.Add(n);
            }
        }
    }

    // Each option recomputes the live URL preview.
    partial void OnFilterChanged(string value) => OnFilterTreeChanged();
    partial void OnOrderByChanged(string value) => UpdateQueryUrl();
    partial void OnTopChanged(string value) => UpdateQueryUrl();
    partial void OnSkipChanged(string value) => UpdateQueryUrl();
    partial void OnCountChanged(bool value) => UpdateQueryUrl();
    partial void OnCrossCompanyChanged(bool value) => OnFilterTreeChanged();
    // Company-awareness lands asynchronously (with the entity's fields), so the dependent filter previews
    // and URL have to be refreshed when it does — the badge is the property itself.
    partial void OnIsCompanyAwareChanged(bool value) => OnFilterTreeChanged();
    partial void OnIsRawFilterModeChanged(bool value) => OnFilterTreeChanged();
    partial void OnCompanyChanged(string value) => OnFilterTreeChanged();

    // The filter tree (or its mode / company scope) changed: refresh the dependent previews + URL.
    private void OnFilterTreeChanged()
    {
        OnPropertyChanged(nameof(BuilderFilter));
        OnPropertyChanged(nameof(EffectiveFilter));
        OnPropertyChanged(nameof(HasEffectiveFilter));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(FilterTabHeader));
        UpdateQueryUrl();
    }

    // Rebuilds the filter context (field + enum metadata) and resets the builder tree for the selected
    // entity. The condition rows read field names + enum members from this context.
    private void RebuildFilterContext()
    {
        var fields = (SelectedEntity is null ? null : _metadata.GetFields(SelectedEntity.Name))
            ?? (IReadOnlyList<EntityField>)Array.Empty<EntityField>();
        _filterContext = new QueryFilterContext(
            fields,
            enumType => _metadata.GetEnumMembers(enumType) ?? Array.Empty<string>());
        FilterRoot = new QueryFilterGroup(_filterContext, OnFilterTreeChanged, isRoot: true);
        // FilterRoot was just replaced by a fresh (empty) tree. Its condition count drives both the
        // Filter section summary and the Filter tab header, so refresh them — switching entities in
        // builder mode (the common no-op path where IsRawFilterMode/Filter/CrossCompany don't change)
        // doesn't otherwise raise these, leaving the previous entity's count stale.
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(FilterTabHeader));
    }

    [RelayCommand]
    private void SetFilterMode(string mode) =>
        IsRawFilterMode = string.Equals(mode, "raw", StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private async Task CopyUrl()
    {
        if (string.IsNullOrEmpty(QueryUrl))
        {
            return;
        }

        // A contended clipboard throws (COMException on Windows), and an AsyncRelayCommand rethrows a
        // faulted command task on the dispatcher — which kills the app. A failed copy is a status line.
        try
        {
            await _clipboard.SetTextAsync(QueryUrl);
            StatusText = "Query URL copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't copy to the clipboard: {ex.Message}";
        }
    }

    // Fetches the selected entity's fields if they aren't cached yet, then rebuilds the field chips.
    [RelayCommand]
    private Task LoadSelectedFields(CancellationToken ct) => LoadSelectedFieldsAsync(ct);

    private async Task LoadSelectedFieldsAsync(CancellationToken ct)
    {
        var entity = SelectedEntity;
        if (entity is null)
        {
            return;
        }

        var fetched = await _loader.EnsureFieldsAsync(entity.Name, ct);
        LoadError = _loader.LastError;
        if (fetched && SelectedEntity == entity)
        {
            LoadFields();
            OnPropertyChanged(nameof(NotCachedMessage));
        }
    }

    private void LoadFields()
    {
        foreach (var old in Fields)
        {
            old.PropertyChanged -= OnChipChanged;
        }

        Fields.Clear();
        var fields = SelectedEntity is null ? null : _metadata.GetFields(SelectedEntity.Name);
        HasFields = fields is not null;
        // The entity is company-aware iff it actually carries a dataAreaId property (see IsCompanyAware).
        IsCompanyAware = fields is not null
            && fields.Any(f => string.Equals(f.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase));
        // Fields have arrived, so the cross-company default for this selection is now knowable: a
        // company-aware entity queries across companies by default (the dataAreaId clause is what you opt
        // into by unticking it), a global entity has nothing to scope.
        if (_applyCrossCompanyDefault && fields is not null)
        {
            _applyCrossCompanyDefault = false;
            CrossCompany = IsCompanyAware;
        }

        if (fields is not null)
        {
            foreach (var f in fields)
            {
                // Default selection = the primary key fields, mirroring the prototype's minimal $select.
                // Carry the type + mandatory metadata so the field row shows a type line + REQ marker.
                var chip = new FieldChipViewModel(f.Name, f.IsKey, isSelected: f.IsKey,
                    isMandatory: f.Mandatory, typeDisplay: f.TypeDisplay);
                chip.PropertyChanged += OnChipChanged;
                Fields.Add(chip);
            }
        }

        LoadNavigations();
        RebuildFilterContext();   // the builder's condition rows read this entity's fields + enums
        RefreshFieldFilter();
        UpdateQueryUrl();
        OnPropertyChanged(nameof(FieldSelectionLabel));
        OnPropertyChanged(nameof(FieldsTabHeader));
    }

    // Rebuilds the navigation-property ($expand) chips for the selected entity. Cached alongside the
    // fields, so this runs whenever the field list is (re)loaded.
    private void LoadNavigations()
    {
        foreach (var old in Navigations)
        {
            old.PropertyChanged -= OnChipChanged;
        }

        Navigations.Clear();
        var navs = SelectedEntity is null ? null : _metadata.GetNavigations(SelectedEntity.Name);
        HasNavigations = navs is { Count: > 0 };
        if (navs is not null)
        {
            foreach (var name in navs)
            {
                var chip = new FieldChipViewModel(name, isKey: false, isSelected: false);
                chip.PropertyChanged += OnChipChanged;
                Navigations.Add(chip);
            }
        }

        RefreshNavigationFilter();
        OnPropertyChanged(nameof(JoinsHeader));
        OnPropertyChanged(nameof(JoinsTabHeader));
    }

    // A chip's selection (toggled by the command or the view's ToggleButton) is the single source of
    // truth for the $select clause; recompute the URL whenever one flips.
    private void OnChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldChipViewModel.IsSelected))
        {
            if (_bulkUpdatingFields)
            {
                return; // SetFieldsSelection refreshes the URL + labels once when the bulk op finishes
            }

            UpdateQueryUrl();
            // No export command's CanExecute depends on the $select any more: export-all gates on the
            // entity alone (a bare $select=* is exportable) and the CSV buttons gate on the last run's
            // ResultColumns, which only a run replaces.
            // The label counters are independent (fields vs navigations); refresh both — a flip is
            // cheap and only one will actually change.
            OnPropertyChanged(nameof(FieldSelectionLabel));
            OnPropertyChanged(nameof(JoinsHeader));
            OnPropertyChanged(nameof(FieldsTabHeader));
            OnPropertyChanged(nameof(JoinsTabHeader));
        }
    }

    [RelayCommand]
    private void ToggleField(FieldChipViewModel? chip)
    {
        if (chip is not null)
        {
            chip.IsSelected = !chip.IsSelected;
        }
    }

    // Selects every currently-visible (filtered) field — so "Select all" after a search selects just the
    // matches. Clearing deselects ALL fields (not only the visible ones), so it's a reliable reset.
    [RelayCommand]
    private void SelectAllFields() => SetFieldsSelection(FilteredFields, selected: true);

    [RelayCommand]
    private void ClearFields() => SetFieldsSelection(Fields, selected: false);

    private void SetFieldsSelection(IEnumerable<FieldChipViewModel> chips, bool selected)
    {
        _bulkUpdatingFields = true;
        try
        {
            foreach (var chip in chips)
            {
                chip.IsSelected = selected;
            }
        }
        finally
        {
            _bulkUpdatingFields = false;
        }

        UpdateQueryUrl();
        OnPropertyChanged(nameof(FieldSelectionLabel));
        OnPropertyChanged(nameof(FieldsTabHeader));
    }

    private void UpdateQueryUrl() => QueryUrl = SelectedEntity is null ? string.Empty : "GET " + BuildPath(forRequest: false);

    // Builds the OData path ("/data/{Entity}?…") from the current selection + options. When
    // <paramref name="forRequest"/> is true the $filter / $orderby values are URL-encoded for the live
    // request; the readable (un-encoded) form drives the URL preview. When <paramref name="unbounded"/>
    // is true the $top / $skip / $count clauses are omitted, so a server-driven page walk (export-all)
    // can page through the entire result set rather than being capped to the preview's $top.
    private string BuildPath(bool forRequest, bool unbounded = false)
    {
        if (SelectedEntity is null)
        {
            return string.Empty;
        }

        string Encode(string value) => forRequest ? Uri.EscapeDataString(value) : value;

        var parts = new List<string>();

        var select = string.Join(",", SelectedFields());
        parts.Add($"$select={(select.Length == 0 ? "*" : select)}");

        var effectiveFilter = EffectiveFilter;
        if (!string.IsNullOrWhiteSpace(effectiveFilter))
        {
            parts.Add($"$filter={Encode(effectiveFilter)}");
        }

        if (!string.IsNullOrWhiteSpace(OrderBy))
        {
            parts.Add($"$orderby={Encode(OrderBy.Trim())}");
        }

        // $expand joins to the ticked navigation properties (related entities). Encode each name but
        // keep the commas literal — encoding the whole joined string would turn the item separators into
        // %2C and malform a multi-navigation $expand.
        var expand = string.Join(",", SelectedExpands().Select(Encode));
        if (expand.Length > 0)
        {
            parts.Add($"$expand={expand}");
        }

        if (!unbounded)
        {
            // Only positive integers contribute; blank/0/invalid omits the clause (server default applies).
            if (int.TryParse(Top, out var top) && top > 0)
            {
                parts.Add($"$top={top}");
            }

            if (int.TryParse(Skip, out var skip) && skip > 0)
            {
                parts.Add($"$skip={skip}");
            }

            if (Count)
            {
                parts.Add("$count=true");
            }
        }

        // Emit the flag either way — the prototype always states cross-company explicitly.
        parts.Add(CrossCompany ? "cross-company=true" : "cross-company=false");

        return $"/data/{SelectedEntity.Name}?{string.Join("&", parts)}";
    }

    // $select fields only.
    private IEnumerable<string> SelectedFields() =>
        Fields.Where(f => f.IsSelected).Select(f => f.Name);

    // $expand navigation properties (joins to related entities).
    private IEnumerable<string> SelectedExpands() =>
        Navigations.Where(n => n.IsSelected).Select(n => n.Name);

    // Result-grid / CSV columns: the $select fields plus the expanded navigations (each expanded nav
    // surfaces its related-entity payload as a column so the join is visible per row).
    private IEnumerable<string> SelectedColumns() =>
        SelectedFields().Concat(SelectedExpands());

    private bool CanRun() => !IsBusy;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRun))]
    private async Task Run(CancellationToken ct)
    {
        if (SelectedEntity is null)
        {
            return;
        }

        // The generation these results will belong to, captured before the await (see IsStillCurrent).
        var generation = _resultsGeneration;
        IsBusy = true;
        StatusText = "Running…";
        SelectedTabIndex = ResultsTabIndex; // land on Results so rows are visible as they load
        try
        {
            var columns = SelectedColumns().ToList();
            var path = BuildPath(forRequest: true);
            var response = await _client.SendAsync("GET", path, body: null, ct);

            // The user switched entity while this was in flight, so these rows describe an entity the
            // screen no longer shows. Drop them: the switch already left a clean slate, and writing to it
            // would label entity A's results as entity B (PR #193 review).
            if (!IsStillCurrent(generation))
            {
                return;
            }

            // With nothing selected the request goes out as $select=*, so the columns aren't knowable
            // before the response: take them from what the server actually returned. Without this the
            // grid rendered "Results · 3" over rows carrying zero cells, and a CSV of bare CRLFs (#168).
            if (columns.Count == 0 && response.IsSuccess)
            {
                MergeDerivedColumns(response.Body, columns, new HashSet<string>(StringComparer.Ordinal));
            }

            // Clear stale rows before swapping columns so the grid never renders old rows under new
            // headers (the column rebuild keys off ResultColumns changing).
            ResultRows.Clear();
            ResultColumns = columns;
            TotalCount = null;
            NextLink = null;
            if (response.IsSuccess)
            {
                foreach (var row in ParseRows(response.Body, columns))
                {
                    ResultRows.Add(row);
                }

                (TotalCount, NextLink) = ParseMeta(response.Body);
            }

            RowCount = ResultRows.Count;
            RunSucceeded = response.IsSuccess;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            StatusText = $"{DescribeCount()} · {response.StatusLine}";
            HasRun = true;
        }
        // An HTTP/socket timeout also arrives as an OperationCanceledException, but with OUR token still
        // live — only a cancelled token means the user pressed Cancel. A timeout falls through to the
        // general handler and is reported as the failure it is.
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RunSucceeded = false;
            StatusText = "Run cancelled.";
        }
        catch (Exception ex)
        {
            RunSucceeded = false;
            StatusText = $"Query failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ExportCsvCommand.NotifyCanExecuteChanged();
            ExportCsvFileCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Grows <paramref name="columns"/> with every property name in this page's rows that isn't already
    /// there, in first-seen order — the columns an unselected (<c>$select=*</c>) query renders and
    /// exports. A single row is not a reliable schema (OData omits a null property rather than emitting
    /// it), so every row on the page contributes; <c>@odata.*</c> entries are response annotations, not
    /// fields, so they stay out. Called per page by a paging export, so the final header is the union
    /// across all of them — the same semantics as <c>FoToolbox.Core.Export.CsvExporter</c>.
    /// </summary>
    /// <param name="seen">
    /// The names already in <paramref name="columns"/>, so a multi-page walk stays O(1) per key. Callers
    /// must keep it in step with <paramref name="columns"/> (both start empty, or both are carried).
    /// </param>
    private static void MergeDerivedColumns(string body, List<string> columns, HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in item.EnumerateObject())
                {
                    if (!property.Name.StartsWith("@odata.", StringComparison.OrdinalIgnoreCase)
                        && seen.Add(property.Name))
                    {
                        columns.Add(property.Name);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // A malformed body is reported by ParseRows, which parses the same payload immediately after.
        }
    }

    private bool CanLoadMore() => !IsBusy && HasMore;

    // Fetches the next server page (@odata.nextLink) and appends its rows under the same columns.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanLoadMore))]
    private async Task LoadMore(CancellationToken ct)
    {
        var link = NextLink;
        if (string.IsNullOrEmpty(link))
        {
            return;
        }

        // Captured before the await, like Run's (see IsStillCurrent).
        var generation = _resultsGeneration;
        IsBusy = true;
        StatusText = "Loading more…";
        SelectedTabIndex = ResultsTabIndex; // Load more can be triggered from any tab; show the grid
        try
        {
            var response = await _client.SendAsync("GET", link, body: null, ct);

            // The entity changed while this page was in flight: it belongs to the previous entity's
            // result set, which no longer exists. Appending it would page entity A into entity B's grid
            // — exactly what clearing on switch was meant to prevent (PR #193 review).
            if (!IsStillCurrent(generation))
            {
                return;
            }

            if (response.IsSuccess)
            {
                // Later pages are projected onto the columns the first page established (whether they
                // came from $select or were derived from that page's payload), so the grid's headers
                // stay stable; a property only some pages carry shows the null placeholder elsewhere.
                foreach (var row in ParseRows(response.Body, ResultColumns))
                {
                    ResultRows.Add(row);
                }

                var (count, next) = ParseMeta(response.Body);
                TotalCount = count ?? TotalCount; // a page may omit the count; keep the prior total
                NextLink = next;
            }

            // Reflect the latest call in the badge, so a failed page doesn't keep showing the prior success.
            RunSucceeded = response.IsSuccess;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            RowCount = ResultRows.Count;
            StatusText = $"{DescribeCount()} · {response.StatusLine}";
        }
        // Only a cancelled token is the user asking to stop; a timeout arrives the same way and is a
        // failure (see Run).
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            StatusText = "Load more cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Load more failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ExportCsvCommand.NotifyCanExecuteChanged();
            ExportCsvFileCommand.NotifyCanExecuteChanged();
        }
    }

    private string DescribeCount() =>
        TotalCount is { } total ? $"{RowCount} of {total} rows" : $"{RowCount} rows";

    // Reads @odata.count + @odata.nextLink from an OData response root (both optional).
    private static (long? Count, string? NextLink) ParseMeta(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, null);
            }

            long? count = null;
            if (root.TryGetProperty("@odata.count", out var c))
            {
                if (c.ValueKind == JsonValueKind.Number && c.TryGetInt64(out var n))
                {
                    count = n;
                }
                else if (c.ValueKind == JsonValueKind.String && long.TryParse(c.GetString(), out var parsed))
                {
                    count = parsed;
                }
            }

            var next = root.TryGetProperty("@odata.nextLink", out var nl) && nl.ValueKind == JsonValueKind.String
                ? nl.GetString()
                : null;

            return (count, next);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    // Rows alone aren't enough: a run can land rows with no columns (a $select=* payload whose objects
    // carry no readable properties), and CSV of nothing but line terminators is worse than a disabled
    // button (#168). Export-all enforces the same invariant, but only after its fetch — it derives its
    // own header, so it can't know before then (see ExportAllCsv).
    private bool CanExportCsv() => ResultRows.Count > 0 && ResultColumns.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsv()
    {
        var csv = QueryCsv.Build(ResultColumns, ResultRows);
        try
        {
            await _clipboard.SetTextAsync(csv);
            StatusText = $"Copied {ResultRows.Count} rows as CSV to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText = $"Couldn't copy to the clipboard: {ex.Message}";
        }
    }

    // Saves the currently-loaded rows to a .csv file the user picks.
    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsvFile(CancellationToken ct)
    {
        // Snapshot the entity name, content and count up front — mirroring ExportAllCsv — so the file
        // describes the rows being written rather than whatever is selected by the time the picker
        // returns. (The rows themselves can't change mid-save: a real entity change clears them.)
        var entityName = SelectedEntity?.Name ?? "query";
        var csv = QueryCsv.Build(ResultColumns, ResultRows);
        var rows = ResultRows.Count;
        var name = $"{entityName}.csv";
        try
        {
            var path = await _fileSave.SaveTextAsync(name, csv, SaveFileType.Csv, ct);
            StatusText = path is null ? "Export cancelled." : $"Saved {rows} rows to {path}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Export cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
    }

    // Export-all can run whenever an entity is selected (it issues its own unbounded query); gated off
    // IsBusy so it doesn't overlap a Run / Load-more. Deliberately NOT gated on a $select: with no
    // fields ticked the query is a valid $select=*, and demanding a selection blocked exporting the very
    // rows Run had just rendered from a derived header (PR #193 review).
    private bool CanExportAllCsv() => !IsBusy && SelectedEntity is not null;

    // Pages through the ENTIRE result set (following @odata.nextLink) and saves it as one .csv file,
    // independent of the preview's $top/paging. Bounded by MaxExportPages as a runaway guard.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanExportAllCsv))]
    private async Task ExportAllCsv(CancellationToken ct)
    {
        if (SelectedEntity is null)
        {
            return;
        }

        // Snapshot the entity (name + columns + base path) up front: the user may switch selection
        // while the export is paging, and the saved file should reflect what was exported, not the
        // current selection.
        var entityName = SelectedEntity.Name;

        IsBusy = true;
        StatusText = "Exporting all rows…";
        try
        {
            var columns = SelectedColumns().ToList();
            // With no $select the header is whatever the server sends, so grow it page by page as they
            // stream in: a later page may carry a property an earlier one omitted (OData drops nulls),
            // and rows parsed before a column appeared simply have no value for it — QueryResultRow
            // reports that as null, which exports as an empty field. The final header is therefore the
            // union across every page.
            var derivingColumns = columns.Count == 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rows = new List<QueryResultRow>();
            var path = BuildPath(forRequest: true, unbounded: true);
            var pages = 0;
            var capped = false;

            while (true)
            {
                var response = await _client.SendAsync("GET", path, body: null, ct);
                if (!response.IsSuccess)
                {
                    StatusText = $"Export failed: {response.StatusLine}";
                    return;
                }

                if (derivingColumns)
                {
                    // Before parsing, so this page's rows carry every column known so far.
                    MergeDerivedColumns(response.Body, columns, seen);
                }

                rows.AddRange(ParseRows(response.Body, columns));

                var (_, next) = ParseMeta(response.Body);
                if (string.IsNullOrEmpty(next))
                {
                    break;
                }

                if (++pages >= MaxExportPages)
                {
                    capped = true;
                    break;
                }

                path = next;
            }

            // Nothing derivable and nothing selected: the file would be bare line terminators. Same
            // invariant CanExportCsv enforces for the preview, but only knowable here after the fetch.
            if (columns.Count == 0)
            {
                StatusText = "Nothing to export: the query returned no columns.";
                return;
            }

            var csv = QueryCsv.Build(columns, rows);
            var name = $"{entityName}.csv";
            var saved = await _fileSave.SaveTextAsync(name, csv, SaveFileType.Csv, ct);
            if (saved is null)
            {
                StatusText = "Export cancelled.";
            }
            else
            {
                StatusText = capped
                    ? $"Saved {rows.Count} rows to {saved} (stopped at the {MaxExportPages}-page limit — more rows may exist)."
                    : $"Saved {rows.Count} rows to {saved}.";
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelling via CancelExportAllCsvCommand is a clean outcome, not an error.
            StatusText = "Export cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Cancels whichever long-running command is in flight — Run, Load more or Export all. Each has its
    /// own generated cancel command (<c>IncludeCancelCommand</c>); this gives the view one Cancel button
    /// to bind so it doesn't have to know which is running. A cancel command reports itself executable
    /// only while its command is running and cancellable, so this is a no-op when idle.
    /// </summary>
    /// <remarks>
    /// The generated cancel commands existed but were bound nowhere, which left every cancellation path
    /// on this screen unreachable in production — including <see cref="ExportAllCsv"/>'s clean
    /// "Export cancelled." outcome (#168).
    /// </remarks>
    [RelayCommand]
    private void CancelBusy()
    {
        foreach (var cancel in new[] { RunCancelCommand, LoadMoreCancelCommand, ExportAllCsvCancelCommand })
        {
            if (cancel.CanExecute(null))
            {
                cancel.Execute(null);
            }
        }
    }

    // Projects an OData {"value":[ {...} ]} payload onto the selected columns.
    private static IEnumerable<QueryResultRow> ParseRows(string body, IReadOnlyList<string> columns)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            yield break;
        }

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in value.EnumerateArray())
        {
            // A column the payload omits is stored as null — the same shape an explicit JSON null takes,
            // and what QueryResultRow renders as the em-dash. Nullness stays a property of the cell
            // rather than of its display text (see QueryResultRow).
            var cells = new Dictionary<string, string?>(columns.Count);
            foreach (var column in columns)
            {
                cells[column] = item.TryGetProperty(column, out var cell) ? CellText(cell) : null;
            }

            yield return new QueryResultRow(cells);
        }
    }

    // The cell's raw text, or null when the field is JSON null. QueryResultRow derives the grid's
    // em-dash from that null at display time; QueryCsv exports it as an empty field.
    private static string? CellText(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => cell.GetString(),
        _ => cell.ToString(),
    };
}
