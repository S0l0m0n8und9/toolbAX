using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.Core.Export;
using FoToolbox.Core.Profiles;
using FoToolbox.SDK.Collections;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace QueryBuilderPlugin;

public sealed class QueryBuilderViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly SavedQueryStore _savedStore;
    private ODataEntityIndex? _entityIndex;
    private ODataEntity? _selectedEntityDetails;
    private readonly BulkObservableCollection<EntityItem> _entities = new();
    private readonly ObservableCollection<FieldItem> _fields = new();
    private readonly ObservableCollection<FieldItem> _filterFields = new();
    private readonly ObservableCollection<string> _navigation = new();
    private readonly ObservableCollection<string> _selectedFields = new();
    private readonly ObservableCollection<SavedQueryItem> _savedQueries = new();
    private readonly HashSet<FilterNodeViewModel> _filterSubscriptions = new();
    private Dictionary<string, ODataEnumType>? _enumLookup;
    private Dictionary<string, EnumFieldInfo>? _enumFields;
    private string? _selectedEntity;
    private string? _entitySearch;
    private string? _fieldSearch;
    private bool _showProperties = true;
    private bool _showNavigation = true;
    private string? _orderBy;
    private string? _top;
    private string? _skip;
    private string? _filterText;
    private string? _expandPath;
    private bool _crossCompany = true;
    private string? _company;
    private bool _count;
    private string _status = "Ready";
    private bool _isLoadingEntities;
    private string _entityLoadStatus = "Metadata not loaded.";
    private string _entityListSummary = "0 entities";
    private string _fieldListSummary = "0 fields";
    private string _fieldSelectionSummary = "0 selected";
    private string _selectedEntitySummary = "No entity selected.";
    private string _filterBuilderPreview = "No builder filter.";
    private string _effectiveFilterPreview = "No filter.";
    private string _filterUsageHint = "Builder filter in use.";
    private string _expandSummary = "No expand selected.";
    private string _queryPreview = "Select an entity to preview the query.";
    private string _responseDetails = "No preview run yet.";
    private long? _lastODataCount;
    private string? _nextLink;
    private bool _expandInvalid;
    private bool _hasMoreNextLink;
    private string? _validationWarning;
    private string? _expandWarning;
    private DataView? _preview;
    private SavedQueryItem? _selectedSaved;
    private readonly SynchronizationContext? _syncContext = SynchronizationContext.Current;
    private CancellationTokenSource? _entitySearchCts;
    private CancellationTokenSource? _fieldSearchCts;
    private CancellationTokenSource? _selectedEntityDetailsCts;
    private bool _suppressFieldSelectionRebuild;

    // Useful for tests and for callers that want to await details loading.
    public Task SelectedEntityDetailsTask { get; private set; } = Task.CompletedTask;

    public FilterGroupViewModel RootGroup { get; } = new() { LogicalOperator = "and" };

    public QueryBuilderViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        _savedStore = new SavedQueryStore(ProfilePaths.ResolveProfileDbPath());
        Entities = new ReadOnlyObservableCollection<EntityItem>(_entities);
        Fields = new ReadOnlyObservableCollection<FieldItem>(_fields);
        FilterFields = new ReadOnlyObservableCollection<FieldItem>(_filterFields);
        NavigationHints = new ReadOnlyObservableCollection<string>(_navigation);
        SelectedFields = new ReadOnlyObservableCollection<string>(_selectedFields);
        SavedQueries = new ReadOnlyObservableCollection<SavedQueryItem>(_savedQueries);
        EntitiesView = CollectionViewSource.GetDefaultView(_entities);
        EntitiesView.Filter = EntityFilter;
        FieldsView = CollectionViewSource.GetDefaultView(_fields);
        FieldsView.Filter = FieldFilter;

        Action<Exception> onCommandError = ex =>
        {
            _ctx.Logger.LogError(ex, "QueryBuilder command failed.");
            Status = $"Command failed: {ex.Message}";
        };

        LoadEntitiesCommand = new AsyncRelayCommand(LoadEntitiesAsync, onCommandError);
        RefreshEntitiesCommand = new AsyncRelayCommand(RefreshEntitiesAsync, onCommandError);
        RefreshSelectedEntityCommand = new AsyncRelayCommand(RefreshSelectedEntityAsync, onCommandError);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, onCommandError);
        AddConditionCommand = new RelayCommand(p => AddCondition(p as FilterGroupViewModel ?? RootGroup));
        AddGroupCommand = new RelayCommand(p => AddGroup(p as FilterGroupViewModel ?? RootGroup));
        RemoveNodeCommand = new RelayCommand(RemoveNode);
        SelectAllFieldsCommand = new RelayCommand(_ => SelectAllFields());
        SelectVisibleFieldsCommand = new RelayCommand(_ => SelectVisibleFields());
        ClearFieldSelectionCommand = new RelayCommand(_ => ClearSelectedFields());
        ExportPageCommand = new AsyncRelayCommand(ExportPageAsync, onCommandError);
        ExportAllCommand = new AsyncRelayCommand(ExportAllAsync, onCommandError);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync, onCommandError);
        SaveQueryCommand = new AsyncRelayCommand(SaveCurrentQueryAsync, onCommandError);
        LoadSavedQueryCommand = new RelayCommand(_ => LoadSelectedQuery());
        DeleteSavedQueryCommand = new AsyncRelayCommand(DeleteSelectedQueryAsync, onCommandError);
        RenameSavedQueryCommand = new AsyncRelayCommand(RenameSelectedQueryAsync, onCommandError);

        HookFilterNode(RootGroup);

        UpdateEntitySummary();
        UpdateFieldSummary();
        UpdateFilterPreview();
        UpdateExpandSummary();

        _ = LoadSavedQueriesAsync();
    }

    public ReadOnlyObservableCollection<EntityItem> Entities { get; }
    public ReadOnlyObservableCollection<FieldItem> Fields { get; }
    public ReadOnlyObservableCollection<FieldItem> FilterFields { get; }
    public ReadOnlyObservableCollection<string> NavigationHints { get; }
    public ReadOnlyObservableCollection<string> SelectedFields { get; }
    public ReadOnlyObservableCollection<SavedQueryItem> SavedQueries { get; }
    public ICollectionView EntitiesView { get; }
    public ICollectionView FieldsView { get; }
    public string FilterHint => "Builder operators: eq/ne/gt/ge/lt/le, startswith(field,'value'), endswith(field,'value'), contains(field,'value'). Raw $filter overrides the builder. When cross-company is off and a company is set, dataAreaId is injected automatically.";

    public string? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_selectedEntity != value)
            {
                _selectedEntity = value;
                OnPropertyChanged();
                _fields.Clear();
                _filterFields.Clear();
                _navigation.Clear();
                _selectedFields.Clear();
                _enumFields = null;
                _selectedEntityDetails = null;
                PreviewTable = null;
                SetNextLink(null);
                _lastODataCount = null;
                ResponseDetails = "No preview run yet.";
                UpdateSelectedEntitySummary();
                UpdateFieldSummary();
                UpdateFilterPreview();
                UpdateExpandSummary();
                UpdateQueryPreview();
                RefreshFilterEnumProviders();
                StartLoadSelectedEntityDetails();
            }
        }
    }

    public string? EntitySearch
    {
        get => _entitySearch;
        set
        {
            if (_entitySearch != value)
            {
                _entitySearch = value;
                OnPropertyChanged();
                ScheduleEntitySearchRefresh();
            }
        }
    }

    public string? FieldSearch
    {
        get => _fieldSearch;
        set
        {
            if (_fieldSearch != value)
            {
                _fieldSearch = value;
                OnPropertyChanged();
                ScheduleFieldSearchRefresh();
            }
        }
    }

    public bool ShowProperties
    {
        get => _showProperties;
        set
        {
            if (_showProperties != value)
            {
                _showProperties = value;
                OnPropertyChanged();
                FieldsView.Refresh();
                UpdateFieldSummary();
            }
        }
    }

    public bool ShowNavigation
    {
        get => _showNavigation;
        set
        {
            if (_showNavigation != value)
            {
                _showNavigation = value;
                OnPropertyChanged();
                FieldsView.Refresh();
                UpdateFieldSummary();
            }
        }
    }

    public string? OrderBy
    {
        get => _orderBy;
        set { _orderBy = value; OnPropertyChanged(); ResetPaging(); UpdateQueryPreview(); }
    }

    public string? Top
    {
        get => _top;
        set { _top = value; OnPropertyChanged(); ResetPaging(); UpdateQueryPreview(); }
    }

    public string? Skip
    {
        get => _skip;
        set { _skip = value; OnPropertyChanged(); ResetPaging(); UpdateQueryPreview(); }
    }

    public string? FilterText
    {
        get => _filterText;
        set
        {
            _filterText = value;
            OnPropertyChanged();
            ResetPaging();
            UpdateFilterPreview();
            UpdateQueryPreview();
        }
    }

    public string? ExpandPath
    {
        get => _expandPath;
        set
        {
            _expandPath = value;
            OnPropertyChanged();
            ResetPaging();
            UpdateExpandSummary();
            UpdateQueryPreview();
        }
    }

    public bool CrossCompany
    {
        get => _crossCompany;
        set
        {
            _crossCompany = value;
            OnPropertyChanged();
            ResetPaging();
            UpdateFilterPreview();
            UpdateQueryPreview();
        }
    }

    public string? Company
    {
        get => _company;
        set
        {
            _company = value;
            OnPropertyChanged();
            ResetPaging();
            UpdateFilterPreview();
            UpdateQueryPreview();
        }
    }

    public bool Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(); UpdateQueryPreview(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public bool IsLoadingEntities
    {
        get => _isLoadingEntities;
        set { _isLoadingEntities = value; OnPropertyChanged(); }
    }

    public string EntityLoadStatus
    {
        get => _entityLoadStatus;
        set { _entityLoadStatus = value; OnPropertyChanged(); }
    }

    public string EntityListSummary
    {
        get => _entityListSummary;
        set { _entityListSummary = value; OnPropertyChanged(); }
    }

    public string FieldListSummary
    {
        get => _fieldListSummary;
        set { _fieldListSummary = value; OnPropertyChanged(); }
    }

    public string FieldSelectionSummary
    {
        get => _fieldSelectionSummary;
        set { _fieldSelectionSummary = value; OnPropertyChanged(); }
    }

    public string SelectedEntitySummary
    {
        get => _selectedEntitySummary;
        set { _selectedEntitySummary = value; OnPropertyChanged(); }
    }

    public string? ValidationWarning
    {
        get => _validationWarning;
        set { _validationWarning = value; OnPropertyChanged(); }
    }

    public string? ExpandWarning
    {
        get => _expandWarning;
        set { _expandWarning = value; OnPropertyChanged(); }
    }

    public string FilterBuilderPreview
    {
        get => _filterBuilderPreview;
        set { _filterBuilderPreview = value; OnPropertyChanged(); }
    }

    public string EffectiveFilterPreview
    {
        get => _effectiveFilterPreview;
        set { _effectiveFilterPreview = value; OnPropertyChanged(); }
    }

    public string FilterUsageHint
    {
        get => _filterUsageHint;
        set { _filterUsageHint = value; OnPropertyChanged(); }
    }

    public string ExpandSummary
    {
        get => _expandSummary;
        set { _expandSummary = value; OnPropertyChanged(); }
    }

    public string QueryPreview
    {
        get => _queryPreview;
        set { _queryPreview = value; OnPropertyChanged(); }
    }

    public string ResponseDetails
    {
        get => _responseDetails;
        set { _responseDetails = value; OnPropertyChanged(); }
    }

    public DataView? PreviewTable
    {
        get => _preview;
        set { _preview = value; OnPropertyChanged(); }
    }

    public SavedQueryItem? SelectedSaved
    {
        get => _selectedSaved;
        set { _selectedSaved = value; OnPropertyChanged(); }
    }

    public bool HasMoreNextLink
    {
        get => _hasMoreNextLink;
        private set { _hasMoreNextLink = value; OnPropertyChanged(); }
    }

    public AsyncRelayCommand LoadEntitiesCommand { get; }
    public AsyncRelayCommand RefreshEntitiesCommand { get; }
    public AsyncRelayCommand RefreshSelectedEntityCommand { get; }
    public AsyncRelayCommand PreviewCommand { get; }
    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand RemoveNodeCommand { get; }
    public RelayCommand SelectAllFieldsCommand { get; }
    public RelayCommand SelectVisibleFieldsCommand { get; }
    public RelayCommand ClearFieldSelectionCommand { get; }
    public AsyncRelayCommand ExportPageCommand { get; }
    public AsyncRelayCommand ExportAllCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public AsyncRelayCommand SaveQueryCommand { get; }
    public RelayCommand LoadSavedQueryCommand { get; }
    public AsyncRelayCommand DeleteSavedQueryCommand { get; }
    public AsyncRelayCommand RenameSavedQueryCommand { get; }

    private async Task LoadEntitiesAsync(CancellationToken cancellationToken)
    {
        await LoadEntitiesCoreAsync(CatalogRefreshMode.UseCacheIfAvailable, cancellationToken);
    }

    private async Task RefreshEntitiesAsync(CancellationToken cancellationToken)
    {
        // Explicit refresh: pull metadata from source and rebuild the index cache.
        await _ctx.Catalog.RefreshAsync(_ctx.CurrentEnv, CatalogRefreshScope.ODataMetadata, cancellationToken);
        await LoadEntitiesCoreAsync(CatalogRefreshMode.UseCacheIfAvailable, cancellationToken);
    }

    private async Task LoadEntitiesCoreAsync(CatalogRefreshMode mode, CancellationToken cancellationToken)
    {
        Status = "Loading entities...";
        EntityLoadStatus = "Loading metadata...";
        IsLoadingEntities = true;
        _entities.Clear();
        _fields.Clear();
        _filterFields.Clear();
        _navigation.Clear();
        _selectedFields.Clear();
        _selectedEntityDetails = null;
        _selectedEntityDetailsCts?.Cancel();
        SelectedEntityDetailsTask = Task.CompletedTask;
        ClearFilterTree();
        PreviewTable = null;
        SetNextLink(null);
        _lastODataCount = null;
        ResponseDetails = "No preview run yet.";

        try
        {
            var started = DateTime.UtcNow;
            _entityIndex = await _ctx.Catalog.GetODataEntityIndexAsync(_ctx.CurrentEnv, mode, cancellationToken);
            _enumLookup = BuildEnumLookup(_entityIndex.Enums);
            EntityLoadStatus = "Populating entity list...";

            var ordered = _entityIndex.Entities
                .OrderBy(e => e.Name)
                .Select(e => new EntityItem(e.Name, e.PropertyCount, e.NavigationCount))
                .ToList();

            _entities.ReplaceAll(ordered);
            EntitiesView.Refresh();
            UpdateEntitySummary();
            EntityLoadStatus = $"Loaded {_entities.Count} entities in {(DateTime.UtcNow - started).TotalSeconds:F1}s.";
            Status = "Pick an entity, then choose fields and filters.";
            RefreshFilterEnumProviders();
        }
        catch (Exception ex)
        {
            EntityLoadStatus = $"Metadata load failed: {ex.Message}";
            Status = EntityLoadStatus;
        }
        finally
        {
            IsLoadingEntities = false;
        }
    }

    private async Task RefreshSelectedEntityAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_selectedEntity))
        {
            Status = "Select an entity first.";
            return;
        }

        var selected = new HashSet<string>(_selectedFields, StringComparer.OrdinalIgnoreCase);

        _fields.Clear();
        _filterFields.Clear();
        _navigation.Clear();
        _selectedFields.Clear();
        _enumFields = null;
        _selectedEntityDetails = null;
        FieldsView.Refresh();
        UpdateFieldSummary();
        UpdateSelectedEntitySummary();
        UpdateExpandSummary();
        UpdateQueryPreview();

        _selectedEntityDetailsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _selectedEntityDetailsCts = cts;
        SelectedEntityDetailsTask = LoadSelectedEntityDetailsAsync(_selectedEntity, CatalogRefreshMode.ForceRefresh, cts.Token);
        await SelectedEntityDetailsTask;

        // Reapply field selection by name (best-effort).
        _suppressFieldSelectionRebuild = true;
        try
        {
            foreach (var field in _fields)
            {
                if (selected.Contains(field.Name))
                {
                    field.IsSelected = true;
                }
            }
        }
        finally
        {
            _suppressFieldSelectionRebuild = false;
        }

        RebuildSelectedFields();
        Status = $"Refreshed entity: {_selectedEntity}";
    }

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildQueryRequest(out var request))
        {
            return;
        }

        Status = "Running query...";
        SetNextLink(null);
        _lastODataCount = null;
        ResponseDetails = "Running query...";
        try
        {
            int rowCount = 0;
            DataTable? table = null;
            ODataPage? first = null;
            await foreach (var page in _ctx.OData.StreamAsync(request, cancellationToken))
            {
                first = page;
                if (table == null)
                {
                    table = BuildTable(page);
                }
                rowCount += page.Rows.Count;
                SetNextLink(page.NextLink);
                break; // first page only for now
            }
            PreviewTable = table?.DefaultView;

            if (first is not null)
            {
                _lastODataCount = first.ODataCount;
                ResponseDetails = FormatResponseDetails(first);
            }
            else
            {
                ResponseDetails = "No response received.";
            }

            var countHint = _lastODataCount is not null ? $" | Count={_lastODataCount}" : string.Empty;
            Status = $"{rowCount} rows (first page){countHint}{(_nextLink is not null ? " | More available" : string.Empty)}";
        }
        catch (Exception ex)
        {
            Status = $"Preview failed: {ex.Message}";
            ResponseDetails = $"Preview failed: {ex.Message}";
        }
    }

    private DataTable BuildTable(ODataPage page)
    {
        var table = new DataTable();
        if (page.Rows.Count == 0) return table;
        var cols = page.Rows[0].Keys.ToList();
        foreach (var c in cols) table.Columns.Add(c);
        foreach (var row in page.Rows)
        {
            var values = cols.Select(c => row.TryGetValue(c, out var v) ? v : null).ToArray();
            table.Rows.Add(values);
        }
        return table;
    }

    private static string FormatResponseDetails(ODataPage page)
    {
        var sb = new StringBuilder();

        if (page.ODataCount is not null)
        {
            sb.AppendLine($"@odata.count: {page.ODataCount}");
        }
        if (!string.IsNullOrWhiteSpace(page.ODataContext))
        {
            sb.AppendLine($"@odata.context: {page.ODataContext}");
        }
        if (!string.IsNullOrWhiteSpace(page.NextLink))
        {
            sb.AppendLine($"@odata.nextLink: {page.NextLink}");
        }

        if (page.ResponseHeaders is { Count: > 0 })
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Headers:");
            foreach (var kvp in page.ResponseHeaders.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
        }

        return sb.Length == 0 ? "No response metadata." : sb.ToString().TrimEnd();
    }

    private void StartLoadSelectedEntityDetails()
    {
        _selectedEntityDetailsCts?.Cancel();

        if (string.IsNullOrWhiteSpace(_selectedEntity))
        {
            SelectedEntityDetailsTask = Task.CompletedTask;
            return;
        }

        var cts = new CancellationTokenSource();
        _selectedEntityDetailsCts = cts;
        SelectedEntityDetailsTask = LoadSelectedEntityDetailsAsync(_selectedEntity, CatalogRefreshMode.UseCacheIfAvailable, cts.Token);
    }

    private async Task LoadSelectedEntityDetailsAsync(string entityName, CatalogRefreshMode mode, CancellationToken cancellationToken)
    {
        try
        {
            // This can be async if the catalog chooses to lazily parse or fetch per-entity details.
            Status = $"Loading fields for {entityName}...";
            var entity = await _ctx.Catalog.GetODataEntityDetailsAsync(_ctx.CurrentEnv, entityName, mode, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (entity is null)
            {
                Status = $"Entity not found in metadata: {entityName}";
                return;
            }

            // Ensure we still match the current selection.
            if (!string.Equals(_selectedEntity, entityName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedEntityDetails = entity;

            var enumFields = new Dictionary<string, EnumFieldInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in entity.Properties.OrderBy(p => p.Name))
            {
                var enumInfo = ResolveEnumInfo(_enumLookup, prop.Type);
                var enumValues = enumInfo is null ? null : string.Join(", ", enumInfo.Members);
                var field = new FieldItem(prop.Name, prop.Type, "Property", prop.Nullable, enumValues);
                field.SelectionChanged += FieldSelectionChanged;
                _fields.Add(field);
                _filterFields.Add(field);
                if (enumInfo is not null)
                {
                    enumFields[prop.Name] = enumInfo;
                }
            }
            foreach (var nav in entity.Navigations.OrderBy(n => n.Name))
            {
                var field = new FieldItem(nav.Name, nav.Type, "Navigation", nullable: true, enumValues: null);
                field.SelectionChanged += FieldSelectionChanged;
                _fields.Add(field);
                _navigation.Add(nav.Name);
            }

            _enumFields = enumFields;
            FieldsView.Refresh();
            UpdateFieldSummary();
            UpdateSelectedEntitySummary();
            UpdateExpandSummary();
            UpdateQueryPreview();
            ResetPaging();
            RefreshFilterEnumProviders();
            Status = "Pick an entity, then choose fields and filters.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load entity details for {Entity}", entityName);
            Status = $"Failed to load fields: {ex.Message}";
        }
    }

    public void UpdateSelectedFields(System.Collections.IList selectedItems)
    {
        _selectedFields.Clear();
        foreach (var item in selectedItems)
        {
            if (item is FieldItem field)
            {
                field.IsSelected = true;
                _selectedFields.Add(field.Name);
            }
            else if (item is string s)
            {
                _selectedFields.Add(s);
            }
        }
        OnPropertyChanged(nameof(SelectedFields));
        ResetPaging();
        UpdateFieldSummary();
        UpdateQueryPreview();
    }

    private void FieldSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressFieldSelectionRebuild) return;
        RebuildSelectedFields();
    }

    private void RebuildSelectedFields()
    {
        _selectedFields.Clear();
        foreach (var field in _fields.Where(f => f.IsSelected))
        {
            _selectedFields.Add(field.Name);
        }
        OnPropertyChanged(nameof(SelectedFields));
        UpdateFieldSummary();
        UpdateQueryPreview();
    }

    public QueryRequest BuildQueryRequest()
    {
        if (!TryBuildQueryRequest(out var request))
        {
            throw new InvalidOperationException(ValidationWarning ?? "Query is not valid.");
        }
        return request;
    }

    public bool TryBuildQueryRequest(out QueryRequest request)
    {
        request = null!;
        if (!TryBuildQuerySpec(out var spec, out var issue))
        {
            ValidationWarning = issue;
            if (!string.IsNullOrWhiteSpace(issue))
            {
                Status = issue;
            }
            return false;
        }

        request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        return true;
    }

    private bool TryBuildQuerySpec(out QuerySpec spec, out string? issue)
    {
        spec = null!;
        issue = null;
        ValidationWarning = null;

        if (string.IsNullOrWhiteSpace(SelectedEntity))
        {
            issue = "Select an entity before running.";
            return false;
        }

        FilterNode? filterNode = null;
        if (string.IsNullOrWhiteSpace(FilterText))
        {
            var (validFilter, ast) = BuildFilterAst();
            if (!validFilter)
            {
                issue = ValidationWarning ?? "Fix validation issues in the filter builder.";
                return false;
            }
            filterNode = ast;
        }

        var expand = NormalizeExpand();
        if (_expandInvalid)
        {
            issue = ExpandWarning ?? "Invalid expand path.";
            return false;
        }

        if (!TryParseNullableInt(Top, minInclusive: 1, out var top))
        {
            issue = "$top must be a positive integer.";
            return false;
        }

        if (!TryParseNullableInt(Skip, minInclusive: 0, out var skip))
        {
            issue = "$skip must be 0 or a positive integer.";
            return false;
        }

        spec = new QuerySpec(
            Entity: SelectedEntity ?? string.Empty,
            CrossCompany: CrossCompany,
            Company: Company,
            Select: SelectedFields.ToList(),
            OrderBy: OrderBy,
            Top: top,
            Skip: skip,
            Count: Count,
            Filter: string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
            Where: filterNode,
            Expand: expand);
        return true;
    }

    private string? NormalizeExpand()
    {
        ExpandWarning = null;
        _expandInvalid = false;
        if (string.IsNullOrWhiteSpace(ExpandPath)) return null;
        var parts = ExpandPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            ExpandWarning = "Expand limited to 1 level; using the first segment only.";
        }
        var expand = parts[0];
        if (_navigation.Count == 0)
        {
            ExpandWarning = (ExpandWarning is null ? string.Empty : $"{ExpandWarning} ") + "Load entities to validate expand.";
            return expand;
        }
        if (_navigation.Count > 0 && !_navigation.Contains(expand))
        {
            ExpandWarning = (ExpandWarning is null ? string.Empty : $"{ExpandWarning} ") + "Pick a navigation from the hints list.";
            _expandInvalid = true;
        }
        return expand;
    }

    private static bool TryParseNullableInt(string? text, int minInclusive, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text, out var parsed)) return false;
        if (parsed < minInclusive) return false;
        value = parsed;
        return true;
    }

    private (bool valid, FilterNode? ast) BuildFilterAst()
    {
        if (RootGroup.Children.Count == 0)
        {
            ValidationWarning = null;
            return (true, null);
        }

        var issues = new List<string>();
        var ast = BuildGroup(RootGroup, issues);

        if (issues.Count > 0)
        {
            ValidationWarning = string.Join(" ", issues.Distinct());
            return (false, null);
        }

        if (ast is FilterGroup { Children.Count: 0 })
        {
            return (true, null);
        }

        return (true, ast);
    }

    private FilterNode BuildGroup(FilterGroupViewModel group, List<string> issues)
    {
        var children = new List<FilterNode>();

        foreach (var child in group.Children)
        {
            if (child is FilterConditionViewModel cond)
            {
                if (string.IsNullOrWhiteSpace(cond.Field) || string.IsNullOrWhiteSpace(cond.Value))
                {
                    issues.Add("Filter conditions need a field and value.");
                    continue;
                }
                children.Add(cond.ToAst());
            }
            else if (child is FilterGroupViewModel grp)
            {
                var nested = BuildGroup(grp, issues);
                if (nested is FilterGroup { Children.Count: 0 }) continue;
                children.Add(nested);
            }
        }

        return new FilterGroup(group.LogicalOperator, children);
    }

    private void AddCondition(FilterGroupViewModel parent)
    {
        parent.Children.Add(new FilterConditionViewModel { Parent = parent, Operator = "eq" });
        UpdateFilterPreview();
        UpdateQueryPreview();
    }

    private void AddGroup(FilterGroupViewModel parent)
    {
        var group = new FilterGroupViewModel { LogicalOperator = "and", Parent = parent };
        parent.Children.Add(group);
        UpdateFilterPreview();
        UpdateQueryPreview();
    }

    private void RemoveNode(object? parameter)
    {
        if (parameter is not FilterNodeViewModel node || node.Parent is null) return;
        UnhookFilterNode(node);
        node.Parent.Children.Remove(node);
        UpdateFilterPreview();
        UpdateQueryPreview();
    }

    private void SelectAllFields()
    {
        foreach (var field in _fields)
        {
            field.IsSelected = true;
        }
        RebuildSelectedFields();
    }

    private void SelectVisibleFields()
    {
        foreach (var item in FieldsView)
        {
            if (item is FieldItem field)
            {
                field.IsSelected = true;
            }
        }
        RebuildSelectedFields();
    }

    private void ClearSelectedFields()
    {
        foreach (var field in _fields)
        {
            field.IsSelected = false;
        }
        RebuildSelectedFields();
    }

    private bool EntityFilter(object? item)
    {
        if (item is not EntityItem entity) return false;
        if (string.IsNullOrWhiteSpace(EntitySearch)) return true;
        return entity.Name.Contains(EntitySearch.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool FieldFilter(object? item)
    {
        if (item is not FieldItem field) return false;

        if (!ShowProperties && field.Kind == "Property") return false;
        if (!ShowNavigation && field.Kind == "Navigation") return false;

        if (string.IsNullOrWhiteSpace(FieldSearch)) return true;

        var term = FieldSearch.Trim();
        return field.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || field.Type.Contains(term, StringComparison.OrdinalIgnoreCase)
            || field.Kind.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateEntitySummary()
    {
        var total = _entities.Count;
        var visible = EntitiesView.Cast<object>().Count();
        EntityListSummary = $"Showing {visible} of {total} entities";
    }

    private void UpdateFieldSummary()
    {
        var total = _fields.Count;
        var visible = FieldsView.Cast<object>().Count();
        var selected = _fields.Count(f => f.IsSelected);
        FieldListSummary = $"Showing {visible} of {total} fields";
        FieldSelectionSummary = $"{selected} selected";
    }

    private void UpdateSelectedEntitySummary()
    {
        if (string.IsNullOrWhiteSpace(SelectedEntity))
        {
            SelectedEntitySummary = "No entity selected.";
            return;
        }

        var item = _entities.FirstOrDefault(e => string.Equals(e.Name, SelectedEntity, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            SelectedEntitySummary = "No entity selected.";
            return;
        }

        SelectedEntitySummary = $"{item.Name} | {item.PropertyCount} fields, {item.NavigationCount} nav properties";
    }

    private void ScheduleEntitySearchRefresh()
    {
        if (_syncContext is null)
        {
            EntitiesView.Refresh();
            UpdateEntitySummary();
            return;
        }

        _entitySearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _entitySearchCts = cts;
        _ = DebounceAsync(cts.Token, () =>
        {
            EntitiesView.Refresh();
            UpdateEntitySummary();
        });
    }

    private void ScheduleFieldSearchRefresh()
    {
        if (_syncContext is null)
        {
            FieldsView.Refresh();
            UpdateFieldSummary();
            return;
        }

        _fieldSearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _fieldSearchCts = cts;
        _ = DebounceAsync(cts.Token, () =>
        {
            FieldsView.Refresh();
            UpdateFieldSummary();
        });
    }

    private async Task DebounceAsync(CancellationToken token, Action action)
    {
        try
        {
            await Task.Delay(200, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }

        if (token.IsCancellationRequested) return;
        _syncContext!.Post(_ =>
        {
            if (!token.IsCancellationRequested)
            {
                action();
            }
        }, null);
    }

    private static Dictionary<string, ODataEnumType> BuildEnumLookup(IReadOnlyList<ODataEnumType> enums)
    {
        var lookup = new Dictionary<string, ODataEnumType>(StringComparer.OrdinalIgnoreCase);
        foreach (var enumType in enums)
        {
            if (!lookup.ContainsKey(enumType.Name))
            {
                lookup.Add(enumType.Name, enumType);
            }
            var shortName = enumType.Name.Split('.').Last();
            if (!lookup.ContainsKey(shortName))
            {
                lookup.Add(shortName, enumType);
            }
        }
        return lookup;
    }

    private EnumFieldInfo? ResolveEnumFieldInfo(string fieldName)
    {
        if (_enumFields is null || string.IsNullOrWhiteSpace(fieldName))
        {
            return null;
        }

        return _enumFields.TryGetValue(fieldName, out var info) ? info : null;
    }

    private static EnumFieldInfo? ResolveEnumInfo(Dictionary<string, ODataEnumType>? lookup, string type)
    {
        if (lookup is null || string.IsNullOrWhiteSpace(type)) return null;
        var normalized = type;
        if (normalized.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(")", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Collection(".Length, normalized.Length - "Collection(".Length - 1);
        }

        if (lookup.TryGetValue(normalized, out var enumType))
        {
            return new EnumFieldInfo(enumType.Name, enumType.Members);
        }

        return null;
    }

    private void RefreshFilterEnumProviders()
    {
        foreach (var node in FlattenFilters(RootGroup))
        {
            if (node is FilterConditionViewModel cond)
            {
                cond.ConfigureEnumProvider(ResolveEnumFieldInfo);
            }
        }
    }

    private static IEnumerable<FilterNodeViewModel> FlattenFilters(FilterNodeViewModel node)
    {
        yield return node;
        if (node is FilterGroupViewModel group)
        {
            foreach (var child in group.Children)
            {
                foreach (var nested in FlattenFilters(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private void ClearFilterTree()
    {
        // RootGroup stays hooked for future adds/removes; we only unhook and clear descendants.
        foreach (var child in RootGroup.Children.ToList())
        {
            UnhookFilterNode(child);
        }
        RootGroup.Children.Clear();
    }

    private void HookFilterNode(FilterNodeViewModel node)
    {
        if (_filterSubscriptions.Contains(node)) return;
        _filterSubscriptions.Add(node);
        node.PropertyChanged += FilterNodeChanged;
        if (node is FilterConditionViewModel cond)
        {
            cond.ConfigureEnumProvider(ResolveEnumFieldInfo);
        }
        if (node is FilterGroupViewModel group)
        {
            group.Children.CollectionChanged += FilterChildrenChanged;
            foreach (var child in group.Children)
            {
                HookFilterNode(child);
            }
        }
    }

    private void UnhookFilterNode(FilterNodeViewModel node)
    {
        if (!_filterSubscriptions.Contains(node)) return;

        node.PropertyChanged -= FilterNodeChanged;

        if (node is FilterGroupViewModel group)
        {
            group.Children.CollectionChanged -= FilterChildrenChanged;
            foreach (var child in group.Children.ToList())
            {
                UnhookFilterNode(child);
            }
        }

        _filterSubscriptions.Remove(node);
    }

    private void FilterChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems)
            {
                if (item is FilterNodeViewModel node)
                {
                    HookFilterNode(node);
                }
            }
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems)
            {
                if (item is FilterNodeViewModel node)
                {
                    UnhookFilterNode(node);
                }
            }
        }
        UpdateFilterPreview();
        UpdateQueryPreview();
    }

    private void FilterNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        UpdateFilterPreview();
        UpdateQueryPreview();
    }

    private void UpdateFilterPreview()
    {
        FilterUsageHint = string.IsNullOrWhiteSpace(FilterText)
            ? "Builder filter in use."
            : "Raw filter overrides the builder.";

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            if (RootGroup.Children.Count == 0)
            {
                FilterBuilderPreview = "No builder filter.";
            }
            else
            {
                var (validRawOverride, astRawOverride) = BuildFilterAst();
                FilterBuilderPreview = validRawOverride && astRawOverride is not null
                    ? RenderFilter(astRawOverride)
                    : "Builder has issues (ignored due to raw filter).";
            }

            var effectiveOverride = BuildEffectiveFilter(null);
            EffectiveFilterPreview = string.IsNullOrWhiteSpace(effectiveOverride) ? "No filter." : effectiveOverride;
            return;
        }

        var (valid, ast) = BuildFilterAst();

        if (!valid)
        {
            FilterBuilderPreview = ValidationWarning ?? "Builder filter has issues.";
            var effectiveInvalid = BuildEffectiveFilter(null);
            EffectiveFilterPreview = string.IsNullOrWhiteSpace(effectiveInvalid) ? "No filter." : effectiveInvalid;
            return;
        }

        if (ast is null)
        {
            FilterBuilderPreview = "No builder filter.";
        }
        else
        {
            FilterBuilderPreview = RenderFilter(ast);
        }

        var effective = BuildEffectiveFilter(ast);
        EffectiveFilterPreview = string.IsNullOrWhiteSpace(effective) ? "No filter." : effective;
    }

    private string? BuildEffectiveFilter(FilterNode? ast)
    {
        string? filter = null;

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            filter = FilterText;
        }
        else if (ast is not null)
        {
            filter = RenderFilter(ast);
        }

        if (!CrossCompany && !string.IsNullOrWhiteSpace(Company))
        {
            var companyClause = $"dataAreaId eq {QuoteStringLiteral(Company)}";
            filter = string.IsNullOrWhiteSpace(filter) ? companyClause : $"({companyClause}) and ({filter})";
        }

        return filter;
    }

    private static string QuoteStringLiteral(string value)
    {
        // OData string literals are single-quoted and escape a single quote by doubling it.
        var escaped = (value ?? string.Empty).Replace("'", "''");
        return $"'{escaped}'";
    }

    private void UpdateExpandSummary()
    {
        var expand = NormalizeExpand();
        if (string.IsNullOrWhiteSpace(ExpandPath))
        {
            ExpandSummary = "No expand selected.";
        }
        else if (_expandInvalid)
        {
            ExpandSummary = "Invalid expand path. Choose a navigation from the hints list.";
        }
        else
        {
            ExpandSummary = $"Using $expand={expand}";
        }
    }

    private void UpdateQueryPreview()
    {
        if (string.IsNullOrWhiteSpace(SelectedEntity))
        {
            QueryPreview = "Select an entity to preview the query.";
            return;
        }

        if (!TryBuildQuerySpec(out var spec, out var issue))
        {
            QueryPreview = $"Preview unavailable: {issue}";
            return;
        }

        var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        QueryPreview = request.Url;
    }

    private static string RenderFilter(FilterNode node)
    {
        return node switch
        {
            FilterCondition cond => RenderCondition(cond),
            FilterGroup group => $"({string.Join($" {group.LogicalOperator} ", group.Children.Select(RenderFilter))})",
            _ => string.Empty
        };
    }

    private static string RenderCondition(FilterCondition cond)
    {
        if (cond.Operator is "startswith" or "endswith" or "contains")
        {
            return $"{cond.Operator}({cond.Field},{cond.Value})";
        }

        return $"{cond.Field} {cond.Operator} {cond.Value}";
    }

    private async Task ExportPageAsync(CancellationToken cancellationToken)
    {
        var table = PreviewTable?.Table;
        if (table is null || table.Rows.Count == 0)
        {
            Status = "No data to export.";
            return;
        }
        var path = PromptForCsvPath();
        if (path == null) return;

        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await CsvExporter.ExportTableAsync(table, stream, cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
            Status = $"Exported page to {path}";
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            Status = "Export cancelled.";
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private async Task ExportAllAsync(CancellationToken cancellationToken)
    {
        var path = PromptForCsvPath();
        if (path == null) return;

        if (!TryBuildQueryRequest(out var request)) return;

        var tempPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await CsvExporter.ExportAsync(_ctx.OData, request, stream, total => Status = $"Exported {total} rows...", cancellationToken);
            }

            File.Move(tempPath, path, overwrite: true);
            Status = $"Exported to {path}";
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempPath);
            Status = "Export cancelled.";
        }
        catch
        {
            TryDeleteFile(tempPath);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private async Task LoadMoreAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_nextLink))
        {
            Status = "No more pages.";
            return;
        }

        var table = PreviewTable?.Table;
        if (table is null)
        {
            Status = "Run a preview first.";
            return;
        }

        try
        {
            Status = "Loading more...";
            var req = new QueryRequest(_nextLink);
            ODataPage? loaded = null;
            await foreach (var page in _ctx.OData.StreamAsync(req, cancellationToken))
            {
                loaded = page;
                var cols = table.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
                foreach (var row in page.Rows)
                {
                    var values = cols.Select(c => row.TryGetValue(c, out var v) ? v : null).ToArray();
                    table.Rows.Add(values);
                }
                SetNextLink(page.NextLink);
                break;
            }
            PreviewTable = table.DefaultView;

            if (loaded is not null)
            {
                _lastODataCount ??= loaded.ODataCount;
                ResponseDetails = FormatResponseDetails(loaded);
            }

            var countHint = _lastODataCount is not null ? $" | Count={_lastODataCount}" : string.Empty;
            Status = $"Loaded more rows (total {table.Rows.Count}){countHint}{(_nextLink is not null ? " | More available" : string.Empty)}";
        }
        catch (Exception ex)
        {
            Status = $"Load more failed: {ex.Message}";
            ResponseDetails = $"Load more failed: {ex.Message}";
        }
    }

    private string? PromptForCsvPath()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            FileName = "export.csv"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private async Task SaveCurrentQueryAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var suggested = $"Query-{DateTime.UtcNow:yyyyMMddHHmmss}";
            var name = PromptWindow.Show("Name for saved query:", suggested);
            if (string.IsNullOrWhiteSpace(name))
            {
                Status = "Save cancelled.";
                return;
            }

            var trimmed = name.Trim();
            var existing = (await _savedStore.LoadForEnvAsync(_ctx.CurrentEnv.Id))
                .FirstOrDefault(q => string.Equals(q.Name, trimmed, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !ConfirmOverwrite(trimmed))
            {
                Status = "Save cancelled (overwrite declined).";
                return;
            }

            var model = ToSavedModel(trimmed, existing);
            await _savedStore.SaveAsync(model);

            var loaded = await LoadSavedQueriesAsync();
            if (loaded)
            {
                Status = $"Saved query '{trimmed}'";
            }
        }
        catch (Exception ex)
        {
            Status = $"Save failed: {ex.Message}";
        }
    }

    private SavedQueryItem ToSavedModel(string name, SavedQueryItem? existing = null)
    {
        TryParseNullableInt(Top, minInclusive: 1, out var top);
        TryParseNullableInt(Skip, minInclusive: 0, out var skip);

        return new SavedQueryItem
        {
            Id = existing?.Id ?? string.Empty,
            EnvId = _ctx.CurrentEnv.Id,
            Name = name,
            Entity = SelectedEntity ?? string.Empty,
            CrossCompany = CrossCompany,
            Company = Company,
            Select = SelectedFields.ToList(),
            OrderBy = OrderBy,
            Top = top,
            Skip = skip,
            Count = Count,
            FilterText = FilterText,
            Expand = ExpandPath,
            FilterRoot = ToFilterDto(RootGroup),
            CreatedUtc = existing?.CreatedUtc
        };
    }

    private static FilterDto? ToFilterDto(FilterNodeViewModel node)
    {
        return node switch
        {
            FilterConditionViewModel cond => new FilterConditionDto { Field = cond.Field, Operator = cond.Operator, Value = cond.Value },
            FilterGroupViewModel grp => new FilterGroupDto
            {
                LogicalOperator = grp.LogicalOperator,
                Children = grp.Children.Select(ToFilterDto).Where(x => x is not null).ToList()!
            },
            _ => null
        };
    }

    private async Task<bool> LoadSavedQueriesAsync()
    {
        try
        {
            var items = await _savedStore.LoadForEnvAsync(_ctx.CurrentEnv.Id);
            _savedQueries.Clear();
            foreach (var item in items) _savedQueries.Add(item);
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Load saved queries failed: {ex.Message}";
            return false;
        }
    }

    private void LoadSelectedQuery()
    {
        if (SelectedSaved is null) return;
        ApplySaved(SelectedSaved);
        Status = $"Loaded saved query: {SelectedSaved.Name}";
    }

    private void ApplySaved(SavedQueryItem item)
    {
        SelectedEntity = item.Entity;
        CrossCompany = item.CrossCompany;
        Company = item.Company;
        OrderBy = item.OrderBy;
        Top = item.Top?.ToString();
        Skip = item.Skip?.ToString();
        Count = item.Count;
        FilterText = item.FilterText;
        ExpandPath = item.Expand;

        var selected = new HashSet<string>(item.Select ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        _ = ApplySavedFieldSelectionAsync(item.Entity, selected);

        ClearFilterTree();
        if (item.FilterRoot is not null)
        {
            RootGroup.Children.Add(FromFilterDto(item.FilterRoot, RootGroup));
        }
    }

    private async Task ApplySavedFieldSelectionAsync(string entityName, HashSet<string> selected)
    {
        try
        {
            await SelectedEntityDetailsTask.ConfigureAwait(true);
            if (!string.Equals(SelectedEntity, entityName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var field in _fields)
            {
                field.IsSelected = selected.Contains(field.Name);
            }
            RebuildSelectedFields();
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed applying saved field selection for {Entity}", entityName);
        }
    }

    private FilterNodeViewModel FromFilterDto(FilterDto dto, FilterGroupViewModel? parent)
    {
        switch (dto)
        {
            case FilterConditionDto cond:
                return new FilterConditionViewModel { Parent = parent, Field = cond.Field ?? string.Empty, Operator = cond.Operator ?? "eq", Value = cond.Value ?? string.Empty };
            case FilterGroupDto grp:
                var vm = new FilterGroupViewModel { Parent = parent, LogicalOperator = grp.LogicalOperator ?? "and" };
                foreach (var child in grp.Children ?? new List<FilterDto>())
                {
                    vm.Children.Add(FromFilterDto(child, vm));
                }
                return vm;
            default:
                return new FilterConditionViewModel { Parent = parent };
        }
    }

    private async Task DeleteSelectedQueryAsync(CancellationToken cancellationToken)
    {
        if (SelectedSaved is null) return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _savedStore.DeleteAsync(SelectedSaved);
            var loaded = await LoadSavedQueriesAsync();
            if (loaded)
            {
                Status = $"Deleted saved query: {SelectedSaved.Name}";
            }
        }
        catch (Exception ex)
        {
            Status = $"Delete failed: {ex.Message}";
        }
    }

    private void SetNextLink(string? next)
    {
        _nextLink = next;
        HasMoreNextLink = !string.IsNullOrWhiteSpace(_nextLink);
    }

    private void ResetPaging()
    {
        SetNextLink(null);
    }

    private async Task RenameSelectedQueryAsync(CancellationToken cancellationToken)
    {
        if (SelectedSaved is null) return;
        var renamed = PromptWindow.Show("Rename saved query:", SelectedSaved.Name);
        if (string.IsNullOrWhiteSpace(renamed)) return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            SelectedSaved.Name = renamed.Trim();
            await _savedStore.SaveAsync(SelectedSaved);
            var loaded = await LoadSavedQueriesAsync();
            if (loaded)
            {
                Status = $"Renamed to '{SelectedSaved.Name}'";
            }
        }
        catch (Exception ex)
        {
            Status = $"Rename failed: {ex.Message}";
        }
    }

    private static bool ConfirmOverwrite(string name)
    {
        var result = MessageBox.Show($"A saved query named '{name}' already exists. Overwrite it?", "Overwrite saved query", MessageBoxButton.YesNo, MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Action<Exception>? _onError;
    private readonly CancellationTokenSource _cts = new();

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Action<Exception>? onError = null)
    {
        _execute = execute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(_cts.Token);
        }
        catch (Exception ex)
        {
            if (_onError is not null)
            {
                _onError(ex);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default) => _execute(cancellationToken);
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}

public sealed class SavedQueryItem
{
    public string Id { get; set; } = string.Empty;
    public string EnvId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Entity { get; set; } = string.Empty;
    public bool CrossCompany { get; set; }
    public string? Company { get; set; }
    public List<string> Select { get; set; } = new();
    public string? OrderBy { get; set; }
    public int? Top { get; set; }
    public int? Skip { get; set; }
    public bool Count { get; set; }
    public string? FilterText { get; set; }
    public string? Expand { get; set; }
    public FilterDto? FilterRoot { get; set; }
    public string? CreatedUtc { get; set; }
    public string? UpdatedUtc { get; set; }
}

public abstract class FilterDto { }
public sealed class FilterConditionDto : FilterDto
{
    public string? Field { get; set; }
    public string? Operator { get; set; }
    public string? Value { get; set; }
}
public sealed class FilterGroupDto : FilterDto
{
    public string? LogicalOperator { get; set; }
    public List<FilterDto>? Children { get; set; }
}
