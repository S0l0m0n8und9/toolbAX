using FoToolbox.Core.Auth;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DualWriteMapBrowserPlugin;

public sealed partial class DualWriteMapBrowserViewModel : INotifyPropertyChanged
{
    private const int DualWriteMapComponentType = 500;
    private static readonly string SelectColumns = string.Join(",",
        "msdyn_dualwriteentitymapid",
        "solutionid",
        "msdyn_name",
        "msdyn_displayname",
        "msdyn_mapping",
        "msdyn_properties",
        "msdyn_version",
        "createdon",
        "modifiedon",
        "statecode",
        "statuscode",
        "ownerid");

    private readonly IPluginContext _ctx;
    private readonly IPluginContextDataverse? _dataverse;
    private readonly ObservableCollection<PublisherOption> _publishers = new();
    private readonly ObservableCollection<SolutionOption> _solutions = new();
    private readonly ObservableCollection<DualWriteMapRecord> _records = new();
    private readonly ObservableCollection<FoEntityOption> _foEntities = new();
    private readonly ObservableCollection<CountLegConfigRow> _countLegConfigs = new();
    private readonly ObservableCollection<CountValidationRow> _countResults = new();
    private readonly ReadOnlyObservableCollection<PublisherOption> _publishersReadOnly;
    private readonly ReadOnlyObservableCollection<SolutionOption> _solutionsReadOnly;
    private readonly ReadOnlyObservableCollection<FoEntityOption> _foEntitiesReadOnly;
    private readonly ReadOnlyObservableCollection<CountLegConfigRow> _countLegConfigsReadOnly;
    private readonly ReadOnlyObservableCollection<CountValidationRow> _countResultsReadOnly;
    private Dictionary<string, string>? _foEntityLookup;
    private Dictionary<string, ODataEnumType> _foEnumLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ODataEntity?> _foEntityDetailsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _foEntityFieldLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, ODataEnumType>> _foEntityEnumFields = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _foEntityNames = new();
    private string _statusMessage = "Ready.";
    private string _solutionSummary = "Solutions not loaded.";
    private string _recordSummary = "Showing 0 of 0 records";
    private string _countSummary = "No count run yet.";
    private string _foCountPreviewUrl = string.Empty;
    private string _ceCountPreviewUrl = string.Empty;
    private bool _isLoading;
    private bool _isLoadingSolutions;
    private bool _isCounting;
    private bool _useExactCeCount;
    private bool _filterBySolution;
    private string? _searchText;
    private PublisherOption? _selectedPublisher;
    private SolutionOption? _selectedSolution;
    private DualWriteMapRecord? _selectedRecord;
    private CountLegConfigRow? _selectedCountLegConfig;

    public DualWriteMapBrowserViewModel(IPluginContext ctx)
        : this(ctx, new TestifyConfigurationStore())
    {
    }

    internal DualWriteMapBrowserViewModel(IPluginContext ctx, TestifyConfigurationStore testifyConfigStore)
    {
        _ctx = ctx;
        _testifyConfigStore = testifyConfigStore ?? throw new ArgumentNullException(nameof(testifyConfigStore));
        _dataverse = ctx as IPluginContextDataverse;
        _write = ctx as IPluginContextWrite;
        DataverseEndpoint = HasDataverseConnection
            ? ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse!.CurrentDataverseEnv!.BaseUrl)
            : "Dataverse profile not configured. Open Profiles and set CE/Dataverse values.";
        _publishersReadOnly = new ReadOnlyObservableCollection<PublisherOption>(_publishers);
        _solutionsReadOnly = new ReadOnlyObservableCollection<SolutionOption>(_solutions);
        _foEntitiesReadOnly = new ReadOnlyObservableCollection<FoEntityOption>(_foEntities);
        _countLegConfigsReadOnly = new ReadOnlyObservableCollection<CountLegConfigRow>(_countLegConfigs);
        _countResultsReadOnly = new ReadOnlyObservableCollection<CountValidationRow>(_countResults);
        _testifyPreflightRowsReadOnly = new ReadOnlyObservableCollection<TestifyPreflightRow>(_testifyPreflightRows);
        _testifyLogRowsReadOnly = new ReadOnlyObservableCollection<TestifyExecutionLogRow>(_testifyLogRows);
        _testifyResultRowsReadOnly = new ReadOnlyObservableCollection<TestifyResultRow>(_testifyResultRows);

        SolutionsView = CollectionViewSource.GetDefaultView(_solutions);
        SolutionsView.Filter = SolutionFilter;

        RecordsView = CollectionViewSource.GetDefaultView(_records);
        RecordsView.Filter = RecordFilter;

        Action<Exception> onError = ex =>
        {
            _ctx.Logger.LogError(ex, "DualWriteMapBrowser command failed.");
            StatusMessage = $"Command failed: {ex.Message}";
        };

        LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
        LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
        InitializeTestifySettingsCommands(onError);
        ClearCommand = new RelayCommand(_ => ClearRecords());

        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
        }
        else
        {
            _ = LoadSolutionsAsync(CancellationToken.None);
        }
    }

    private bool HasDataverseConnection =>
        _dataverse is not null &&
        _dataverse.HasDataverseProfile &&
        _dataverse.DataverseHttp is not null &&
        _dataverse.CurrentDataverseEnv is not null;

    public ICollectionView SolutionsView { get; }
    public ICollectionView RecordsView { get; }
    public AsyncRelayCommand LoadMapsCommand { get; }
    public AsyncRelayCommand LoadSolutionsCommand { get; }
    public AsyncRelayCommand RefreshCountSetupCommand { get; }
    public AsyncRelayCommand ValidateCountsCommand { get; }
    public RelayCommand ClearCommand { get; }
    public string DataverseEndpoint { get; }
    public ReadOnlyObservableCollection<PublisherOption> Publishers => _publishersReadOnly;
    public ReadOnlyObservableCollection<SolutionOption> Solutions => _solutionsReadOnly;
    public ReadOnlyObservableCollection<FoEntityOption> FoEntities => _foEntitiesReadOnly;
    public ReadOnlyObservableCollection<CountLegConfigRow> CountLegConfigs => _countLegConfigsReadOnly;
    public ReadOnlyObservableCollection<CountValidationRow> CountResults => _countResultsReadOnly;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotLoading));
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsNotLoading => !IsBusy;

    public bool IsLoadingSolutions
    {
        get => _isLoadingSolutions;
        set
        {
            if (_isLoadingSolutions == value)
            {
                return;
            }

            _isLoadingSolutions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsCounting
    {
        get => _isCounting;
        set
        {
            if (_isCounting == value)
            {
                return;
            }

            _isCounting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;

    public bool FilterBySolution
    {
        get => _filterBySolution;
        set
        {
            if (_filterBySolution == value)
            {
                return;
            }

            _filterBySolution = value;
            OnPropertyChanged();
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            RecordsView.Refresh();
            UpdateRecordSummary();
        }
    }

    public PublisherOption? SelectedPublisher
    {
        get => _selectedPublisher;
        set
        {
            if (_selectedPublisher == value)
            {
                return;
            }

            _selectedPublisher = value;
            OnPropertyChanged();
            SolutionsView.Refresh();
            SelectedSolution = SolutionsView.Cast<SolutionOption>().FirstOrDefault();
            UpdateSolutionSummary();
        }
    }

    public DualWriteMapRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (_selectedRecord == value)
            {
                return;
            }

            _selectedRecord = value;
            OnPropertyChanged();
            OnSelectedRecordChanged();
        }
    }

    public SolutionOption? SelectedSolution
    {
        get => _selectedSolution;
        set
        {
            if (_selectedSolution == value)
            {
                return;
            }

            _selectedSolution = value;
            OnPropertyChanged();
        }
    }

    public CountLegConfigRow? SelectedCountLegConfig
    {
        get => _selectedCountLegConfig;
        set
        {
            if (_selectedCountLegConfig == value)
            {
                return;
            }

            _selectedCountLegConfig = value;
            OnPropertyChanged();
            RefreshCountPreviewUrls();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string RecordSummary
    {
        get => _recordSummary;
        set
        {
            _recordSummary = value;
            OnPropertyChanged();
        }
    }

    public string SolutionSummary
    {
        get => _solutionSummary;
        set
        {
            _solutionSummary = value;
            OnPropertyChanged();
        }
    }

    public string CountSummary
    {
        get => _countSummary;
        set
        {
            _countSummary = value;
            OnPropertyChanged();
        }
    }

    public bool UseExactCeCount
    {
        get => _useExactCeCount;
        set
        {
            if (_useExactCeCount == value)
            {
                return;
            }

            _useExactCeCount = value;
            OnPropertyChanged();
            RefreshCountPreviewUrls();
        }
    }

    public string FoCountPreviewUrl
    {
        get => _foCountPreviewUrl;
        set
        {
            if (_foCountPreviewUrl == value)
            {
                return;
            }

            _foCountPreviewUrl = value;
            OnPropertyChanged();
        }
    }

    public string CeCountPreviewUrl
    {
        get => _ceCountPreviewUrl;
        set
        {
            if (_ceCountPreviewUrl == value)
            {
                return;
            }

            _ceCountPreviewUrl = value;
            OnPropertyChanged();
        }
    }

    private async Task LoadMapsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        if (FilterBySolution && _solutions.Count == 0)
        {
            await LoadSolutionsAsync(cancellationToken);
        }

        if (FilterBySolution && SelectedSolution is null)
        {
            StatusMessage = "Select a solution, or clear 'Filter by solution'.";
            return;
        }

        IsLoading = true;
        _records.Clear();
        SelectedRecord = null;
        ClearCountSetup();
        _countResults.Clear();
        CountSummary = "No count run yet.";
        UpdateRecordSummary();
        StatusMessage = "Loading dual-write map records...";

        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        HashSet<Guid>? componentMapIds = null;
        if (FilterBySolution && SelectedSolution is not null)
        {
            StatusMessage = $"Resolving map components for solution '{SelectedSolution.DisplayName}'...";
            componentMapIds = await LoadDualWriteComponentIdsForSolutionAsync(
                dataverseHttp,
                apiBase,
                SelectedSolution.UniqueName,
                cancellationToken);

            if (componentMapIds.Count == 0)
            {
                StatusMessage = $"No dual-write map components found in solution '{SelectedSolution.DisplayName}'.";
                IsLoading = false;
                return;
            }
        }

        var nextLink = BuildMapsUrl(apiBase);
        var pageCount = 0;

        try
        {
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation(
                    "Prefer",
                    "odata.include-annotations=\"OData.Community.Display.V1.FormattedValue\",odata.maxpagesize=250");

                using var response = await dataverseHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException(
                        $"Dataverse request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;

                if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Dataverse response did not contain a 'value' array.");
                }

                foreach (var item in valueArray.EnumerateArray())
                {
                    if (componentMapIds is not null)
                    {
                        var mapIdText = GetValueAsString(item, "msdyn_dualwriteentitymapid");
                        if (!Guid.TryParse(mapIdText, out var mapId) || !componentMapIds.Contains(mapId))
                        {
                            continue;
                        }
                    }

                    _records.Add(ParseRecord(item));
                }

                pageCount++;
                nextLink = GetValueAsString(root, "@odata.nextLink");
                StatusMessage = $"Loaded {_records.Count} records so far...";
            }

            RecordsView.Refresh();
            UpdateRecordSummary();
            SelectedRecord ??= _records.FirstOrDefault();
            await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: false);
            StatusMessage = FilterBySolution && SelectedSolution is not null
                ? $"Loaded {_records.Count} dual-write map records from solution '{SelectedSolution.DisplayName}' ({pageCount} page(s))."
                : $"Loaded {_records.Count} dual-write map records from {pageCount} page(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Load cancelled.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load msdyn_dualwriteentitymap records.");
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSolutionsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            SolutionSummary = "Dataverse profile not configured.";
            return;
        }

        IsLoadingSolutions = true;
        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var nextLink = $"{apiBase}/solutions?$select=solutionid,uniquename,friendlyname,version,_publisherid_value&$expand=publisherid($select=uniquename,friendlyname)&$orderby=uniquename%20asc";
        var loaded = new List<SolutionOption>();
        var pageCount = 0;
        var selectedId = SelectedSolution?.Id;
        var selectedPublisherKey = SelectedPublisher?.UniqueName;

        try
        {
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=500");

                using var response = await dataverseHttp.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new InvalidOperationException(
                        $"Dataverse solutions request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Dataverse solutions response did not contain a 'value' array.");
                }

                foreach (var solution in valueArray.EnumerateArray())
                {
                    var idText = GetValueAsString(solution, "solutionid");
                    if (!Guid.TryParse(idText, out var id))
                    {
                        continue;
                    }

                    var uniqueName = GetValueAsString(solution, "uniquename") ?? string.Empty;
                    var friendlyName = GetValueAsString(solution, "friendlyname") ?? string.Empty;
                    var version = GetValueAsString(solution, "version") ?? string.Empty;

                    var publisherUniqueName = string.Empty;
                    var publisherDisplayName = GetValueAsString(solution, "_publisherid_value@OData.Community.Display.V1.FormattedValue") ?? string.Empty;
                    if (solution.TryGetProperty("publisherid", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
                    {
                        publisherUniqueName = GetValueAsString(publisher, "uniquename") ?? string.Empty;
                        var friendlyPublisherName = GetValueAsString(publisher, "friendlyname") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(friendlyPublisherName))
                        {
                            publisherDisplayName = friendlyPublisherName;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(publisherUniqueName))
                    {
                        publisherUniqueName = GetValueAsString(solution, "_publisherid_value") ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(publisherDisplayName))
                    {
                        publisherDisplayName = string.IsNullOrWhiteSpace(publisherUniqueName)
                            ? "(Unknown Publisher)"
                            : publisherUniqueName;
                    }

                    var display = string.IsNullOrWhiteSpace(friendlyName) ? uniqueName : $"{friendlyName} [{uniqueName}]";
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        display = $"{display} v{version}";
                    }

                    loaded.Add(new SolutionOption(
                        id,
                        display,
                        uniqueName,
                        friendlyName,
                        version,
                        publisherUniqueName,
                        publisherDisplayName));
                }

                pageCount++;
                nextLink = GetValueAsString(root, "@odata.nextLink");
            }

            _solutions.Clear();
            foreach (var option in loaded
                         .OrderBy(s => s.PublisherDisplayName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(s => s.UniqueName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(s => s.Version, StringComparer.OrdinalIgnoreCase))
            {
                _solutions.Add(option);
            }

            RebuildPublishers(loaded, selectedPublisherKey);
            SolutionsView.Refresh();
            SelectedSolution = selectedId is not null
                ? _solutions.FirstOrDefault(s => s.Id == selectedId.Value && SolutionFilter(s)) ?? SolutionsView.Cast<SolutionOption>().FirstOrDefault()
                : SolutionsView.Cast<SolutionOption>().FirstOrDefault();

            UpdateSolutionSummary(pageCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SolutionSummary = "Solutions load cancelled.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load solutions.");
            SolutionSummary = $"Solutions load failed: {ex.Message}";
        }
        finally
        {
            IsLoadingSolutions = false;
        }
    }

    private void RebuildPublishers(IEnumerable<SolutionOption> loaded, string? selectedPublisherKey)
    {
        var selectedKey = string.IsNullOrWhiteSpace(selectedPublisherKey)
            ? PublisherOption.All.UniqueName
            : selectedPublisherKey;

        _publishers.Clear();
        _publishers.Add(PublisherOption.All);

        foreach (var publisher in loaded
                     .Where(s => !string.IsNullOrWhiteSpace(s.PublisherUniqueName))
                     .GroupBy(s => s.PublisherUniqueName, StringComparer.OrdinalIgnoreCase)
                     .Select(g => new PublisherOption(
                         g.First().PublisherUniqueName,
                         g.First().PublisherDisplayName,
                         g.Count()))
                     .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            _publishers.Add(publisher);
        }

        SelectedPublisher = _publishers.FirstOrDefault(p => string.Equals(p.UniqueName, selectedKey, StringComparison.OrdinalIgnoreCase))
            ?? _publishers.FirstOrDefault();
    }

    private bool SolutionFilter(object? item)
    {
        if (item is not SolutionOption solution)
        {
            return false;
        }

        if (SelectedPublisher is null || SelectedPublisher.IsAll)
        {
            return true;
        }

        return string.Equals(solution.PublisherUniqueName, SelectedPublisher.UniqueName, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSolutionSummary(int? pageCount = null)
    {
        var visible = SolutionsView.Cast<object>().Count();
        var pagePart = pageCount is null ? string.Empty : $" from {pageCount.Value} page(s)";
        SolutionSummary = $"Showing {visible} of {_solutions.Count} solutions{pagePart}.";
    }

    private async Task RefreshCountSetupAsync(CancellationToken cancellationToken)
    {
        await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: true);
    }

    private async Task RefreshCountSetupCoreAsync(CancellationToken cancellationToken, bool updateStatus)
    {
        var selectedMaps = GetMapsForCounting();
        if (selectedMaps.Count == 0)
        {
            ClearCountSetup();
            CountSummary = "No count setup prepared.";
            if (updateStatus)
            {
                StatusMessage = "Select one or more maps (checkbox), or select a current map.";
            }
            return;
        }

        await EnsureFoEntityLookupAsync(cancellationToken);

        var previousRows = _countLegConfigs.ToDictionary(
            row => BuildCountLegKey(row.MapId, row.LegId),
            row => row,
            StringComparer.OrdinalIgnoreCase);
        var selectedKey = SelectedCountLegConfig is null
            ? null
            : BuildCountLegKey(SelectedCountLegConfig.MapId, SelectedCountLegConfig.LegId);

        ClearCountSetup();

        foreach (var map in selectedMaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var leg in map.MappingLegRows)
            {
                var rowKey = BuildCountLegKey(map.Id, leg.LegId);
                previousRows.TryGetValue(rowKey, out var previous);
                var foEntityResolved = ResolveFoEntityName(leg.SourceSchemaDistinctName, leg.SourceSchema);
                var (foFilter, filterNote) = await ConvertSourceFilterToODataAsync(foEntityResolved, leg.SourceFilter, cancellationToken);
                var row = new CountLegConfigRow(
                    mapDisplayName: map.DisplayName,
                    mapId: map.Id,
                    legId: leg.LegId,
                    sourceSchema: leg.SourceSchema,
                    sourceSchemaDistinctName: leg.SourceSchemaDistinctName,
                    sourceEnvironmentType: leg.SourceEnvironmentType,
                    destinationEnvironmentType: leg.DestinationEnvironmentType,
                    foEntityResolved: foEntityResolved,
                    sourceFilterXpp: leg.SourceFilter,
                    foFilter: foFilter,
                    foFilterNote: filterNote,
                    ceEntity: leg.DestinationSchema,
                    ceFilter: leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                    include: previous?.Include ?? true,
                    foEntityOverride: previous?.FoEntityOverride ?? string.Empty);

                AttachCountLegConfig(row);
                _countLegConfigs.Add(row);
            }
        }

        if (_countLegConfigs.Count == 0)
        {
            CountSummary = "No count legs available for the selected maps.";
            if (updateStatus)
            {
                StatusMessage = "No count legs available for the selected maps.";
            }
            return;
        }

        SelectedCountLegConfig = selectedKey is null
            ? _countLegConfigs.FirstOrDefault()
            : _countLegConfigs.FirstOrDefault(row => string.Equals(
                    BuildCountLegKey(row.MapId, row.LegId),
                    selectedKey,
                    StringComparison.OrdinalIgnoreCase))
              ?? _countLegConfigs.FirstOrDefault();

        CountSummary = $"Prepared count setup for {_countLegConfigs.Count} leg(s).";
        if (updateStatus)
        {
            StatusMessage = $"Prepared count setup for {_countLegConfigs.Count} leg(s).";
        }
    }

    private async Task ValidateCountsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: false);
        if (_countLegConfigs.Count == 0)
        {
            StatusMessage = "No count legs available for the selected maps.";
            CountSummary = "No count legs available for the selected maps.";
            return;
        }

        var legsToValidate = _countLegConfigs.Where(row => row.Include).ToList();
        if (legsToValidate.Count == 0)
        {
            StatusMessage = "No count legs included. Select at least one row in count setup.";
            CountSummary = "No count legs selected for validation.";
            return;
        }

        _countResults.Clear();
        CountSummary = UseExactCeCount
            ? $"Running count validation for {legsToValidate.Count} leg(s) [Exact CE]..."
            : $"Running count validation for {legsToValidate.Count} leg(s) [Fast CE]...";
        IsCounting = true;

        try
        {
            var dataverseHttp = _dataverse!.DataverseHttp!;
            var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);

            foreach (var legConfig in legsToValidate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ceEntity = legConfig.CeEntity;
                var foEntity = legConfig.FoEntityEffective;
                var foFilter = legConfig.FoFilter;
                var ceFilter = legConfig.CeFilter;
                var foFilterNote = legConfig.FoFilterNote;
                if (!string.IsNullOrWhiteSpace(foEntity))
                {
                    var converted = await ConvertSourceFilterToODataAsync(foEntity, legConfig.SourceFilterXpp, cancellationToken);
                    foFilter = converted.Filter;
                    foFilterNote = converted.Note;
                }

                long? ceCount = null;
                long? foCount = null;
                bool? match = null;
                var statusParts = new List<string>();

                if (!string.Equals(legConfig.SourceEnvironmentType, "AX", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(legConfig.DestinationEnvironmentType, "CRM", StringComparison.OrdinalIgnoreCase))
                {
                    statusParts.Add("Leg direction is not AX->CRM; using Source as FO and Destination as CE.");
                }

                if (string.IsNullOrWhiteSpace(ceEntity))
                {
                    statusParts.Add("Missing CE destination schema.");
                }
                else
                {
                    try
                    {
                        ceCount = UseExactCeCount
                            ? await GetDataverseExactCountAsync(dataverseHttp, apiBase, ceEntity, ceFilter, cancellationToken)
                            : await GetDataverseCountAsync(dataverseHttp, apiBase, ceEntity, ceFilter, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        statusParts.Add($"CE count failed: {ex.Message}");
                    }
                }

                if (string.IsNullOrWhiteSpace(foEntity))
                {
                    var schemaInfo = string.IsNullOrWhiteSpace(legConfig.SourceSchemaDistinctName)
                        ? legConfig.SourceSchema
                        : $"{legConfig.SourceSchemaDistinctName}' / '{legConfig.SourceSchema}";
                    statusParts.Add($"FO entity unresolved from source schema '{schemaInfo}'.");
                }
                else
                {
                    try
                    {
                        var foResult = await GetFoCountWithFallbackAsync(foEntity, foFilter, cancellationToken);
                        foCount = foResult.Count;
                        if (!string.IsNullOrWhiteSpace(foResult.Note))
                        {
                            statusParts.Add(foResult.Note);
                        }
                    }
                    catch (Exception ex)
                    {
                        statusParts.Add($"FO count failed: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(foFilterNote))
                {
                    statusParts.Add(foFilterNote);
                }

                if (ceCount.HasValue && foCount.HasValue)
                {
                    match = ceCount.Value == foCount.Value;
                    if (match == false)
                    {
                        statusParts.Add("Counts differ.");
                    }
                }

                if (!UseExactCeCount && ceCount == 5000)
                {
                    statusParts.Add("CE count returned 5000 (possible API cap). Enable Exact CE Count for full value.");
                }

                var status = statusParts.Count == 0 ? "OK" : string.Join(" ", statusParts);
                _countResults.Add(new CountValidationRow(
                    legConfig.MapDisplayName,
                    legConfig.MapId,
                    legConfig.LegId,
                    foEntity,
                    foFilter,
                    ceEntity,
                    ceFilter,
                    foCount,
                    ceCount,
                    match,
                    status));
            }

            var matched = _countResults.Count(r => r.CountsMatch == true);
            var mismatched = _countResults.Count(r => r.CountsMatch == false);
            var incomplete = _countResults.Count - matched - mismatched;
            var modeText = UseExactCeCount ? "Exact CE" : "Fast CE";
            CountSummary = $"Validated {_countResults.Count} leg(s) [{modeText}]. Matched: {matched}. Mismatch: {mismatched}. Incomplete: {incomplete}.";
            StatusMessage = "Count validation finished.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CountSummary = "Count validation cancelled.";
            StatusMessage = "Count validation cancelled.";
        }
        finally
        {
            IsCounting = false;
        }
    }

    private async Task EnsureFoEntityLookupAsync(CancellationToken cancellationToken)
    {
        if (_foEntityLookup is not null)
        {
            return;
        }

        var index = await _ctx.Catalog.GetODataEntityIndexAsync(
            _ctx.CurrentEnv,
            CatalogRefreshMode.UseCacheIfAvailable,
            cancellationToken);

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in index.Entities)
        {
            names.Add(entity.Name);
            var key = NormalizeEntityKey(entity.Name);
            if (!lookup.ContainsKey(key))
            {
                lookup.Add(key, entity.Name);
            }
        }

        _foEntityLookup = lookup;
        _foEnumLookup = BuildEnumLookup(index.Enums);
        _foEntityDetailsCache.Clear();
        _foEntityFieldLookup.Clear();
        _foEntityEnumFields.Clear();
        _foEntityNames = names.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToList();
        RebuildFoEntityOptions();
    }

    private List<DualWriteMapRecord> GetMapsForCounting()
    {
        var selectedMaps = _records.Where(r => r.IsSelected).ToList();
        if (selectedMaps.Count == 0 && SelectedRecord is not null)
        {
            selectedMaps.Add(SelectedRecord);
        }

        return selectedMaps;
    }

    private static string BuildCountLegKey(string mapId, string legId) => $"{mapId}|{legId}";

    private void RebuildFoEntityOptions()
    {
        _foEntities.Clear();
        _foEntities.Add(FoEntityOption.Auto);
        if (_foEntityNames.Count == 0)
        {
            return;
        }

        foreach (var entity in _foEntityNames)
        {
            _foEntities.Add(new FoEntityOption(entity, entity));
        }
    }

    private void AttachCountLegConfig(CountLegConfigRow row)
    {
        row.PropertyChanged += OnCountLegConfigPropertyChanged;
    }

    private void ClearCountSetup()
    {
        foreach (var row in _countLegConfigs)
        {
            row.PropertyChanged -= OnCountLegConfigPropertyChanged;
        }

        _countLegConfigs.Clear();
        SelectedCountLegConfig = null;
        FoCountPreviewUrl = string.Empty;
        CeCountPreviewUrl = string.Empty;
    }

    private void OnCountLegConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CountLegConfigRow row || SelectedCountLegConfig != row)
        {
            return;
        }

        if (e.PropertyName == nameof(CountLegConfigRow.FoEntityOverride) ||
            e.PropertyName == nameof(CountLegConfigRow.FoEntityEffective) ||
            e.PropertyName == nameof(CountLegConfigRow.FoFilter) ||
            e.PropertyName == nameof(CountLegConfigRow.CeEntity) ||
            e.PropertyName == nameof(CountLegConfigRow.CeFilter))
        {
            RefreshCountPreviewUrls();
        }
    }

    private void RefreshCountPreviewUrls()
    {
        if (SelectedCountLegConfig is null)
        {
            FoCountPreviewUrl = string.Empty;
            CeCountPreviewUrl = string.Empty;
            return;
        }

        FoCountPreviewUrl = BuildFoCountPreviewUrl(SelectedCountLegConfig.FoEntityEffective, SelectedCountLegConfig.FoFilter);
        CeCountPreviewUrl = BuildDataverseCountPreviewUrl(SelectedCountLegConfig.CeEntity, SelectedCountLegConfig.CeFilter);
    }

    private string BuildFoCountPreviewUrl(string foEntity, string? oDataFilter)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return "(FO entity unresolved)";
        }

        var spec = new QuerySpec(
            Entity: foEntity,
            Filter: string.IsNullOrWhiteSpace(oDataFilter) ? null : oDataFilter,
            Top: 1,
            Count: true,
            CrossCompany: true);

        return QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec).ToString();
    }

    private string BuildDataverseCountPreviewUrl(string ceEntity, string? oDataFilter)
    {
        if (!HasDataverseConnection)
        {
            return "(Dataverse profile not configured)";
        }

        if (string.IsNullOrWhiteSpace(ceEntity))
        {
            return "(CE entity not provided)";
        }

        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse!.CurrentDataverseEnv!.BaseUrl);
        if (UseExactCeCount)
        {
            return $"{BuildDataversePagedCountStartUrl(apiBase, ceEntity, oDataFilter)} [paged, prefer: odata.maxpagesize=5000]";
        }

        var query = new List<string> { "$top=1", "$count=true" };
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        return $"{apiBase}/{ceEntity}?{string.Join("&", query)}";
    }

    private async Task<(string Filter, string Note)> ConvertSourceFilterToODataAsync(
        string foEntity,
        string? xppFilter,
        CancellationToken cancellationToken)
    {
        var filter = ConvertXppFilterToOData(xppFilter, out var conversionNote);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return (filter, conversionNote);
        }

        await EnsureFoEntityLookupAsync(cancellationToken);
        var fieldLookup = await GetFoEntityFieldLookupAsync(foEntity, cancellationToken);
        if (fieldLookup.Count > 0)
        {
            filter = NormalizeFilterFieldNames(filter, fieldLookup, out var fieldRenameCount);
            if (fieldRenameCount > 0)
            {
                conversionNote = AppendNote(conversionNote, $"Normalized {fieldRenameCount} field name(s) to FO entity property names.");
            }
        }

        var enumFields = await GetFoEntityEnumFieldLookupAsync(foEntity, cancellationToken);
        if (enumFields.Count == 0)
        {
            if (filter.Contains("::", StringComparison.Ordinal))
            {
                conversionNote = AppendNote(conversionNote, "Source filter still contains enum tokens (::); FO filter conversion may need manual adjustment.");
            }

            return (filter, conversionNote);
        }

        var replacements = 0;
        filter = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\s+(?<enum>[A-Za-z_][A-Za-z0-9_.]*)::(?<member>[A-Za-z_][A-Za-z0-9_]*)\b",
            m =>
            {
                var field = m.Groups["field"].Value;
                var op = m.Groups["op"].Value;
                var enumToken = m.Groups["enum"].Value;
                var memberToken = m.Groups["member"].Value;

                if (!enumFields.TryGetValue(field, out var enumType))
                {
                    enumType = ResolveEnumType(_foEnumLookup, enumToken);
                    if (enumType is null)
                    {
                        return m.Value;
                    }
                }

                var member = ResolveEnumMember(enumType, memberToken);
                if (string.IsNullOrWhiteSpace(member))
                {
                    return m.Value;
                }

                replacements++;
                return $"{field} {op} {enumType.Name}'{EscapeSingleQuoted(member)}'";
            },
            RegexOptions.IgnoreCase);

        filter = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\s+'(?<value>[^']*)'",
            m =>
            {
                var field = m.Groups["field"].Value;
                if (!enumFields.TryGetValue(field, out var enumType))
                {
                    return m.Value;
                }

                var valueToken = m.Groups["value"].Value.Replace("''", "'", StringComparison.Ordinal);
                var member = ResolveEnumMember(enumType, valueToken);
                if (string.IsNullOrWhiteSpace(member))
                {
                    return m.Value;
                }

                replacements++;
                var op = m.Groups["op"].Value;
                return $"{field} {op} {enumType.Name}'{EscapeSingleQuoted(member)}'";
            },
            RegexOptions.IgnoreCase);

        if (replacements > 0)
        {
            conversionNote = AppendNote(conversionNote, $"Applied enum metadata conversion on {replacements} condition(s).");
        }

        if (filter.Contains("::", StringComparison.Ordinal))
        {
            conversionNote = AppendNote(conversionNote, "Source filter still contains enum tokens (::); FO filter conversion may need manual adjustment.");
        }

        return (Regex.Replace(filter, @"\s+", " ").Trim(), conversionNote);
    }

    private async Task<Dictionary<string, ODataEnumType>> GetFoEntityEnumFieldLookupAsync(string foEntity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return new Dictionary<string, ODataEnumType>(StringComparer.OrdinalIgnoreCase);
        }

        if (_foEntityEnumFields.TryGetValue(foEntity, out var cached))
        {
            return cached;
        }

        var details = await GetFoEntityDetailsCachedAsync(foEntity, cancellationToken);

        var lookup = new Dictionary<string, ODataEnumType>(StringComparer.OrdinalIgnoreCase);
        if (details is not null)
        {
            foreach (var property in details.Properties)
            {
                var enumType = ResolveEnumType(_foEnumLookup, property.Type);
                if (enumType is null)
                {
                    continue;
                }

                lookup[property.Name] = enumType;
            }
        }

        _foEntityEnumFields[foEntity] = lookup;
        return lookup;
    }

    private async Task<Dictionary<string, string>> GetFoEntityFieldLookupAsync(string foEntity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        if (_foEntityFieldLookup.TryGetValue(foEntity, out var cached))
        {
            return cached;
        }

        var details = await GetFoEntityDetailsCachedAsync(foEntity, cancellationToken);
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (details is not null)
        {
            foreach (var property in details.Properties)
            {
                var key = NormalizeEntityKey(property.Name);
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                {
                    lookup.Add(key, property.Name);
                }
            }
        }

        _foEntityFieldLookup[foEntity] = lookup;
        return lookup;
    }

    private async Task<ODataEntity?> GetFoEntityDetailsCachedAsync(string foEntity, CancellationToken cancellationToken)
    {
        if (_foEntityDetailsCache.TryGetValue(foEntity, out var cached))
        {
            return cached;
        }

        var details = await _ctx.Catalog.GetODataEntityDetailsAsync(
            _ctx.CurrentEnv,
            foEntity,
            CatalogRefreshMode.UseCacheIfAvailable,
            cancellationToken);

        details ??= await _ctx.Catalog.GetODataEntityDetailsAsync(
            _ctx.CurrentEnv,
            foEntity,
            CatalogRefreshMode.UseCacheIfFresh,
            cancellationToken);

        _foEntityDetailsCache[foEntity] = details;
        return details;
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

            var shortName = enumType.Name.Split('.').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(shortName) && !lookup.ContainsKey(shortName))
            {
                lookup.Add(shortName, enumType);
            }
        }

        return lookup;
    }

    private static ODataEnumType? ResolveEnumType(Dictionary<string, ODataEnumType> lookup, string type)
    {
        if (lookup.Count == 0 || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var normalized = type;
        if (normalized.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase) &&
            normalized.EndsWith(")", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Substring("Collection(".Length, normalized.Length - "Collection(".Length - 1);
        }

        if (lookup.TryGetValue(normalized, out var enumType))
        {
            return enumType;
        }

        var shortName = normalized.Split('.').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(shortName) && lookup.TryGetValue(shortName, out enumType))
        {
            return enumType;
        }

        return null;
    }

    private static string NormalizeFilterFieldNames(
        string filter,
        Dictionary<string, string> fieldLookup,
        out int replacementCount)
    {
        var replacements = 0;
        if (string.IsNullOrWhiteSpace(filter) || fieldLookup.Count == 0)
        {
            replacementCount = 0;
            return filter;
        }

        var normalized = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\b",
            m =>
            {
                var field = m.Groups["field"].Value;
                var normalized = NormalizeEntityKey(field);
                if (string.IsNullOrWhiteSpace(normalized) || !fieldLookup.TryGetValue(normalized, out var actual))
                {
                    return m.Value;
                }

                if (string.Equals(field, actual, StringComparison.Ordinal))
                {
                    return m.Value;
                }

                replacements++;
                return $"{actual} {m.Groups["op"].Value}";
            },
            RegexOptions.IgnoreCase);

        replacementCount = replacements;
        return normalized;
    }

    private static string? ResolveEnumMember(ODataEnumType enumType, string member)
    {
        if (string.IsNullOrWhiteSpace(member))
        {
            return null;
        }

        return enumType.Members.FirstOrDefault(m => string.Equals(m, member, StringComparison.OrdinalIgnoreCase));
    }

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string AppendNote(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return next;
        }

        return $"{current} {next}";
    }

    private string ResolveFoEntityName(params string?[] sourceSchemas)
    {
        if (_foEntityLookup is null || _foEntityLookup.Count == 0 || _foEntityNames.Count == 0)
        {
            return string.Empty;
        }

        foreach (var sourceSchema in sourceSchemas)
        {
            var resolved = ResolveFoEntityNameSingle(sourceSchema);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return string.Empty;
    }

    private string ResolveFoEntityNameSingle(string? sourceSchema)
    {
        if (string.IsNullOrWhiteSpace(sourceSchema))
        {
            return string.Empty;
        }

        var aliases = BuildNormalizedAliases(sourceSchema);
        foreach (var alias in aliases)
        {
            if (_foEntityLookup!.TryGetValue(alias, out var direct))
            {
                return direct;
            }
        }

        var sourceTokens = TokenizeName(sourceSchema)
            .Where(t => !StopTokens.Contains(t))
            .ToList();
        if (sourceTokens.Count == 0)
        {
            sourceTokens = TokenizeName(sourceSchema).ToList();
        }

        var ranked = new List<(string Name, int Score)>(_foEntityNames.Count);
        foreach (var entityName in _foEntityNames)
        {
            var score = ScoreEntityName(entityName, aliases, sourceTokens);
            if (score > int.MinValue)
            {
                ranked.Add((entityName, score));
            }
        }

        if (ranked.Count == 0)
        {
            return string.Empty;
        }

        var best = ranked.OrderByDescending(r => r.Score).First();
        if (best.Score < 110)
        {
            return string.Empty;
        }

        var second = ranked
            .Where(r => !string.Equals(r.Name, best.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.Score)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(second.Name) && second.Score >= best.Score - 8)
        {
            return string.Empty;
        }

        return best.Name;
    }

    private static readonly HashSet<string> StopTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "cds", "dynamics", "d365", "entity", "entities", "the", "of", "and", "for", "data"
    };

    private static List<string> BuildNormalizedAliases(string sourceSchema)
    {
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var raw = sourceSchema.Trim();
        var withoutParen = Regex.Replace(raw, @"\([^)]*\)", " ");
        var tokens = TokenizeName(withoutParen).ToList();
        var filtered = tokens.Where(t => !StopTokens.Contains(t)).ToList();

        AddAlias(aliases, raw);
        AddAlias(aliases, withoutParen);
        AddAlias(aliases, string.Concat(filtered));
        AddAlias(aliases, string.Concat(filtered.Where(t => !Regex.IsMatch(t, @"^v\d+$", RegexOptions.IgnoreCase))));
        AddAlias(aliases, string.Concat(filtered.Select(t => t.StartsWith("v", StringComparison.OrdinalIgnoreCase) && t.Length > 1 ? t[1..] : t)));

        return aliases.ToList();
    }

    private static void AddAlias(HashSet<string> aliases, string candidate)
    {
        var normalized = NormalizeEntityKey(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
        }
    }

    private static int ScoreEntityName(string entityName, IReadOnlyList<string> aliases, IReadOnlyList<string> sourceTokens)
    {
        var entityNorm = NormalizeEntityKey(entityName);
        if (string.IsNullOrWhiteSpace(entityNorm))
        {
            return int.MinValue;
        }

        var bestScore = int.MinValue;
        foreach (var alias in aliases)
        {
            var score = 0;
            if (string.Equals(entityNorm, alias, StringComparison.OrdinalIgnoreCase))
            {
                score += 220;
            }
            else if (entityNorm.StartsWith(alias, StringComparison.OrdinalIgnoreCase) ||
                     alias.StartsWith(entityNorm, StringComparison.OrdinalIgnoreCase))
            {
                score += 130;
            }
            else if (entityNorm.Contains(alias, StringComparison.OrdinalIgnoreCase) ||
                     alias.Contains(entityNorm, StringComparison.OrdinalIgnoreCase))
            {
                score += 90;
            }

            score -= Math.Abs(entityNorm.Length - alias.Length);
            bestScore = Math.Max(bestScore, score);
        }

        var entityTokens = TokenizeName(entityName)
            .Where(t => !StopTokens.Contains(t))
            .ToList();

        if (entityTokens.Count > 0 && sourceTokens.Count > 0)
        {
            var overlap = entityTokens.Intersect(sourceTokens, StringComparer.OrdinalIgnoreCase).Count();
            bestScore += overlap * 28;

            if (string.Equals(entityTokens[0], sourceTokens[0], StringComparison.OrdinalIgnoreCase))
            {
                bestScore += 20;
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> TokenizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var withBoundaries = Regex.Replace(value, @"([a-z])([A-Z])", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"([A-Za-z])(\d)", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"(\d)([A-Za-z])", "$1 $2");

        foreach (Match match in Regex.Matches(withBoundaries, @"[A-Za-z0-9]+"))
        {
            var token = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token.ToLowerInvariant();
            }
        }
    }

    private static string NormalizeEntityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private async Task<(long? Count, string Note)> GetFoCountWithFallbackAsync(string foEntity, string? oDataFilter, CancellationToken cancellationToken)
    {
        var candidates = BuildFoFilterCandidates(oDataFilter);
        Exception? lastException = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[i];
            try
            {
                var count = await GetFoCountAsync(foEntity, candidate, cancellationToken);
                if (i == 0)
                {
                    return (count, string.Empty);
                }

                return (count, $"FO filter fallback variant {i + 1} of {candidates.Count} succeeded.");
            }
            catch (Exception ex) when (i < candidates.Count - 1 && IsHttp400(ex))
            {
                lastException = ex;
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        if (lastException is not null)
        {
            var attempted = string.Join(" || ", candidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => TrimForStatus(c!))
                .Take(6));
            throw new InvalidOperationException(
                $"FO count failed after {candidates.Count} filter variant(s). Last error: {lastException.Message} Attempted filters: {attempted}",
                lastException);
        }

        return (null, string.Empty);
    }

    private static List<string?> BuildFoFilterCandidates(string? oDataFilter)
    {
        var candidates = new List<string?> { oDataFilter };

        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            var boolNoYes = oDataFilter;
            boolNoYes = Regex.Replace(
                boolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'Yes'",
                "true",
                RegexOptions.IgnoreCase);
            boolNoYes = Regex.Replace(
                boolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'No'",
                "false",
                RegexOptions.IgnoreCase);
            if (!string.Equals(boolNoYes, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(boolNoYes);
            }

            var plainMember = Regex.Replace(
                oDataFilter,
                @"\b[A-Za-z_][A-Za-z0-9_.]*'([A-Za-z_][A-Za-z0-9_]*)'",
                "'$1'");
            if (!string.Equals(plainMember, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(plainMember);
            }

            var partyTypeTyped = Regex.Replace(
                oDataFilter,
                @"\bPartyType\s+(eq|ne)\s+'([A-Za-z_][A-Za-z0-9_]*)'",
                "PartyType $1 Microsoft.Dynamics.DataEntities.DirPartyType'$2'",
                RegexOptions.IgnoreCase);
            if (!string.Equals(partyTypeTyped, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(partyTypeTyped);
            }

            var partyTypeBoolNoYes = Regex.Replace(
                partyTypeTyped,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'Yes'",
                "true",
                RegexOptions.IgnoreCase);
            partyTypeBoolNoYes = Regex.Replace(
                partyTypeBoolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'No'",
                "false",
                RegexOptions.IgnoreCase);
            if (!string.Equals(partyTypeBoolNoYes, partyTypeTyped, StringComparison.Ordinal))
            {
                candidates.Add(partyTypeBoolNoYes);
            }
        }

        return candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsHttp400(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains(" 400 ", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("400 (", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("StatusCode: 400", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<long?> GetFoCountAsync(string foEntity, string? oDataFilter, CancellationToken cancellationToken)
    {
        var spec = new QuerySpec(
            Entity: foEntity,
            Filter: string.IsNullOrWhiteSpace(oDataFilter) ? null : oDataFilter,
            Top: 1,
            Count: true,
            CrossCompany: true);

        var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        await foreach (var page in _ctx.OData.StreamAsync(request, cancellationToken))
        {
            return page.ODataCount ?? page.Rows.Count;
        }

        return null;
    }

    private static async Task<long?> GetDataverseCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { "$top=1", "$count=true" };
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        var url = $"{apiBase}/{entitySetName}?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=1");

        using var response = await dataverseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Dataverse count request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("@odata.count", out var countElement))
        {
            if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt64(out var number))
            {
                return number;
            }

            if (countElement.ValueKind == JsonValueKind.String && long.TryParse(countElement.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static async Task<long?> GetDataverseExactCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        // Exact mode is a true page walk to avoid the Dataverse 5,000 row count ceiling behavior.
        return await GetDataversePagedCountAsync(dataverseHttp, apiBase, entitySetName, oDataFilter, cancellationToken);
    }

    private static async Task<long?> GetDataversePagedCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        var nextLink = BuildDataversePagedCountStartUrl(apiBase, entitySetName, oDataFilter);
        long total = 0;
        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=5000");

            using var response = await dataverseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Dataverse exact count paging failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Dataverse exact count paging response did not contain a 'value' array.");
            }

            total += valueArray.GetArrayLength();
            nextLink = GetValueAsString(root, "@odata.nextLink");
        }

        return total;
    }

    private static string BuildDataversePagedCountStartUrl(string apiBase, string entitySetName, string? oDataFilter)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        var baseUrl = $"{apiBase}/{entitySetName}";
        return query.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", query)}";
    }

    private static string ConvertXppFilterToOData(string? xppFilter, out string conversionNote)
    {
        conversionNote = string.Empty;
        if (string.IsNullOrWhiteSpace(xppFilter))
        {
            return string.Empty;
        }

        var source = xppFilter.Trim();
        var output = new System.Text.StringBuilder(source.Length * 2);
        var inString = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (ch == '"')
            {
                inString = !inString;
                output.Append('\'');
                continue;
            }

            if (inString)
            {
                output.Append(ch == '\'' ? "''" : ch);
                continue;
            }

            if (ch == '&' && i + 1 < source.Length && source[i + 1] == '&')
            {
                output.Append(" and ");
                i++;
                continue;
            }

            if (ch == '|' && i + 1 < source.Length && source[i + 1] == '|')
            {
                output.Append(" or ");
                i++;
                continue;
            }

            if (ch == '=' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" eq ");
                i++;
                continue;
            }

            if (ch == '=')
            {
                output.Append(" eq ");
                continue;
            }

            if (ch == '!' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ne ");
                i++;
                continue;
            }

            if (ch == '>' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ge ");
                i++;
                continue;
            }

            if (ch == '<' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" le ");
                i++;
                continue;
            }

            if (ch == '>')
            {
                output.Append(" gt ");
                continue;
            }

            if (ch == '<')
            {
                output.Append(" lt ");
                continue;
            }

            if (ch is '\r' or '\n' or '\t')
            {
                output.Append(' ');
                continue;
            }

            output.Append(ch);
        }

        var converted = output.ToString();

        return Regex.Replace(converted, @"\s+", " ").Trim();
    }

    private void ClearRecords()
    {
        _records.Clear();
        SelectedRecord = null;
        RecordsView.Refresh();
        UpdateRecordSummary();
        ClearCountSetup();
        _countResults.Clear();
        ClearTestifyState();
        CountSummary = "No count run yet.";
        StatusMessage = "Cleared.";
    }

    private bool RecordFilter(object? item)
    {
        if (item is not DualWriteMapRecord record)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var term = SearchText.Trim();
        return record.Name.Contains(term, System.StringComparison.OrdinalIgnoreCase)
            || record.DisplayName.Contains(term, System.StringComparison.OrdinalIgnoreCase)
            || record.Version.Contains(term, System.StringComparison.OrdinalIgnoreCase)
            || record.State.Contains(term, System.StringComparison.OrdinalIgnoreCase)
            || record.Status.Contains(term, System.StringComparison.OrdinalIgnoreCase)
            || record.Owner.Contains(term, System.StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateRecordSummary()
    {
        var visible = RecordsView.Cast<object>().Count();
        RecordSummary = $"Showing {visible} of {_records.Count} records";
    }

    private static DualWriteMapRecord ParseRecord(JsonElement item)
    {
        var stateName = GetValueAsString(item, "statecode@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "statecodename")
            ?? GetValueAsString(item, "statecode")
            ?? string.Empty;

        var statusName = GetValueAsString(item, "statuscode@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "statuscodename")
            ?? GetValueAsString(item, "statuscode")
            ?? string.Empty;

        var owner = GetValueAsString(item, "_ownerid_value@OData.Community.Display.V1.FormattedValue")
            ?? GetValueAsString(item, "owneridname")
            ?? GetValueAsString(item, "_ownerid_value")
            ?? GetValueAsString(item, "ownerid")
            ?? string.Empty;

        var mappingRaw = GetValueAsString(item, "msdyn_mapping");
        var propertiesRaw = GetValueAsString(item, "msdyn_properties");
        var mappingRoot = TryParseJsonElement(mappingRaw);
        var propertiesRoot = TryParseJsonElement(propertiesRaw);

        return new DualWriteMapRecord(
            id: GetValueAsString(item, "msdyn_dualwriteentitymapid") ?? string.Empty,
            solutionId: GetValueAsString(item, "solutionid") ?? string.Empty,
            name: GetValueAsString(item, "msdyn_name") ?? string.Empty,
            displayName: GetValueAsString(item, "msdyn_displayname") ?? string.Empty,
            version: GetValueAsString(item, "msdyn_version") ?? string.Empty,
            state: stateName,
            status: statusName,
            owner: owner,
            createdOn: ParseDate(GetValueAsString(item, "createdon")),
            modifiedOn: ParseDate(GetValueAsString(item, "modifiedon")),
            mappingRows: BuildFlattenedRows(mappingRoot, mappingRaw),
            mappingSummaryRows: BuildMappingSummaryRows(mappingRoot),
            mappingLegRows: BuildMappingLegRows(mappingRoot),
            mappingFieldRows: BuildMappingFieldRows(mappingRoot),
            mappingValueTransformRows: BuildMappingValueTransformRows(mappingRoot),
            propertiesRows: BuildPropertiesRows(propertiesRoot, propertiesRaw),
            mappingRaw: mappingRaw,
            propertiesRaw: propertiesRaw);
    }

    private static string? GetValueAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static JsonElement? TryParseJsonElement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", System.StringComparison.Ordinal) &&
            !trimmed.StartsWith("[", System.StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<JsonTableRow> BuildFlattenedRows(JsonElement? root, string? fallbackRaw)
    {
        if (root is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return Array.Empty<JsonTableRow>();
            }

            return new[] { new JsonTableRow("$", "String", fallbackRaw) };
        }

        var rows = new List<JsonTableRow>();
        AppendJsonRows(root.Value, "$", rows);
        return rows;
    }

    private static void AppendJsonRows(JsonElement element, string path, List<JsonTableRow> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var hasProperties = false;
                foreach (var property in element.EnumerateObject())
                {
                    hasProperties = true;
                    var childPath = path == "$" ? $"$.{property.Name}" : $"{path}.{property.Name}";
                    AppendJsonRows(property.Value, childPath, rows);
                }

                if (!hasProperties)
                {
                    rows.Add(new JsonTableRow(path, "Object", "{}"));
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AppendJsonRows(item, $"{path}[{index}]", rows);
                    index++;
                }

                if (index == 0)
                {
                    rows.Add(new JsonTableRow(path, "Array", "[]"));
                }
                break;
            }
            case JsonValueKind.String:
                rows.Add(new JsonTableRow(path, "String", element.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number:
                rows.Add(new JsonTableRow(path, "Number", element.ToString()));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                rows.Add(new JsonTableRow(path, "Boolean", element.GetBoolean() ? "true" : "false"));
                break;
            case JsonValueKind.Null:
                rows.Add(new JsonTableRow(path, "Null", "null"));
                break;
            default:
                rows.Add(new JsonTableRow(path, element.ValueKind.ToString(), element.ToString()));
                break;
        }
    }

    private static IReadOnlyList<MappingSummaryRow> BuildMappingSummaryRows(JsonElement? mappingRoot)
    {
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<MappingSummaryRow>();
        }

        var rows = new List<MappingSummaryRow>();
        foreach (var property in mappingRoot.Value.EnumerateObject())
        {
            if (property.NameEquals("legs"))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    rows.Add(new MappingSummaryRow("legs.count", property.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture)));
                }
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            rows.Add(new MappingSummaryRow(property.Name, GetPrimitiveValue(property.Value)));
        }

        return rows;
    }

    private static IReadOnlyList<MappingLegRow> BuildMappingLegRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingLegRow>();
        }

        var rows = new List<MappingLegRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var fieldCount = 0;
            if (leg.TryGetProperty("fieldMappings", out var fieldMappings) && fieldMappings.ValueKind == JsonValueKind.Array)
            {
                fieldCount = fieldMappings.GetArrayLength();
            }

            rows.Add(new MappingLegRow(
                legId: GetJsonString(leg, "id"),
                sourceSchema: GetJsonString(leg, "sourceSchema"),
                sourceSchemaDistinctName: GetJsonString(leg, "sourceSchemaDistinctName"),
                destinationSchema: GetJsonString(leg, "destinationSchema"),
                sourceEnvironmentType: GetJsonString(leg, "sourceEnvironmentType"),
                destinationEnvironmentType: GetJsonString(leg, "destinationEnvironmentType"),
                sourceFilter: GetJsonString(leg, "sourceFilter"),
                reversedSourceFilter: GetJsonString(leg, "reversedSourceFilter"),
                fieldMappings: fieldCount));
        }

        return rows;
    }

    private static IReadOnlyList<MappingFieldRow> BuildMappingFieldRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingFieldRow>();
        }

        var rows = new List<MappingFieldRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            var sourceSchema = GetJsonString(leg, "sourceSchema");
            var destinationSchema = GetJsonString(leg, "destinationSchema");

            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var syncDirection = mapping.TryGetProperty("syncDirection", out var dir)
                    ? dir.ToString()
                    : string.Empty;

                var valueTransforms = 0;
                if (mapping.TryGetProperty("valueTransforms", out var transforms) && transforms.ValueKind == JsonValueKind.Array)
                {
                    valueTransforms = transforms.GetArrayLength();
                }

                rows.Add(new MappingFieldRow(
                    legId: legId,
                    sourceSchema: sourceSchema,
                    destinationSchema: destinationSchema,
                    syncDirection: syncDirection,
                    sourceField: GetJsonString(mapping, "sourceField"),
                    destinationField: GetJsonString(mapping, "destinationField"),
                    destinationLookupEntity: GetJsonString(mapping, "destinationLookupFieldRelatedEntity"),
                    isSystemGenerated: GetJsonBool(mapping, "isSystemGenerated"),
                    valueTransforms: valueTransforms));
            }
        }

        return rows;
    }

    private static IReadOnlyList<MappingValueTransformRow> BuildMappingValueTransformRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingValueTransformRow>();
        }

        var rows = new List<MappingValueTransformRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var sourceField = GetJsonString(mapping, "sourceField");
                var destinationField = GetJsonString(mapping, "destinationField");

                if (!mapping.TryGetProperty("valueTransforms", out var transforms) || transforms.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var transform in transforms.EnumerateArray())
                {
                    var valueMap = string.Empty;
                    if (transform.TryGetProperty("valueMap", out var valueMapElement) &&
                        valueMapElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        valueMap = JsonSerializer.Serialize(valueMapElement);
                    }

                    var hasDefaultValue = transform.TryGetProperty("defaultValue", out var defaultValueElement);
                    var defaultValue = hasDefaultValue
                        ? GetNullablePrimitiveValue(defaultValueElement)
                        : null;

                    rows.Add(new MappingValueTransformRow(
                        legId: legId,
                        sourceField: sourceField,
                        destinationField: destinationField,
                        transformType: GetJsonString(transform, "transformType"),
                        defaultValue: defaultValue,
                        hasDefaultValue: hasDefaultValue,
                        valueMap: valueMap,
                        createValuesOnDestination: GetJsonBool(transform, "createValuesOnDestination")));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<PropertyTableRow> BuildPropertiesRows(JsonElement? propertiesRoot, string? fallbackRaw)
    {
        if (propertiesRoot is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return Array.Empty<PropertyTableRow>();
            }

            return new[] { new PropertyTableRow("$", "String", fallbackRaw) };
        }

        var root = propertiesRoot.Value;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var rows = new List<PropertyTableRow>();
            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value;
                rows.Add(new PropertyTableRow(
                    key: property.Name,
                    type: value.ValueKind.ToString(),
                    value: value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? JsonSerializer.Serialize(value)
                        : GetPrimitiveValue(value)));
            }

            return rows;
        }

        return new[]
        {
            new PropertyTableRow("$", root.ValueKind.ToString(), root.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? JsonSerializer.Serialize(root)
                : GetPrimitiveValue(root))
        };
    }

    private static bool TryGetLegsArray(JsonElement? mappingRoot, out JsonElement legs)
    {
        legs = default;
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!mappingRoot.Value.TryGetProperty("legs", out legs) || legs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return true;
    }

    private static string GetPrimitiveValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString()
        };
    }

    private static string? GetNullablePrimitiveValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString()
        };
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return GetPrimitiveValue(value);
    }

    private static bool? GetJsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static string BuildMapsUrl(string apiBase)
    {
        var queryParts = new List<string>
        {
            $"$select={Uri.EscapeDataString(SelectColumns)}",
            "$orderby=modifiedon%20desc"
        };

        return $"{apiBase}/msdyn_dualwriteentitymaps?{string.Join("&", queryParts)}";
    }

    private static async Task<HashSet<Guid>> LoadDualWriteComponentIdsForSolutionAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        var escapedSolution = EscapeODataString(solutionUniqueName);
        var filter = $"(componenttype eq {DualWriteMapComponentType}) and (solutionid/uniquename eq '{escapedSolution}')";
        var nextLink = $"{apiBase}/solutioncomponents?$select=objectid&$filter={Uri.EscapeDataString(filter)}";

        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=500");

            using var response = await dataverseHttp.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Dataverse solutioncomponents request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Dataverse solutioncomponents response did not contain a 'value' array.");
            }

            foreach (var component in valueArray.EnumerateArray())
            {
                var objectId = GetValueAsString(component, "objectid");
                if (Guid.TryParse(objectId, out var parsed))
                {
                    ids.Add(parsed);
                }
            }

            nextLink = GetValueAsString(root, "@odata.nextLink");
        }

        return ids;
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string TrimForStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = text.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        if (compact.Length <= 280)
        {
            return compact;
        }

        return compact[..280] + "...";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DualWriteMapRecord
{
    public DualWriteMapRecord(
        string id,
        string solutionId,
        string name,
        string displayName,
        string version,
        string state,
        string status,
        string owner,
        DateTimeOffset? createdOn,
        DateTimeOffset? modifiedOn,
        IReadOnlyList<JsonTableRow> mappingRows,
        IReadOnlyList<MappingSummaryRow> mappingSummaryRows,
        IReadOnlyList<MappingLegRow> mappingLegRows,
        IReadOnlyList<MappingFieldRow> mappingFieldRows,
        IReadOnlyList<MappingValueTransformRow> mappingValueTransformRows,
        IReadOnlyList<PropertyTableRow> propertiesRows,
        string? mappingRaw,
        string? propertiesRaw)
    {
        Id = id;
        SolutionId = solutionId;
        Name = name;
        DisplayName = displayName;
        Version = version;
        State = state;
        Status = status;
        Owner = owner;
        CreatedOn = createdOn;
        ModifiedOn = modifiedOn;
        MappingRows = mappingRows;
        MappingSummaryRows = mappingSummaryRows;
        MappingLegRows = mappingLegRows;
        MappingFieldRows = mappingFieldRows;
        MappingValueTransformRows = mappingValueTransformRows;
        PropertiesRows = propertiesRows;
        MappingRaw = mappingRaw;
        PropertiesRaw = propertiesRaw;
    }

    public string Id { get; }
    public string SolutionId { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string State { get; }
    public string Status { get; }
    public string Owner { get; }
    public DateTimeOffset? CreatedOn { get; }
    public DateTimeOffset? ModifiedOn { get; }
    public IReadOnlyList<JsonTableRow> MappingRows { get; }
    public IReadOnlyList<MappingSummaryRow> MappingSummaryRows { get; }
    public IReadOnlyList<MappingLegRow> MappingLegRows { get; }
    public IReadOnlyList<MappingFieldRow> MappingFieldRows { get; }
    public IReadOnlyList<MappingValueTransformRow> MappingValueTransformRows { get; }
    public IReadOnlyList<PropertyTableRow> PropertiesRows { get; }
    public string? MappingRaw { get; }
    public string? PropertiesRaw { get; }
    public bool IsSelected { get; set; }
    public string CreatedOnDisplay => FormatDate(CreatedOn);
    public string ModifiedOnDisplay => FormatDate(ModifiedOn);

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

public sealed class MappingSummaryRow
{
    public MappingSummaryRow(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public string Value { get; }
}

public sealed class MappingLegRow
{
    public MappingLegRow(
        string legId,
        string sourceSchema,
        string sourceSchemaDistinctName,
        string destinationSchema,
        string sourceEnvironmentType,
        string destinationEnvironmentType,
        string sourceFilter,
        string reversedSourceFilter,
        int fieldMappings)
    {
        LegId = legId;
        SourceSchema = sourceSchema;
        SourceSchemaDistinctName = sourceSchemaDistinctName;
        DestinationSchema = destinationSchema;
        SourceEnvironmentType = sourceEnvironmentType;
        DestinationEnvironmentType = destinationEnvironmentType;
        SourceFilter = sourceFilter;
        ReversedSourceFilter = reversedSourceFilter;
        FieldMappings = fieldMappings;
    }

    public string LegId { get; }
    public string SourceSchema { get; }
    public string SourceSchemaDistinctName { get; }
    public string DestinationSchema { get; }
    public string SourceEnvironmentType { get; }
    public string DestinationEnvironmentType { get; }
    public string SourceFilter { get; }
    public string ReversedSourceFilter { get; }
    public int FieldMappings { get; }
}

public sealed class MappingFieldRow
{
    public MappingFieldRow(
        string legId,
        string sourceSchema,
        string destinationSchema,
        string syncDirection,
        string sourceField,
        string destinationField,
        string destinationLookupEntity,
        bool? isSystemGenerated,
        int valueTransforms)
    {
        LegId = legId;
        SourceSchema = sourceSchema;
        DestinationSchema = destinationSchema;
        SyncDirection = syncDirection;
        SourceField = sourceField;
        DestinationField = destinationField;
        DestinationLookupEntity = destinationLookupEntity;
        IsSystemGenerated = isSystemGenerated;
        ValueTransforms = valueTransforms;
    }

    public string LegId { get; }
    public string SourceSchema { get; }
    public string DestinationSchema { get; }
    public string SyncDirection { get; }
    public string SourceField { get; }
    public string DestinationField { get; }
    public string DestinationLookupEntity { get; }
    public bool? IsSystemGenerated { get; }
    public int ValueTransforms { get; }
}

public sealed class MappingValueTransformRow
{
    public MappingValueTransformRow(
        string legId,
        string sourceField,
        string destinationField,
        string transformType,
        string? defaultValue,
        bool hasDefaultValue,
        string valueMap,
        bool? createValuesOnDestination)
    {
        LegId = legId;
        SourceField = sourceField;
        DestinationField = destinationField;
        TransformType = transformType;
        DefaultValue = defaultValue;
        HasDefaultValue = hasDefaultValue;
        ValueMap = valueMap;
        CreateValuesOnDestination = createValuesOnDestination;
    }

    public string LegId { get; }
    public string SourceField { get; }
    public string DestinationField { get; }
    public string TransformType { get; }
    public string? DefaultValue { get; }
    public bool HasDefaultValue { get; }
    public string ValueMap { get; }
    public bool? CreateValuesOnDestination { get; }
}

public sealed class PropertyTableRow
{
    public PropertyTableRow(string key, string type, string value)
    {
        Key = key;
        Type = type;
        Value = value;
    }

    public string Key { get; }
    public string Type { get; }
    public string Value { get; }
}

public sealed class FoEntityOption
{
    public static readonly FoEntityOption Auto = new(string.Empty, "(Auto)");

    public FoEntityOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }
    public string DisplayName { get; }
}

public sealed class CountLegConfigRow : INotifyPropertyChanged
{
    private bool _include;
    private string _foEntityOverride;

    public CountLegConfigRow(
        string mapDisplayName,
        string mapId,
        string legId,
        string sourceSchema,
        string sourceSchemaDistinctName,
        string sourceEnvironmentType,
        string destinationEnvironmentType,
        string foEntityResolved,
        string sourceFilterXpp,
        string foFilter,
        string foFilterNote,
        string ceEntity,
        string ceFilter,
        bool include,
        string foEntityOverride)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        LegId = legId;
        SourceSchema = sourceSchema;
        SourceSchemaDistinctName = sourceSchemaDistinctName;
        SourceEnvironmentType = sourceEnvironmentType;
        DestinationEnvironmentType = destinationEnvironmentType;
        FoEntityResolved = foEntityResolved;
        SourceFilterXpp = sourceFilterXpp;
        FoFilter = foFilter;
        FoFilterNote = foFilterNote;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
        _include = include;
        _foEntityOverride = foEntityOverride ?? string.Empty;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string LegId { get; }
    public string SourceSchema { get; }
    public string SourceSchemaDistinctName { get; }
    public string SourceEnvironmentType { get; }
    public string DestinationEnvironmentType { get; }
    public string FoEntityResolved { get; }
    public string SourceFilterXpp { get; }
    public string FoFilter { get; }
    public string FoFilterNote { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }

    public bool Include
    {
        get => _include;
        set
        {
            if (_include == value)
            {
                return;
            }

            _include = value;
            OnPropertyChanged();
        }
    }

    public string FoEntityOverride
    {
        get => _foEntityOverride;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_foEntityOverride, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _foEntityOverride = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FoEntityEffective));
        }
    }

    public string FoEntityEffective =>
        string.IsNullOrWhiteSpace(FoEntityOverride)
            ? FoEntityResolved
            : FoEntityOverride;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CountValidationRow
{
    public CountValidationRow(
        string mapDisplayName,
        string mapId,
        string legId,
        string foEntity,
        string foFilter,
        string ceEntity,
        string ceFilter,
        long? foCount,
        long? ceCount,
        bool? countsMatch,
        string status)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        LegId = legId;
        FoEntity = foEntity;
        FoFilter = foFilter;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
        FoCount = foCount;
        CeCount = ceCount;
        CountsMatch = countsMatch;
        Status = status;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string LegId { get; }
    public string FoEntity { get; }
    public string FoFilter { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }
    public long? FoCount { get; }
    public long? CeCount { get; }
    public bool? CountsMatch { get; }
    public string Status { get; }
}

