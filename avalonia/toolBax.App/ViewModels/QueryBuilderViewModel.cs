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

    public ObservableCollection<EntitySet> Entities { get; }
    public ObservableCollection<FieldChipViewModel> Fields { get; } = new();
    public ObservableCollection<QueryResultRow> ResultRows { get; } = new();

    [ObservableProperty]
    private EntitySet? _selectedEntity;

    [ObservableProperty]
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

    [ObservableProperty]
    private int _top = 50;

    [ObservableProperty]
    private int _skip;

    /// <summary>Include <c>$count=true</c> (total matching rows).</summary>
    [ObservableProperty]
    private bool _count;

    /// <summary>Query across all legal entities (<c>cross-company=true</c>).</summary>
    [ObservableProperty]
    private bool _crossCompany;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasRun;

    /// <summary>True only when the last run returned a 2xx — gates the success badge.</summary>
    [ObservableProperty]
    private bool _runSucceeded;

    /// <summary>The last run's "{code} {reason}" (e.g. "200 OK", "404 Not Found").</summary>
    [ObservableProperty]
    private string _statusBadge = string.Empty;

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private string _statusText = "Not run yet.";

    /// <summary>Columns of the last run, in selection order (drives the dynamic result grid).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _resultColumns = Array.Empty<string>();

    public QueryBuilderViewModel(IMetadataService metadata, IODataClient client, IClipboardService? clipboard = null)
    {
        _metadata = metadata;
        _loader = new EntityCatalogLoader(metadata);
        _client = client;
        _clipboard = clipboard ?? new FakeClipboardService();
        // The fake seeds its catalogue synchronously; the real service starts empty and fills in via
        // InitializeAsync (triggered by the view on load).
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
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

            SelectedEntity = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
        }

        await LoadSelectedFieldsAsync(ct);
    }

    partial void OnSelectedEntityChanged(EntitySet? value)
    {
        // Default cross-company to the entity's company-awareness; the user can still override it.
        CrossCompany = value?.CompanyAware ?? false;
        LoadFields();                              // show what's cached immediately
        OnPropertyChanged(nameof(NotCachedMessage));
        LoadSelectedFieldsCommand.Execute(null);   // then fetch from $metadata if not cached yet
    }

    // Each option recomputes the live URL preview.
    partial void OnFilterChanged(string value) => UpdateQueryUrl();
    partial void OnOrderByChanged(string value) => UpdateQueryUrl();
    partial void OnTopChanged(int value) => UpdateQueryUrl();
    partial void OnSkipChanged(int value) => UpdateQueryUrl();
    partial void OnCountChanged(bool value) => UpdateQueryUrl();
    partial void OnCrossCompanyChanged(bool value) => UpdateQueryUrl();

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
        if (fields is not null)
        {
            foreach (var f in fields)
            {
                // Default selection = the primary key fields, mirroring the prototype's minimal $select.
                var chip = new FieldChipViewModel(f.Name, f.IsKey, isSelected: f.IsKey);
                chip.PropertyChanged += OnChipChanged;
                Fields.Add(chip);
            }
        }

        UpdateQueryUrl();
    }

    // A chip's selection (toggled by the command or the view's ToggleButton) is the single source of
    // truth for the $select clause; recompute the URL whenever one flips.
    private void OnChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FieldChipViewModel.IsSelected))
        {
            UpdateQueryUrl();
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

    private void UpdateQueryUrl() => QueryUrl = SelectedEntity is null ? string.Empty : "GET " + BuildPath(forRequest: false);

    // Builds the OData path ("/data/{Entity}?…") from the current selection + options. When
    // <paramref name="forRequest"/> is true the $filter / $orderby values are URL-encoded for the live
    // request; the readable (un-encoded) form drives the URL preview.
    private string BuildPath(bool forRequest)
    {
        if (SelectedEntity is null)
        {
            return string.Empty;
        }

        string Encode(string value) => forRequest ? Uri.EscapeDataString(value) : value;

        var parts = new List<string>();

        var select = string.Join(",", SelectedColumns());
        parts.Add($"$select={(select.Length == 0 ? "*" : select)}");

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            parts.Add($"$filter={Encode(Filter.Trim())}");
        }

        if (!string.IsNullOrWhiteSpace(OrderBy))
        {
            parts.Add($"$orderby={Encode(OrderBy.Trim())}");
        }

        if (Top > 0)
        {
            parts.Add($"$top={Top}");
        }

        if (Skip > 0)
        {
            parts.Add($"$skip={Skip}");
        }

        if (Count)
        {
            parts.Add("$count=true");
        }

        if (CrossCompany)
        {
            parts.Add("cross-company=true");
        }

        return $"/data/{SelectedEntity.Name}?{string.Join("&", parts)}";
    }

    private IEnumerable<string> SelectedColumns() =>
        Fields.Where(f => f.IsSelected).Select(f => f.Name);

    private bool CanRun() => !IsBusy;

    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRun))]
    private async Task Run(CancellationToken ct)
    {
        if (SelectedEntity is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "Running…";
        try
        {
            var columns = SelectedColumns().ToList();
            var path = BuildPath(forRequest: true);
            var response = await _client.SendAsync("GET", path, body: null, ct);

            // Clear stale rows before swapping columns so the grid never renders old rows under new
            // headers (the column rebuild keys off ResultColumns changing).
            ResultRows.Clear();
            ResultColumns = columns;
            if (response.IsSuccess)
            {
                foreach (var row in ParseRows(response.Body, columns))
                {
                    ResultRows.Add(row);
                }
            }

            RowCount = ResultRows.Count;
            RunSucceeded = response.IsSuccess;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            StatusText = $"{RowCount} rows · {response.StatusLine}";
            HasRun = true;
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
        }
    }

    private bool CanExportCsv() => ResultRows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsv()
    {
        var csv = QueryCsv.Build(ResultColumns, ResultRows);
        await _clipboard.SetTextAsync(csv);
        StatusText = $"Copied {ResultRows.Count} rows as CSV to the clipboard.";
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
            var cells = new Dictionary<string, string>(columns.Count);
            foreach (var column in columns)
            {
                cells[column] = item.TryGetProperty(column, out var cell) ? CellText(cell) : "—";
            }

            yield return new QueryResultRow(cells);
        }
    }

    private static string CellText(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.Null => "—",
        JsonValueKind.String => cell.GetString() ?? "—",
        _ => cell.ToString(),
    };
}
