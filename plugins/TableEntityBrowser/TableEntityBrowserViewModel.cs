using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Collections;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace TableEntityBrowserPlugin;

public sealed class TableEntityBrowserViewModel : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions TemplateJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IPluginContext _ctx;
    private readonly ObservableCollection<TableInfoViewModel> _tables = new();
    private readonly BulkObservableCollection<EntityInfoViewModel> _entities = new();
    private readonly ObservableCollection<EntityFieldItem> _entityFields = new();
    private readonly ObservableCollection<string> _navigation = new();
    private string? _tableSearch;
    private string? _entitySearch;
    private TableInfoViewModel? _selectedTable;
    private EntityInfoViewModel? _selectedEntity;
    private string _status = "Ready";
    private string _tableListSummary = "0 tables";
    private string _entityListSummary = "0 entities";
    private string _selectedTableSummary = "No table selected.";
    private string _selectedEntitySummary = "No entity selected.";
    private string? _selectedTableBrowserUrl;
    private string? _selectedEntityEndpoint;
    private string _tableBrowserTemplate = string.Empty;
    private bool _isLoadingTables;
    private bool _isLoadingEntities;
    private ODataEntityIndex? _entityIndex;
    private Dictionary<string, ODataEnumType>? _enumLookup;
    private readonly SynchronizationContext? _syncContext = SynchronizationContext.Current;
    private CancellationTokenSource? _tableSearchCts;
    private CancellationTokenSource? _entitySearchCts;
    private CancellationTokenSource? _selectedEntityDetailsCts;
    private CancellationTokenSource? _entityCountCts;
    private string? _suggestedEntityForTable;

    public TableEntityBrowserViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        Tables = new ReadOnlyObservableCollection<TableInfoViewModel>(_tables);
        Entities = new ReadOnlyObservableCollection<EntityInfoViewModel>(_entities);
        EntityFields = new ReadOnlyObservableCollection<EntityFieldItem>(_entityFields);
        NavigationHints = new ReadOnlyObservableCollection<string>(_navigation);

        TablesView = CollectionViewSource.GetDefaultView(_tables);
        TablesView.Filter = TableFilter;
        EntitiesView = CollectionViewSource.GetDefaultView(_entities);
        EntitiesView.Filter = EntityFilter;

        Action<Exception> onCommandError = ex =>
        {
            _ctx.Logger.LogError(ex, "TableEntityBrowser command failed.");
            Status = $"Command failed: {ex.Message}";
        };

        LoadTablesCommand = new AsyncRelayCommand(LoadTablesAsync, onCommandError);
        LoadEntitiesCommand = new AsyncRelayCommand(LoadEntitiesAsync, onCommandError);
        RefreshEntitiesCommand = new AsyncRelayCommand(RefreshEntitiesAsync, onCommandError);
        RefreshSelectedEntityCommand = new AsyncRelayCommand(RefreshSelectedEntityAsync, onCommandError);
        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, onCommandError);
        OpenTableBrowserCommand = new RelayCommand(_ => OpenTableBrowser());
        CopyEntityEndpointCommand = new RelayCommand(_ => CopyEntityEndpoint());
        OpenInQueryBuilderCommand = new RelayCommand(_ => NavigateToPlugin("fo.querybuilder"));
        SendToApiBuilderCommand = new RelayCommand(_ => NavigateToPlugin("fo.odatapostbuilder"));
        ImportTablesCommand = new AsyncRelayCommand(ImportTablesAsync, onCommandError);
        SaveImportTemplateCommand = new AsyncRelayCommand(SaveImportTemplateAsync, onCommandError);
        SaveTemplateCommand = new AsyncRelayCommand(SaveTableBrowserTemplateAsync, onCommandError);

        _ = LoadTableBrowserTemplateAsync();
    }

    public ReadOnlyObservableCollection<TableInfoViewModel> Tables { get; }
    public ReadOnlyObservableCollection<EntityInfoViewModel> Entities { get; }
    public ReadOnlyObservableCollection<EntityFieldItem> EntityFields { get; }
    public ReadOnlyObservableCollection<string> NavigationHints { get; }
    public ICollectionView TablesView { get; }
    public ICollectionView EntitiesView { get; }

    public string? TableSearch
    {
        get => _tableSearch;
        set
        {
            if (_tableSearch != value)
            {
                _tableSearch = value;
                OnPropertyChanged();
                ScheduleTableSearchRefresh();
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

    public TableInfoViewModel? SelectedTable
    {
        get => _selectedTable;
        set
        {
            if (_selectedTable != value)
            {
                _selectedTable = value;
                OnPropertyChanged();
                UpdateSelectedTableDetails();
            }
        }
    }

    public EntityInfoViewModel? SelectedEntity
    {
        get => _selectedEntity;
        set
        {
            if (_selectedEntity != value)
            {
                _selectedEntity = value;
                OnPropertyChanged();
                StartLoadSelectedEntityDetails();
            }
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string TableListSummary
    {
        get => _tableListSummary;
        set { _tableListSummary = value; OnPropertyChanged(); }
    }

    public string EntityListSummary
    {
        get => _entityListSummary;
        set { _entityListSummary = value; OnPropertyChanged(); }
    }

    public string SelectedTableSummary
    {
        get => _selectedTableSummary;
        set { _selectedTableSummary = value; OnPropertyChanged(); }
    }

    public string SelectedEntitySummary
    {
        get => _selectedEntitySummary;
        set { _selectedEntitySummary = value; OnPropertyChanged(); }
    }

    public string? SelectedTableBrowserUrl
    {
        get => _selectedTableBrowserUrl;
        set { _selectedTableBrowserUrl = value; OnPropertyChanged(); }
    }

    public string? SelectedEntityEndpoint
    {
        get => _selectedEntityEndpoint;
        set { _selectedEntityEndpoint = value; OnPropertyChanged(); }
    }

    public string TableBrowserTemplate
    {
        get => _tableBrowserTemplate;
        set { _tableBrowserTemplate = value; OnPropertyChanged(); }
    }

    public bool IsLoadingTables
    {
        get => _isLoadingTables;
        set
        {
            _isLoadingTables = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsLoadingEntities
    {
        get => _isLoadingEntities;
        set
        {
            _isLoadingEntities = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsBusy => IsLoadingTables || IsLoadingEntities;

    public AsyncRelayCommand LoadTablesCommand { get; }
    public AsyncRelayCommand LoadEntitiesCommand { get; }
    public AsyncRelayCommand RefreshEntitiesCommand { get; }
    public AsyncRelayCommand RefreshSelectedEntityCommand { get; }
    public AsyncRelayCommand RefreshAllCommand { get; }
    public RelayCommand OpenTableBrowserCommand { get; }
    public RelayCommand CopyEntityEndpointCommand { get; }
    /// <summary>Opens the selected entity in the Query Builder plugin, if available.</summary>
    public RelayCommand OpenInQueryBuilderCommand { get; }
    /// <summary>Sends the selected entity to the OData API Builder plugin, if available.</summary>
    public RelayCommand SendToApiBuilderCommand { get; }
    public AsyncRelayCommand ImportTablesCommand { get; }
    public AsyncRelayCommand SaveImportTemplateCommand { get; }
    public AsyncRelayCommand SaveTemplateCommand { get; }

    /// <summary>
    /// The name of the entity that most closely matches the selected table, if one was found.
    /// </summary>
    public string? SuggestedEntityForTable
    {
        get => _suggestedEntityForTable;
        private set { _suggestedEntityForTable = value; OnPropertyChanged(); }
    }

    private async Task LoadTablesAsync(CancellationToken ct)
    {
        Status = "Loading tables...";
        IsLoadingTables = true;
        _tables.Clear();
        try
        {
            var catalog = await _ctx.Catalog.GetTablesAsync(_ctx.CurrentEnv, CatalogRefreshMode.UseCacheIfFresh, ct);
            foreach (var table in catalog.Tables.OrderBy(t => t.Name))
            {
                _tables.Add(new TableInfoViewModel(table));
            }
            TablesView.Refresh();
            UpdateTableSummary();
            Status = $"Loaded {catalog.Tables.Count} tables (v{catalog.Version}).";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Table load failed for {Env}", _ctx.CurrentEnv.Name);
            Status = $"Table load failed: {ex.Message}";
        }
        finally
        {
            IsLoadingTables = false;
        }
    }

    private async Task LoadEntitiesAsync(CancellationToken ct)
    {
        Status = "Loading entities...";
        IsLoadingEntities = true;
        _entities.Clear();
        _entityFields.Clear();
        _navigation.Clear();
        _selectedEntityDetailsCts?.Cancel();
        try
        {
            _entityIndex = await _ctx.Catalog.GetODataEntityIndexAsync(_ctx.CurrentEnv, CatalogRefreshMode.UseCacheIfAvailable, ct);
            _enumLookup = BuildEnumLookup(_entityIndex.Enums);
            Status = "Populating entity list...";
            var ordered = _entityIndex.Entities
                .OrderBy(e => e.Name)
                .Select(e => new EntityInfoViewModel(e.Name, e.PropertyCount, e.NavigationCount))
                .ToList();
            _entities.ReplaceAll(ordered);
            EntitiesView.Refresh();
            UpdateEntitySummary();
            Status = $"Loaded {_entities.Count} entities.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Entity load failed for {Env}", _ctx.CurrentEnv.Name);
            Status = $"Entity load failed: {ex.Message}";
        }
        finally
        {
            IsLoadingEntities = false;
        }
    }

    private async Task RefreshEntitiesAsync(CancellationToken ct)
    {
        Status = "Refreshing entities...";
        await _ctx.Catalog.RefreshAsync(_ctx.CurrentEnv, CatalogRefreshScope.ODataMetadata, ct);
        await LoadEntitiesAsync(ct);
    }

    private async Task RefreshSelectedEntityAsync(CancellationToken ct)
    {
        if (SelectedEntity is null)
        {
            Status = "Select an entity first.";
            return;
        }

        UpdateSelectedEntityDetails();
        _selectedEntityDetailsCts?.Cancel();
        var cts = new CancellationTokenSource();
        _selectedEntityDetailsCts = cts;
        await LoadSelectedEntityDetailsAsync(SelectedEntity.Name, CatalogRefreshMode.ForceRefresh, cts.Token);
        Status = $"Refreshed entity: {SelectedEntity.Name}";
    }

    private async Task RefreshAllAsync(CancellationToken ct)
    {
        Status = "Refreshing catalog...";
        await _ctx.Catalog.RefreshAsync(_ctx.CurrentEnv, CatalogRefreshScope.All, ct);
        await LoadTablesAsync(ct);
        await LoadEntitiesAsync(ct);
    }

    private async Task ImportTablesAsync(CancellationToken ct)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(dlg.FileName, ct);
            var catalog = await _ctx.Catalog.ImportTableCatalogAsync(_ctx.CurrentEnv, json, ct);
            _tables.Clear();
            foreach (var table in catalog.Tables.OrderBy(t => t.Name))
            {
                _tables.Add(new TableInfoViewModel(table));
            }
            TablesView.Refresh();
            UpdateTableSummary();
            Status = $"Imported {catalog.Tables.Count} tables (v{catalog.Version}).";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Import tables failed. File={File}", Path.GetFileName(dlg.FileName));
            Status = $"Import failed: {ex.Message}";
        }
    }

    private async Task SaveImportTemplateAsync(CancellationToken ct)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "table-catalog.import.template.json",
            OverwritePrompt = true
        };
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var json = BuildTableCatalogImportTemplateJson();
            await File.WriteAllTextAsync(dlg.FileName, json, Encoding.UTF8, ct);
            Status = $"Template saved: {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Save import template failed. File={File}", Path.GetFileName(dlg.FileName));
            Status = $"Template save failed: {ex.Message}";
        }
    }

    private async Task LoadTableBrowserTemplateAsync()
    {
        try
        {
            TableBrowserTemplate = await _ctx.Catalog.GetTableBrowserUrlTemplateAsync();
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Template load failed for {Env}", _ctx.CurrentEnv.Name);
            Status = $"Template load failed: {ex.Message}";
        }
    }

    private async Task SaveTableBrowserTemplateAsync(CancellationToken ct)
    {
        try
        {
            await _ctx.Catalog.SetTableBrowserUrlTemplateAsync(TableBrowserTemplate, ct);
            Status = "Table browser template saved.";
            UpdateSelectedTableDetails();
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Template save failed for {Env}", _ctx.CurrentEnv.Name);
            Status = $"Template save failed: {ex.Message}";
        }
    }

    private static string BuildTableCatalogImportTemplateJson()
    {
        // Notes:
        // - `source` and `updatedUtc` are ignored on import (the app overwrites them).
        // - Keep the property names (camelCase) and booleans as shown.
        var template = new TableCatalog(
            Version: "unknown",
            Source: "UserImport",
            UpdatedUtc: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Tables: new List<TableInfo>
            {
                new(
                    Name: "REPLACE_ME_TABLE_NAME",
                    Label: "Optional label (what you want shown in the UI)",
                    IsView: false,
                    ConfigurationKey: null,
                    IsDeprecated: false,
                    Notes: "Optional notes (free text)."),
                new(
                    Name: "ANOTHER_TABLE_NAME",
                    Label: null,
                    IsView: true,
                    ConfigurationKey: "Optional config key",
                    IsDeprecated: false,
                    Notes: null)
            });

        return JsonSerializer.Serialize(template, TemplateJsonOptions);
    }

    private void OpenTableBrowser()
    {
        if (SelectedTable is null)
        {
            Status = "Select a table first.";
            return;
        }

        var url = _ctx.Catalog.BuildTableBrowserUrl(_ctx.CurrentEnv, SelectedTable.Name);
        SelectedTableBrowserUrl = url;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to open browser.");
            Status = $"Failed to open browser: {ex.Message}";
        }
    }

    private void UpdateSelectedTableDetails()
    {
        if (SelectedTable is null)
        {
            SelectedTableSummary = "No table selected.";
            SelectedTableBrowserUrl = null;
            SuggestedEntityForTable = null;
            return;
        }

        var deprecated = SelectedTable.IsDeprecated ? "Deprecated" : "Active";
        var viewFlag = SelectedTable.IsView ? "View" : "Table";
        var config = string.IsNullOrWhiteSpace(SelectedTable.ConfigurationKey) ? "No config key" : $"Config: {SelectedTable.ConfigurationKey}";

        var suggested = FindEntityForTable(SelectedTable.Name);
        SuggestedEntityForTable = suggested;
        var entityHint = suggested is not null ? $" | Likely entity: {suggested}" : string.Empty;

        SelectedTableSummary = $"{SelectedTable.Name} | {viewFlag} | {deprecated} | {config}{entityHint}";
        SelectedTableBrowserUrl = _ctx.Catalog.BuildTableBrowserUrl(_ctx.CurrentEnv, SelectedTable.Name);
    }

    private void UpdateSelectedEntityDetails()
    {
        _entityFields.Clear();
        _navigation.Clear();
        _entityCountCts?.Cancel();
        if (SelectedEntity is null)
        {
            SelectedEntitySummary = "No entity selected.";
            SelectedEntityEndpoint = null;
            return;
        }

        SelectedEntitySummary = $"{SelectedEntity.Name} | {SelectedEntity.PropertyCount} fields, {SelectedEntity.NavigationCount} nav properties | count: ...";
        SelectedEntityEndpoint = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, SelectedEntity.Name);

        var cts = new CancellationTokenSource();
        _entityCountCts = cts;
        _ = LoadEntityCountAsync(SelectedEntity.Name, cts.Token);
    }

    private void StartLoadSelectedEntityDetails()
    {
        UpdateSelectedEntityDetails();
        _selectedEntityDetailsCts?.Cancel();
        if (SelectedEntity is null) return;

        var cts = new CancellationTokenSource();
        _selectedEntityDetailsCts = cts;
        _ = LoadSelectedEntityDetailsAsync(SelectedEntity.Name, CatalogRefreshMode.UseCacheIfAvailable, cts.Token);
    }

    private async Task LoadEntityCountAsync(string entityName, CancellationToken ct)
    {
        try
        {
            var spec = new QuerySpec(Entity: entityName, CrossCompany: true, Top: 0, Count: true);
            var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
            long? count = null;
            await foreach (var page in _ctx.OData.StreamAsync(request, ct).ConfigureAwait(false))
            {
                count = page.ODataCount;
                break;
            }

            if (ct.IsCancellationRequested) return;
            if (SelectedEntity is null || !string.Equals(SelectedEntity.Name, entityName, StringComparison.OrdinalIgnoreCase)) return;

            var countText = count.HasValue ? $"~{count.Value:N0} records" : "count unavailable";
            SelectedEntitySummary = $"{entityName} | {SelectedEntity.PropertyCount} fields, {SelectedEntity.NavigationCount} nav properties | {countText}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Entity count failed for {Entity}", entityName);
            if (!ct.IsCancellationRequested && SelectedEntity?.Name == entityName)
            {
                SelectedEntitySummary = $"{entityName} | {SelectedEntity.PropertyCount} fields, {SelectedEntity.NavigationCount} nav properties | count unavailable";
            }
        }
    }

    private void CopyEntityEndpoint()
    {
        if (string.IsNullOrWhiteSpace(SelectedEntityEndpoint))
        {
            Status = "No entity selected.";
            return;
        }

        Clipboard.SetText(SelectedEntityEndpoint);
        Status = $"Copied: {SelectedEntityEndpoint}";
    }

    private void NavigateToPlugin(string targetPluginId)
    {
        if (SelectedEntity is null)
        {
            Status = "Select an entity first.";
            return;
        }

        if (_ctx is not IPluginContextNavigation nav)
        {
            Status = "Cross-plugin navigation is not supported by this host.";
            return;
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity"] = SelectedEntity.Name
        };

        if (!nav.TryNavigateTo(targetPluginId, parameters))
        {
            Status = $"Plugin '{targetPluginId}' is not available or does not support navigation.";
        }
    }

    private string? FindEntityForTable(string tableName)
    {
        if (_entities.Count == 0 || string.IsNullOrWhiteSpace(tableName))
        {
            return null;
        }

        // Normalize the table name: remove underscores, lowercase for comparison.
        var normalizedTable = tableName.Replace("_", "").ToUpperInvariant();

        string? bestMatch = null;
        var bestScore = 0;

        foreach (var entity in _entities)
        {
            var normalizedEntity = entity.Name.ToUpperInvariant();
            int score;

            if (string.Equals(normalizedEntity, normalizedTable, StringComparison.OrdinalIgnoreCase))
            {
                // Exact match after normalization — return immediately.
                return entity.Name;
            }
            else if (normalizedEntity.StartsWith(normalizedTable, StringComparison.OrdinalIgnoreCase) ||
                     normalizedTable.StartsWith(normalizedEntity, StringComparison.OrdinalIgnoreCase))
            {
                score = 80 + Math.Min(normalizedEntity.Length, normalizedTable.Length);
            }
            else if (normalizedEntity.Contains(normalizedTable, StringComparison.OrdinalIgnoreCase))
            {
                score = 50 + normalizedTable.Length;
            }
            else if (normalizedTable.Contains(normalizedEntity, StringComparison.OrdinalIgnoreCase))
            {
                score = 40 + normalizedEntity.Length;
            }
            else
            {
                continue;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestMatch = entity.Name;
            }
        }

        return bestMatch;
    }

    private async Task LoadSelectedEntityDetailsAsync(string entityName, CatalogRefreshMode mode, CancellationToken ct)
    {
        try
        {
            var entity = await _ctx.Catalog.GetODataEntityDetailsAsync(_ctx.CurrentEnv, entityName, mode, ct);
            if (ct.IsCancellationRequested) return;
            if (entity is null) return;
            if (SelectedEntity is null || !string.Equals(SelectedEntity.Name, entityName, StringComparison.OrdinalIgnoreCase)) return;

            var lookup = _enumLookup ?? new Dictionary<string, ODataEnumType>(StringComparer.OrdinalIgnoreCase);

            _entityFields.Clear();
            foreach (var prop in entity.Properties.OrderBy(p => p.Name))
            {
                var enumValues = ResolveEnumValues(lookup, prop.Type);
                _entityFields.Add(new EntityFieldItem(
                    prop.Name,
                    prop.Type,
                    prop.Nullable,
                    prop.IsKey,
                    prop.IsMandatory,
                    enumValues,
                    prop.MaxLength,
                    prop.Precision,
                    prop.Scale,
                    prop.MinValue,
                    prop.MaxValue));
            }

            _navigation.Clear();
            foreach (var nav in entity.Navigations.OrderBy(n => n.Name))
            {
                _navigation.Add($"{nav.Name} ({nav.Type})");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load entity details for {Entity}", entityName);
            Status = $"Entity details load failed: {ex.Message}";
        }
    }

    private void ScheduleTableSearchRefresh()
    {
        if (_syncContext is null)
        {
            TablesView.Refresh();
            UpdateTableSummary();
            return;
        }

        _tableSearchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _tableSearchCts = cts;
        _ = DebounceAsync(cts.Token, () =>
        {
            TablesView.Refresh();
            UpdateTableSummary();
        });
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

    private static string ResolveEnumValues(Dictionary<string, ODataEnumType> lookup, string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;
        var normalized = type;
        if (normalized.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith(")", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Collection(".Length, normalized.Length - "Collection(".Length - 1);
        }

        if (lookup.TryGetValue(normalized, out var enumType))
        {
            return string.Join(", ", enumType.Members);
        }

        return string.Empty;
    }

    private bool TableFilter(object? item)
    {
        if (item is not TableInfoViewModel table) return false;
        if (string.IsNullOrWhiteSpace(TableSearch)) return true;
        var term = TableSearch.Trim();
        return table.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (table.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || (table.Notes?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool EntityFilter(object? item)
    {
        if (item is not EntityInfoViewModel entity) return false;
        if (string.IsNullOrWhiteSpace(EntitySearch)) return true;
        var term = EntitySearch.Trim();
        return entity.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateTableSummary()
    {
        var total = _tables.Count;
        var visible = TablesView.Cast<object>().Count();
        TableListSummary = $"Showing {visible} of {total} tables";
    }

    private void UpdateEntitySummary()
    {
        var total = _entities.Count;
        var visible = EntitiesView.Cast<object>().Count();
        EntityListSummary = $"Showing {visible} of {total} entities";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class TableInfoViewModel
{
    public TableInfoViewModel(TableInfo info)
    {
        Name = info.Name;
        Label = info.Label;
        IsView = info.IsView;
        ConfigurationKey = info.ConfigurationKey;
        IsDeprecated = info.IsDeprecated;
        Notes = info.Notes;
    }

    public string Name { get; }
    public string? Label { get; }
    public bool IsView { get; }
    public string? ConfigurationKey { get; }
    public bool IsDeprecated { get; }
    public string? Notes { get; }
}

public sealed class EntityInfoViewModel
{
    public EntityInfoViewModel(string name, int propertyCount, int navigationCount)
    {
        Name = name;
        PropertyCount = propertyCount;
        NavigationCount = navigationCount;
    }

    public string Name { get; }
    public int PropertyCount { get; }
    public int NavigationCount { get; }
}

public sealed class EntityFieldItem
{
    public EntityFieldItem(
        string name,
        string type,
        bool nullable,
        bool isKey,
        bool isMandatory,
        string? enumValues,
        string? maxLength = null,
        string? precision = null,
        string? scale = null,
        string? minValue = null,
        string? maxValue = null)
    {
        Name = name;
        Type = type;
        Nullable = nullable;
        IsKey = isKey;
        IsMandatory = isMandatory;
        EnumValues = enumValues;
        MaxLength = maxLength;
        Precision = precision;
        Scale = scale;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    public string Name { get; }
    public string Type { get; }
    public bool Nullable { get; }
    public bool IsKey { get; }
    public bool IsMandatory { get; }
    public bool Mandatory => IsKey || IsMandatory;
    public string? EnumValues { get; }
    public string? MaxLength { get; }
    public string? Precision { get; }
    public string? Scale { get; }
    public string? MinValue { get; }
    public string? MaxValue { get; }
    public string? PrecisionScale => string.IsNullOrWhiteSpace(Precision) && string.IsNullOrWhiteSpace(Scale)
        ? null
        : $"{(string.IsNullOrWhiteSpace(Precision) ? "-" : Precision)}/{(string.IsNullOrWhiteSpace(Scale) ? "-" : Scale)}";
    public string? Range => string.IsNullOrWhiteSpace(MinValue) && string.IsNullOrWhiteSpace(MaxValue)
        ? null
        : $"{(string.IsNullOrWhiteSpace(MinValue) ? "-" : MinValue)} .. {(string.IsNullOrWhiteSpace(MaxValue) ? "-" : MaxValue)}";
}

