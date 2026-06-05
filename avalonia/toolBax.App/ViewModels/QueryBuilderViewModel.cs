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
    private const int TopRows = 50;

    private readonly IMetadataService _metadata;
    private readonly IODataClient _client;

    public ObservableCollection<EntitySet> Entities { get; }
    public ObservableCollection<FieldChipViewModel> Fields { get; } = new();
    public ObservableCollection<QueryResultRow> ResultRows { get; } = new();

    [ObservableProperty]
    private EntitySet? _selectedEntity;

    [ObservableProperty]
    private bool _hasFields;

    [ObservableProperty]
    private string _queryUrl = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasRun;

    [ObservableProperty]
    private int _rowCount;

    [ObservableProperty]
    private string _statusText = "Not run yet.";

    /// <summary>Columns of the last run, in selection order (drives the dynamic result grid).</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _resultColumns = Array.Empty<string>();

    public QueryBuilderViewModel(IMetadataService metadata, IODataClient client)
    {
        _metadata = metadata;
        _client = client;
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
        SelectedEntity = Entities.FirstOrDefault();
    }

    public string NotCachedMessage => SelectedEntity is null
        ? string.Empty
        : $"$metadata for {SelectedEntity.Name} isn't cached — run once to populate the field list.";

    partial void OnSelectedEntityChanged(EntitySet? value)
    {
        LoadFields();
        OnPropertyChanged(nameof(NotCachedMessage));
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

    private void UpdateQueryUrl() => QueryUrl = BuildQueryUrl();

    private string BuildQueryUrl()
    {
        if (SelectedEntity is null)
        {
            return string.Empty;
        }

        var select = string.Join(",", SelectedColumns());
        if (select.Length == 0)
        {
            select = "*";
        }

        var url = $"GET /data/{SelectedEntity.Name}?$select={select}&$top={TopRows}";
        if (SelectedEntity.CompanyAware)
        {
            url += "&cross-company=true";
        }

        return url;
    }

    private IEnumerable<string> SelectedColumns() =>
        Fields.Where(f => f.IsSelected).Select(f => f.Name);

    [RelayCommand(IncludeCancelCommand = true)]
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
            var path = BuildQueryUrl()["GET ".Length..];
            var response = await _client.SendAsync("GET", path, body: null, ct);

            ResultColumns = columns;
            ResultRows.Clear();
            if (response.IsSuccess)
            {
                foreach (var row in ParseRows(response.Body, columns))
                {
                    ResultRows.Add(row);
                }
            }

            RowCount = ResultRows.Count;
            StatusText = $"{RowCount} rows · {response.StatusLine}";
            HasRun = true;
        }
        catch (Exception ex)
        {
            StatusText = $"Query failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
