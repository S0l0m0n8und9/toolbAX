using FoToolbox.Core.OData;
using FoToolbox.Core.Export;
using FoToolbox.SDK.Plugins;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace QueryBuilderPlugin;

public sealed class QueryBuilderViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly IMetadataProvider _metadataProvider;
    private readonly SavedQueryStore _savedStore;
    private ODataMetadata? _metadata;
    private readonly ObservableCollection<string> _entities = new();
    private readonly ObservableCollection<string> _fields = new();
    private readonly ObservableCollection<string> _navigation = new();
    private readonly ObservableCollection<string> _selectedFields = new();
    private readonly ObservableCollection<SavedQueryItem> _savedQueries = new();
    private string? _selectedEntity;
    private string? _orderBy;
    private string? _filterText;
    private string? _expandPath;
    private bool _crossCompany = true;
    private string? _company;
    private bool _count;
    private string _status = "Ready";
    private string? _nextLink;
    private bool _expandInvalid;
    private bool _hasMoreNextLink;
    private string? _validationWarning;
    private string? _expandWarning;
    private DataView? _preview;
    private SavedQueryItem? _selectedSaved;

    public FilterGroupViewModel RootGroup { get; } = new() { LogicalOperator = "and" };

    public QueryBuilderViewModel(IPluginContext ctx, IMetadataProvider metadataProvider)
    {
        _ctx = ctx;
        _metadataProvider = metadataProvider;
        _savedStore = new SavedQueryStore(Path.Combine(AppContext.BaseDirectory, "querybuilder.saved.json"));
        Entities = new ReadOnlyObservableCollection<string>(_entities);
        Fields = new ReadOnlyObservableCollection<string>(_fields);
        NavigationHints = new ReadOnlyObservableCollection<string>(_navigation);
        SelectedFields = new ReadOnlyObservableCollection<string>(_selectedFields);
        SavedQueries = new ReadOnlyObservableCollection<SavedQueryItem>(_savedQueries);

        LoadEntitiesCommand = new AsyncRelayCommand(LoadEntitiesAsync);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync);
        AddConditionCommand = new RelayCommand(_ => AddCondition(RootGroup));
        AddGroupCommand = new RelayCommand(_ => AddGroup(RootGroup));
        RemoveNodeCommand = new RelayCommand(RemoveNode);
        ExportPageCommand = new AsyncRelayCommand(ExportPageAsync);
        ExportAllCommand = new AsyncRelayCommand(ExportAllAsync);
        LoadMoreCommand = new AsyncRelayCommand(LoadMoreAsync);
        SaveQueryCommand = new RelayCommand(_ => SaveCurrentQuery());
        LoadSavedQueryCommand = new RelayCommand(_ => LoadSelectedQuery());
        DeleteSavedQueryCommand = new RelayCommand(_ => DeleteSelectedQuery());
        RenameSavedQueryCommand = new RelayCommand(_ => RenameSelectedQuery());

        LoadSavedQueriesAsync().GetAwaiter().GetResult();
    }

    public ReadOnlyObservableCollection<string> Entities { get; }
    public ReadOnlyObservableCollection<string> Fields { get; }
    public ReadOnlyObservableCollection<string> NavigationHints { get; }
    public ReadOnlyObservableCollection<string> SelectedFields { get; }
    public ReadOnlyObservableCollection<SavedQueryItem> SavedQueries { get; }
    public string FilterHint => "Operators: eq/ne/gt/ge/lt/le, startswith(value), endswith(value), contains(*value*). When cross-company is off and a company is set, dataAreaId is injected automatically.";

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
                _navigation.Clear();
                _selectedFields.Clear();
                PreviewTable = null;
                SetNextLink(null);
                PopulateFieldsForSelection();
            }
        }
    }

    public string? OrderBy
    {
        get => _orderBy;
        set { _orderBy = value; OnPropertyChanged(); ResetPaging(); }
    }

    public string? FilterText
    {
        get => _filterText;
        set { _filterText = value; OnPropertyChanged(); ResetPaging(); }
    }

    public string? ExpandPath
    {
        get => _expandPath;
        set { _expandPath = value; OnPropertyChanged(); ResetPaging(); }
    }

    public bool CrossCompany
    {
        get => _crossCompany;
        set { _crossCompany = value; OnPropertyChanged(); ResetPaging(); }
    }

    public string? Company
    {
        get => _company;
        set { _company = value; OnPropertyChanged(); ResetPaging(); }
    }

    public bool Count
    {
        get => _count;
        set { _count = value; OnPropertyChanged(); }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
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
    public AsyncRelayCommand PreviewCommand { get; }
    public RelayCommand AddConditionCommand { get; }
    public RelayCommand AddGroupCommand { get; }
    public RelayCommand RemoveNodeCommand { get; }
    public AsyncRelayCommand ExportPageCommand { get; }
    public AsyncRelayCommand ExportAllCommand { get; }
    public AsyncRelayCommand LoadMoreCommand { get; }
    public RelayCommand SaveQueryCommand { get; }
    public RelayCommand LoadSavedQueryCommand { get; }
    public RelayCommand DeleteSavedQueryCommand { get; }
    public RelayCommand RenameSavedQueryCommand { get; }

    private async Task LoadEntitiesAsync(CancellationToken cancellationToken)
    {
        Status = "Loading entities...";
        _entities.Clear();
        _fields.Clear();
        _navigation.Clear();
        _selectedFields.Clear();
        RootGroup.Children.Clear();
        PreviewTable = null;
        SetNextLink(null);

        try
        {
            _metadata = await _metadataProvider.GetMetadataAsync(_ctx.CurrentEnv.Id, _ctx.CurrentEnv.BaseUrl, cancellationToken);
            foreach (var entity in _metadata.Entities.OrderBy(e => e.Name))
            {
                _entities.Add(entity.Name);
            }
            Status = "Pick an entity, then choose fields and filters.";
        }
        catch (Exception ex)
        {
            Status = $"Metadata load failed: {ex.Message}";
        }
    }

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildQueryRequest(out var request))
        {
            return;
        }

        Status = "Running query...";
        SetNextLink(null);
        try
        {
            int rowCount = 0;
            DataTable? table = null;
            await foreach (var page in _ctx.OData.StreamAsync(request, cancellationToken))
            {
                if (table == null)
                {
                    table = BuildTable(page);
                }
                rowCount += page.Rows.Count;
                SetNextLink(page.NextLink);
                break; // first page only for now
            }
            PreviewTable = table?.DefaultView;
            Status = $"{rowCount} rows (first page){(_nextLink is not null ? " | More available" : string.Empty)}";
        }
        catch (Exception ex)
        {
            Status = $"Preview failed: {ex.Message}";
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

    private void PopulateFieldsForSelection()
    {
        if (string.IsNullOrWhiteSpace(_selectedEntity))
        {
            return;
        }
        var entity = _metadata?.Entities.FirstOrDefault(e => string.Equals(e.Name, _selectedEntity, StringComparison.OrdinalIgnoreCase));
        if (entity is null) return;

        foreach (var prop in entity.Properties)
        {
            _fields.Add(prop.Name);
        }
        foreach (var nav in entity.Navigations)
        {
            _fields.Add(nav.Name);
            _navigation.Add(nav.Name);
        }

        ResetPaging();
    }

    public void UpdateSelectedFields(System.Collections.IList selectedItems)
    {
        _selectedFields.Clear();
        foreach (var item in selectedItems)
        {
            if (item is string s)
            {
                _selectedFields.Add(s);
            }
        }
        OnPropertyChanged(nameof(SelectedFields));
        ResetPaging();
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
        ValidationWarning = null;

        if (string.IsNullOrWhiteSpace(SelectedEntity))
        {
            ValidationWarning = "Select an entity before running.";
            Status = ValidationWarning;
            return false;
        }

        var (validFilter, filterNode) = BuildFilterAst();
        if (!validFilter)
        {
            Status = ValidationWarning ?? "Fix validation issues in the filter builder.";
            return false;
        }

        var expand = NormalizeExpand();
        if (_expandInvalid)
        {
            ValidationWarning = ExpandWarning ?? "Invalid expand path.";
            Status = ValidationWarning!;
            return false;
        }

        var spec = new QuerySpec(
            Entity: SelectedEntity ?? string.Empty,
            CrossCompany: CrossCompany,
            Company: Company,
            Select: SelectedFields.ToList(),
            OrderBy: OrderBy,
            Count: Count,
            Filter: string.IsNullOrWhiteSpace(FilterText) ? null : FilterText,
            Where: filterNode,
            Expand: expand);

        request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
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
        if (_navigation.Count > 0 && !_navigation.Contains(expand))
        {
            ExpandWarning = (ExpandWarning is null ? string.Empty : $"{ExpandWarning} ") + "Pick a navigation from the hints list.";
            _expandInvalid = true;
        }
        return expand;
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
    }

    private void AddGroup(FilterGroupViewModel parent)
    {
        var group = new FilterGroupViewModel { LogicalOperator = "and", Parent = parent };
        parent.Children.Add(group);
    }

    private void RemoveNode(object? parameter)
    {
        if (parameter is not FilterNodeViewModel node || node.Parent is null) return;
        node.Parent.Children.Remove(node);
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
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await CsvExporter.ExportTableAsync(table, stream, cancellationToken);
        Status = $"Exported page to {path}";
    }

    private async Task ExportAllAsync(CancellationToken cancellationToken)
    {
        var path = PromptForCsvPath();
        if (path == null) return;

        if (!TryBuildQueryRequest(out var request)) return;

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await CsvExporter.ExportAsync(_ctx.OData, request, stream, rows => Status = $"Exported {rows} rows...", cancellationToken);
        Status = $"Exported to {path}";
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
            await foreach (var page in _ctx.OData.StreamAsync(req, cancellationToken))
            {
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
            Status = $"Loaded more rows (total {table.Rows.Count}){(_nextLink is not null ? " | More available" : string.Empty)}";
        }
        catch (Exception ex)
        {
            Status = $"Load more failed: {ex.Message}";
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

    private async void SaveCurrentQuery()
    {
        var suggested = $"Query-{DateTime.UtcNow:yyyyMMddHHmmss}";
        var name = PromptWindow.Show("Name for saved query:", suggested);
        if (string.IsNullOrWhiteSpace(name))
        {
            Status = "Save cancelled.";
            return;
        }

        var trimmed = name.Trim();
        var existing = _savedQueries.FirstOrDefault(q => string.Equals(q.Name, trimmed, StringComparison.OrdinalIgnoreCase) && q.EnvId == _ctx.CurrentEnv.Id);
        if (existing is not null && !ConfirmOverwrite(trimmed))
        {
            Status = "Save cancelled (overwrite declined).";
            return;
        }

        var model = ToSavedModel(trimmed, existing);
        await _savedStore.SaveAsync(model);
        await LoadSavedQueriesAsync();
        Status = $"Saved query '{trimmed}'";
    }

    private SavedQueryItem ToSavedModel(string name, SavedQueryItem? existing = null)
    {
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

    private async Task LoadSavedQueriesAsync()
    {
        _savedQueries.Clear();
        var items = await _savedStore.LoadForEnvAsync(_ctx.CurrentEnv.Id);
        foreach (var item in items) _savedQueries.Add(item);
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
        Count = item.Count;
        FilterText = item.FilterText;
        ExpandPath = item.Expand;

        PopulateFieldsForSelection();
        _selectedFields.Clear();
        foreach (var s in item.Select)
        {
            _selectedFields.Add(s);
        }
        OnPropertyChanged(nameof(SelectedFields));

        RootGroup.Children.Clear();
        if (item.FilterRoot is not null)
        {
            RootGroup.Children.Add(FromFilterDto(item.FilterRoot, RootGroup));
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

    private void DeleteSelectedQuery()
    {
        if (SelectedSaved is null) return;
        _savedStore.DeleteAsync(SelectedSaved).GetAwaiter().GetResult();
        LoadSavedQueriesAsync().GetAwaiter().GetResult();
        Status = $"Deleted saved query: {SelectedSaved.Name}";
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

    private void RenameSelectedQuery()
    {
        if (SelectedSaved is null) return;
        var renamed = PromptWindow.Show("Rename saved query:", SelectedSaved.Name);
        if (string.IsNullOrWhiteSpace(renamed)) return;
        SelectedSaved.Name = renamed.Trim();
        _savedStore.SaveAsync(SelectedSaved).GetAwaiter().GetResult();
        LoadSavedQueriesAsync().GetAwaiter().GetResult();
        Status = $"Renamed to '{SelectedSaved.Name}'";
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
    private readonly CancellationTokenSource _cts = new();

    public AsyncRelayCommand(Func<CancellationToken, Task> execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(_cts.Token);
        }
        catch
        {
            // swallow for now; could log via context
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
