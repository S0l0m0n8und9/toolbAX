using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace TableEntityBrowserPlugin;

public sealed class TableEntityBrowserViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly ObservableCollection<TableInfoViewModel> _tables = new();
    private readonly ObservableCollection<EntityInfoViewModel> _entities = new();
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
    private ODataMetadata? _metadata;

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
        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, onCommandError);
        OpenTableBrowserCommand = new RelayCommand(_ => OpenTableBrowser());
        ImportTablesCommand = new AsyncRelayCommand(ImportTablesAsync, onCommandError);
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
                TablesView.Refresh();
                UpdateTableSummary();
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
                EntitiesView.Refresh();
                UpdateEntitySummary();
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
                UpdateSelectedEntityDetails();
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
    public AsyncRelayCommand RefreshAllCommand { get; }
    public RelayCommand OpenTableBrowserCommand { get; }
    public AsyncRelayCommand ImportTablesCommand { get; }
    public AsyncRelayCommand SaveTemplateCommand { get; }

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
        try
        {
            _metadata = await _ctx.Catalog.GetODataMetadataAsync(_ctx.CurrentEnv, CatalogRefreshMode.UseCacheIfFresh, ct);
            var ordered = _metadata.Entities.OrderBy(e => e.Name).ToList();
            var total = ordered.Count;
            var loaded = 0;
            foreach (var entity in ordered)
            {
                ct.ThrowIfCancellationRequested();
                _entities.Add(new EntityInfoViewModel(entity));
                loaded++;
                if (loaded % 200 == 0)
                {
                    Status = $"Loaded {loaded}/{total} entities...";
                    await Task.Yield(); // keep UI responsive while loading lots of rows
                }
            }
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
            return;
        }

        var deprecated = SelectedTable.IsDeprecated ? "Deprecated" : "Active";
        var viewFlag = SelectedTable.IsView ? "View" : "Table";
        var config = string.IsNullOrWhiteSpace(SelectedTable.ConfigurationKey) ? "No config key" : $"Config: {SelectedTable.ConfigurationKey}";
        SelectedTableSummary = $"{SelectedTable.Name} | {viewFlag} | {deprecated} | {config}";
        SelectedTableBrowserUrl = _ctx.Catalog.BuildTableBrowserUrl(_ctx.CurrentEnv, SelectedTable.Name);
    }

    private void UpdateSelectedEntityDetails()
    {
        _entityFields.Clear();
        _navigation.Clear();
        if (_metadata is null || SelectedEntity is null)
        {
            SelectedEntitySummary = "No entity selected.";
            SelectedEntityEndpoint = null;
            return;
        }

        var entity = _metadata.Entities.FirstOrDefault(e => string.Equals(e.Name, SelectedEntity.Name, StringComparison.OrdinalIgnoreCase));
        if (entity is null)
        {
            SelectedEntitySummary = "No entity selected.";
            SelectedEntityEndpoint = null;
            return;
        }

        var enumLookup = BuildEnumLookup(_metadata.Enums);
        foreach (var prop in entity.Properties.OrderBy(p => p.Name))
        {
            var enumValues = ResolveEnumValues(enumLookup, prop.Type);
            _entityFields.Add(new EntityFieldItem(prop.Name, prop.Type, prop.Nullable, enumValues));
        }

        foreach (var nav in entity.Navigations.OrderBy(n => n.Name))
        {
            _navigation.Add($"{nav.Name} ({nav.Type})");
        }

        SelectedEntitySummary = $"{entity.Name} | {entity.Properties.Count} fields, {entity.Navigations.Count} nav properties";
        SelectedEntityEndpoint = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, entity.Name);
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
    public EntityInfoViewModel(ODataEntity entity)
    {
        Name = entity.Name;
        PropertyCount = entity.Properties.Count;
        NavigationCount = entity.Navigations.Count;
    }

    public string Name { get; }
    public int PropertyCount { get; }
    public int NavigationCount { get; }
}

public sealed class EntityFieldItem
{
    public EntityFieldItem(string name, string type, bool nullable, string? enumValues)
    {
        Name = name;
        Type = type;
        Nullable = nullable;
        EnumValues = enumValues;
    }

    public string Name { get; }
    public string Type { get; }
    public bool Nullable { get; }
    public string? EnumValues { get; }
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
                Debug.WriteLine(ex);
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
