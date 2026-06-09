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

    // True only while RefreshEntityFilter is rebuilding FilteredEntities, so the transient selection
    // null a bound ListBox emits during Clear() doesn't run OnSelectedEntityChanged's side-effects
    // (which would wipe the field selection + query URL on every keystroke in the entity search box).
    private bool _refreshingEntities;

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

    /// <summary>True when the selected entity exposes navigation properties to expand.</summary>
    [ObservableProperty]
    private bool _hasNavigations;

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

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportAllCsvCommand))]
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

        // Default cross-company to the entity's company-awareness; the user can still override it.
        CrossCompany = value?.CompanyAware ?? false;
        LoadFields();                              // show what's cached immediately
        OnPropertyChanged(nameof(NotCachedMessage));
        ExportAllCsvCommand.NotifyCanExecuteChanged();
        LoadSelectedFieldsCommand.Execute(null);   // then fetch from $metadata if not cached yet
    }

    // The two search boxes only re-filter what's displayed; they never touch the master lists.
    partial void OnEntitySearchChanged(string value) => RefreshEntityFilter();
    partial void OnFieldSearchChanged(string value) => RefreshFieldFilter();

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
            if (string.IsNullOrEmpty(term) || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFields.Add(f);
            }
        }
    }

    // Each option recomputes the live URL preview.
    partial void OnFilterChanged(string value) => UpdateQueryUrl();
    partial void OnOrderByChanged(string value) => UpdateQueryUrl();
    partial void OnTopChanged(string value) => UpdateQueryUrl();
    partial void OnSkipChanged(string value) => UpdateQueryUrl();
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

        LoadNavigations();
        RefreshFieldFilter();
        UpdateQueryUrl();
        OnPropertyChanged(nameof(FieldSelectionLabel));
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
            ExportAllCsvCommand.NotifyCanExecuteChanged(); // $select drives CanExportAllCsv
            OnPropertyChanged(nameof(FieldSelectionLabel));
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
        ExportAllCsvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FieldSelectionLabel));
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

        if (!string.IsNullOrWhiteSpace(Filter))
        {
            parts.Add($"$filter={Encode(Filter.Trim())}");
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

        if (CrossCompany)
        {
            parts.Add("cross-company=true");
        }

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

        IsBusy = true;
        StatusText = "Loading more…";
        try
        {
            var response = await _client.SendAsync("GET", link, body: null, ct);
            if (response.IsSuccess)
            {
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

    private bool CanExportCsv() => ResultRows.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsv()
    {
        var csv = QueryCsv.Build(ResultColumns, ResultRows);
        await _clipboard.SetTextAsync(csv);
        StatusText = $"Copied {ResultRows.Count} rows as CSV to the clipboard.";
    }

    // Saves the currently-loaded rows to a .csv file the user picks.
    [RelayCommand(CanExecute = nameof(CanExportCsv))]
    private async Task ExportCsvFile(CancellationToken ct)
    {
        var csv = QueryCsv.Build(ResultColumns, ResultRows);
        var rows = ResultRows.Count;
        var name = $"{SelectedEntity?.Name ?? "query"}.csv";
        var path = await _fileSave.SaveTextAsync(name, csv, ct);
        StatusText = path is null ? "Export cancelled." : $"Saved {rows} rows to {path}.";
    }

    // Export-all can run whenever an entity is selected (it issues its own unbounded query); gated off
    // IsBusy so it doesn't overlap a Run / Load-more.
    private bool CanExportAllCsv() => !IsBusy && SelectedEntity is not null && SelectedColumns().Any();

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

            var csv = QueryCsv.Build(columns, rows);
            var name = $"{entityName}.csv";
            var saved = await _fileSave.SaveTextAsync(name, csv, ct);
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