public sealed class SolutionOption
{
    public SolutionOption(
        Guid id,
        string displayName,
        string uniqueName,
        string friendlyName,
        string version,
        string publisherUniqueName,
        string publisherDisplayName)
    {
        Id = id;
        DisplayName = displayName;
        UniqueName = uniqueName;
        FriendlyName = friendlyName;
        Version = version;
        PublisherUniqueName = publisherUniqueName;
        PublisherDisplayName = publisherDisplayName;
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string UniqueName { get; }
    public string FriendlyName { get; }
    public string Version { get; }
    public string PublisherUniqueName { get; }
    public string PublisherDisplayName { get; }
}

public sealed class PublisherOption
{
    public static readonly PublisherOption All = new(string.Empty, "(All Publishers)", 0);

    public PublisherOption(string uniqueName, string displayName, int solutionCount)
    {
        UniqueName = uniqueName;
        DisplayName = solutionCount > 0 && !string.IsNullOrWhiteSpace(uniqueName)
            ? $"{displayName} ({solutionCount})"
            : displayName;
        SolutionCount = solutionCount;
    }

    public string UniqueName { get; }
    public string DisplayName { get; }
    public int SolutionCount { get; }
    public bool IsAll => string.IsNullOrWhiteSpace(UniqueName);
}

public sealed class JsonTableRow
{
    public JsonTableRow(string path, string type, string value)
    {
        Path = path;
        Type = type;
        Value = value;
    }

    public string Path { get; }
    public string Type { get; }
    public string Value { get; }
}

