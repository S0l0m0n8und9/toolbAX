using FoToolbox.Core.Auth;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;

namespace DualWriteMapBrowserPlugin;

public sealed class DualWriteMapBrowserViewModel : INotifyPropertyChanged
{
    private static readonly string SelectColumns = string.Join(",",
        "msdyn_dualwriteentitymapid",
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
    private readonly ObservableCollection<DualWriteMapRecord> _records = new();
    private string _statusMessage = "Ready.";
    private string _recordSummary = "Showing 0 of 0 records";
    private bool _isLoading;
    private string? _searchText;
    private DualWriteMapRecord? _selectedRecord;

    public DualWriteMapBrowserViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        _dataverse = ctx as IPluginContextDataverse;
        DataverseEndpoint = HasDataverseConnection
            ? ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse!.CurrentDataverseEnv!.BaseUrl)
            : "Dataverse profile not configured. Open Profiles and set CE/Dataverse values.";

        RecordsView = CollectionViewSource.GetDefaultView(_records);
        RecordsView.Filter = RecordFilter;

        Action<Exception> onError = ex =>
        {
            _ctx.Logger.LogError(ex, "DualWriteMapBrowser command failed.");
            StatusMessage = $"Command failed: {ex.Message}";
        };

        LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
        ClearCommand = new RelayCommand(_ => ClearRecords());

        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
        }
    }

    private bool HasDataverseConnection =>
        _dataverse is not null &&
        _dataverse.HasDataverseProfile &&
        _dataverse.DataverseHttp is not null &&
        _dataverse.CurrentDataverseEnv is not null;

    public ICollectionView RecordsView { get; }
    public AsyncRelayCommand LoadMapsCommand { get; }
    public RelayCommand ClearCommand { get; }
    public string DataverseEndpoint { get; }

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
        }
    }

    public bool IsNotLoading => !IsLoading;

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

    private async Task LoadMapsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        IsLoading = true;
        _records.Clear();
        SelectedRecord = null;
        UpdateRecordSummary();
        StatusMessage = "Loading dual-write map records...";

        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var nextLink = $"{apiBase}/msdyn_dualwriteentitymaps?$select={SelectColumns}&$orderby=modifiedon%20desc";
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
                    _records.Add(ParseRecord(item));
                }

                pageCount++;
                nextLink = GetValueAsString(root, "@odata.nextLink");
                StatusMessage = $"Loaded {_records.Count} records so far...";
            }

            RecordsView.Refresh();
            UpdateRecordSummary();
            SelectedRecord ??= _records.FirstOrDefault();
            StatusMessage = $"Loaded {_records.Count} dual-write map records from {pageCount} page(s).";
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

    private void ClearRecords()
    {
        _records.Clear();
        SelectedRecord = null;
        RecordsView.Refresh();
        UpdateRecordSummary();
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
            propertiesRows: BuildPropertiesRows(propertiesRoot, propertiesRaw));
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

                    rows.Add(new MappingValueTransformRow(
                        legId: legId,
                        sourceField: sourceField,
                        destinationField: destinationField,
                        transformType: GetJsonString(transform, "transformType"),
                        defaultValue: GetJsonString(transform, "defaultValue"),
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
        IReadOnlyList<PropertyTableRow> propertiesRows)
    {
        Id = id;
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
    }

    public string Id { get; }
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
        string destinationSchema,
        string sourceEnvironmentType,
        string destinationEnvironmentType,
        string sourceFilter,
        string reversedSourceFilter,
        int fieldMappings)
    {
        LegId = legId;
        SourceSchema = sourceSchema;
        DestinationSchema = destinationSchema;
        SourceEnvironmentType = sourceEnvironmentType;
        DestinationEnvironmentType = destinationEnvironmentType;
        SourceFilter = sourceFilter;
        ReversedSourceFilter = reversedSourceFilter;
        FieldMappings = fieldMappings;
    }

    public string LegId { get; }
    public string SourceSchema { get; }
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
        string defaultValue,
        string valueMap,
        bool? createValuesOnDestination)
    {
        LegId = legId;
        SourceField = sourceField;
        DestinationField = destinationField;
        TransformType = transformType;
        DefaultValue = defaultValue;
        ValueMap = valueMap;
        CreateValuesOnDestination = createValuesOnDestination;
    }

    public string LegId { get; }
    public string SourceField { get; }
    public string DestinationField { get; }
    public string TransformType { get; }
    public string DefaultValue { get; }
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
