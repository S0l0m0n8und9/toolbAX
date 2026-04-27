using FoToolbox.Core.Auth;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace DualWriteMapBrowserPlugin;

public sealed partial class DualWriteMapBrowserViewModel
{
    internal static Func<TimeSpan, CancellationToken, Task> TestifyDelayAsync { get; set; } = Task.Delay;
    internal static Func<DateTimeOffset> TestifyUtcNow { get; set; } = static () => DateTimeOffset.UtcNow;

    private readonly IPluginContextWrite? _write;
    private readonly ObservableCollection<TestifyPreflightRow> _testifyPreflightRows = new();
    private readonly ObservableCollection<TestifyExecutionLogRow> _testifyLogRows = new();
    private readonly ObservableCollection<TestifyResultRow> _testifyResultRows = new();
    private readonly ReadOnlyObservableCollection<TestifyPreflightRow> _testifyPreflightRowsReadOnly;
    private readonly ReadOnlyObservableCollection<TestifyExecutionLogRow> _testifyLogRowsReadOnly;
    private readonly ReadOnlyObservableCollection<TestifyResultRow> _testifyResultRowsReadOnly;
    private readonly Dictionary<string, TestifyMapPlan> _testifyPlans = new(StringComparer.OrdinalIgnoreCase);
    private readonly TestifyConfigurationStore _testifyConfigStore;

    private bool _isPreparingTestify;
    private bool _isRunningTestify;
    private string _testifySummary = "No Testify run yet.";

    public AsyncRelayCommand PrepareTestifyCommand { get; }
    public AsyncRelayCommand RunTestifyCommand { get; }
    public AsyncRelayCommand CleanupTestifyCommand { get; }

    public ReadOnlyObservableCollection<TestifyPreflightRow> TestifyPreflightRows => _testifyPreflightRowsReadOnly;
    public ReadOnlyObservableCollection<TestifyExecutionLogRow> TestifyLogRows => _testifyLogRowsReadOnly;
    public ReadOnlyObservableCollection<TestifyResultRow> TestifyResultRows => _testifyResultRowsReadOnly;

    public string TestifySummary
    {
        get => _testifySummary;
        private set
        {
            if (string.Equals(_testifySummary, value, StringComparison.Ordinal))
            {
                return;
            }

            _testifySummary = value;
            OnPropertyChanged();
        }
    }

    private bool IsPreparingTestify
    {
        get => _isPreparingTestify;
        set
        {
            if (_isPreparingTestify == value)
            {
                return;
            }

            _isPreparingTestify = value;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    private bool IsRunningTestify
    {
        get => _isRunningTestify;
        set
        {
            if (_isRunningTestify == value)
            {
                return;
            }

            _isRunningTestify = value;
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    private async Task PrepareTestifyAsync(CancellationToken cancellationToken)
    {
        if (_write?.ODataWrite is null)
        {
            StatusMessage = "Testify requires OData.Write capability, but it is not available in this host context.";
            return;
        }

        var selectedMaps = GetMapsForCounting();
        if (selectedMaps.Count == 0)
        {
            StatusMessage = "Select one or more maps (checkbox), or select a current map.";
            return;
        }

        IsPreparingTestify = true;
        _testifyPreflightRows.Clear();
        _testifyLogRows.Clear();
        _testifyResultRows.Clear();
        _testifyPlans.Clear();
        TestifySummary = "Preparing Testify preflight...";

        try
        {
            await EnsureFoEntityLookupAsync(cancellationToken);

            var totalPlannedUpdates = 0;
            var runnable = 0;

            foreach (var map in selectedMaps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var plan = await BuildTestifyMapPlanAsync(map, cancellationToken);
                _testifyPlans[map.Id] = plan;

                var blockingIssue = FormatBlockingIssue(plan);
                var rowStatus = plan.CanRun
                    ? (plan.Warnings.Count > 0 ? "Ready (with warnings)" : "Ready")
                    : GetBlockedStatus(plan);
                var row = new TestifyPreflightRow(
                    mapDisplayName: plan.MapDisplayName,
                    mapId: plan.MapId,
                    foEntity: plan.FoEntity,
                    enumFields: plan.EnumFields.Count,
                    plannedUpdates: plan.PatchSteps.Count,
                    isReady: plan.CanRun,
                    status: rowStatus,
                    blockingIssue: blockingIssue,
                    coverageGaps: plan.CoverageGaps);
                _testifyPreflightRows.Add(row);

                if (plan.CanRun)
                {
                    runnable++;
                    totalPlannedUpdates += plan.PatchSteps.Count;
                }
            }

            var blocked = _testifyPreflightRows.Count - runnable;
            TestifySummary = $"Preflight complete. Maps: {_testifyPreflightRows.Count}. Ready: {runnable}. Blocked: {blocked}. Planned PATCH updates: {totalPlannedUpdates}.";
            StatusMessage = "Testify preflight complete.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TestifySummary = "Testify preflight cancelled.";
            StatusMessage = "Testify preflight cancelled.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Testify preflight failed.");
            TestifySummary = "Testify preflight failed.";
            StatusMessage = $"Testify preflight failed: {ex.Message}";
        }
        finally
        {
            IsPreparingTestify = false;
        }
    }

    private async Task RunTestifyAsync(CancellationToken cancellationToken)
    {
        if (_write?.ODataWrite is null)
        {
            StatusMessage = "Testify requires OData.Write capability, but it is not available in this host context.";
            return;
        }

        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        if (_testifyPlans.Count == 0)
        {
            await PrepareTestifyAsync(cancellationToken);
            if (_testifyPlans.Count == 0)
            {
                return;
            }
        }

        var runnablePlans = _testifyPlans.Values.Where(p => p.CanRun).ToList();
        if (runnablePlans.Count == 0)
        {
            StatusMessage = "No Testify-ready maps. Run 'Prepare Testify' and resolve blocking issues.";
            return;
        }

        var totalUpdates = runnablePlans.Sum(p => p.PatchSteps.Count);
        var perMapBreakdown = string.Join(
            Environment.NewLine,
            runnablePlans
                .OrderBy(p => p.MapDisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"- {p.MapDisplayName}: {p.PatchSteps.Count} PATCH"));
        var confirmation = MessageBox.Show(
            $"Run Testify for {runnablePlans.Count} map(s)?\n\nPer-map PATCH totals:\n{perMapBreakdown}\n\nTotal planned PATCH updates: {totalUpdates}.\n\nThis will create and update FO records and validate CE visibility.",
            "Confirm Testify",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "Testify run cancelled.";
            return;
        }

        IsRunningTestify = true;
        _testifyLogRows.Clear();
        _testifyResultRows.Clear();
        TestifySummary = "Running Testify...";

        try
        {
            var allPlans = _testifyPlans.Values
                .OrderBy(p => p.MapDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var plan in allPlans)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!plan.CanRun)
                {
                    var blockedStatus = FormatBlockingIssue(plan);
                    AddTestifyLog(plan.MapDisplayName, "Preflight", "Blocked", blockedStatus);
                    _testifyResultRows.Add(new TestifyResultRow(
                        plan.MapDisplayName,
                        plan.MapId,
                        valid: false,
                        createSucceeded: false,
                        patchesPlanned: plan.PatchSteps.Count,
                        patchesSucceeded: 0,
                        ceVerificationSucceeded: false,
                        status: GetBlockedStatus(plan),
                        coverageGaps: plan.CoverageGaps));
                    continue;
                }

                var createSucceeded = false;
                var patchesSucceeded = 0;
                var ceSucceeded = false;
                var valid = false;
                var status = "Unknown error.";
                var createdThisRun = false;
                Dictionary<string, TestifyCorrelatedCeRow>? correlatedCeRows = null;
                var fieldAssertionResults = new List<TestifyFieldAssertionResult>();

                try
                {
                    var runtimeCreateValues = new Dictionary<string, string>(plan.CreateValues, StringComparer.OrdinalIgnoreCase);
                    string entityInstanceUrl;

                    // Idempotency: reuse the record from the last run if it still exists.
                    var reusingExisting = false;
                    if (!string.IsNullOrWhiteSpace(plan.Configuration.LastEntityInstanceUrl))
                    {
                        var existingUrl = plan.Configuration.LastEntityInstanceUrl!;
                        var recordExists = await CheckFoRecordExistsAsync(existingUrl, cancellationToken);
                        if (recordExists)
                        {
                            entityInstanceUrl = existingUrl;
                            reusingExisting = true;
                            createSucceeded = true;
                            AddTestifyLog(plan.MapDisplayName, "Create", "Skipped", $"Reusing existing test record from last run: {existingUrl}");
                        }
                        else
                        {
                            AddTestifyLog(plan.MapDisplayName, "Create", "Info", "Previous test record no longer exists; creating fresh record.");
                            plan.Configuration.LastEntityInstanceUrl = null;
                            plan.Configuration.LastRunToken = null;
                            await _testifyConfigStore.SaveAsync(plan.Configuration, cancellationToken);
                        }
                    }

                    if (!reusingExisting)
                    {
                        AddTestifyLog(plan.MapDisplayName, "Create", "Started", "Creating FO test record.");

                        var createResponse = await SendCreateWithRetryAsync(plan, runtimeCreateValues, plan.Configuration, cancellationToken);
                        if (!IsSuccessfulStatusCode(createResponse.StatusCode))
                        {
                            throw new InvalidOperationException($"FO create failed: HTTP {createResponse.StatusCode}. {TrimForStatus(createResponse.Body ?? string.Empty)}");
                        }

                        MergeKeyValuesFromCreateResponse(plan.FoEntityDetails!, createResponse.Body, runtimeCreateValues);

                        createSucceeded = true;
                        createdThisRun = true;
                        AddTestifyLog(plan.MapDisplayName, "Create", "Succeeded", $"FO create returned HTTP {createResponse.StatusCode}.");

                        var collectionUrl = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, plan.FoEntity);
                        if (!TestifyRunner.TryBuildEntityInstanceUrl(collectionUrl, plan.FoEntityDetails!, runtimeCreateValues, out entityInstanceUrl, out var keyError))
                        {
                            throw new InvalidOperationException(keyError);
                        }

                        // Persist the instance URL immediately so downstream rollback can clear stale idempotency metadata.
                        plan.Configuration.LastRunToken = runtimeCreateValues.TryGetValue("FOTBTestifyRunId", out var tok) ? tok
                            : runtimeCreateValues.TryGetValue("Name", out tok) ? tok
                            : runtimeCreateValues.TryGetValue("Description", out tok) ? tok
                            : null;
                        plan.Configuration.LastEntityInstanceUrl = entityInstanceUrl;
                        await _testifyConfigStore.SaveAsync(plan.Configuration, cancellationToken);

                        correlatedCeRows = await WaitForCorrelatedCeRowsAsync(plan, runtimeCreateValues, correlatedRows: null, cancellationToken, "after create");
                        fieldAssertionResults.AddRange(await VerifyCeFieldAssertionsAsync(plan, runtimeCreateValues, correlatedCeRows, cancellationToken, "after create"));
                        AddTestifyLog(plan.MapDisplayName, "CE Verify", "Succeeded", "Correlated CE row located after create.");
                    }
                    else
                    {
                        entityInstanceUrl = plan.Configuration.LastEntityInstanceUrl!;
                    }

                    correlatedCeRows ??= await WaitForCorrelatedCeRowsAsync(plan, runtimeCreateValues, correlatedRows: null, cancellationToken, "before patch verification");

                    foreach (var step in plan.PatchSteps)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        if (!TryBuildPatchPayload(plan, step, out var patchJson, out var patchError))
                        {
                            throw new InvalidOperationException(patchError);
                        }

                        AddTestifyLog(plan.MapDisplayName, "Patch", "Started", $"PATCH step {step.StepNumber} of {plan.PatchSteps.Count}.");
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["If-Match"] = "*"
                        };

                        var patchResponse = await _write.ODataWrite.SendAsync(
                            new ODataWriteRequest(new HttpMethod("PATCH"), entityInstanceUrl, patchJson, headers),
                            cancellationToken);

                        if (!IsSuccessfulStatusCode(patchResponse.StatusCode))
                        {
                            throw new InvalidOperationException($"FO PATCH step {step.StepNumber} failed: HTTP {patchResponse.StatusCode}. {TrimForStatus(patchResponse.Body ?? string.Empty)}");
                        }

                        patchesSucceeded++;
                        AddTestifyLog(plan.MapDisplayName, "Patch", "Succeeded", $"PATCH step {step.StepNumber} returned HTTP {patchResponse.StatusCode}.");

                        correlatedCeRows = await WaitForCorrelatedCeRowsAsync(plan, runtimeCreateValues, correlatedCeRows, cancellationToken, $"after patch {step.StepNumber}");
                        fieldAssertionResults.AddRange(await VerifyCeFieldAssertionsAsync(plan, runtimeCreateValues, correlatedCeRows, cancellationToken, $"after patch {step.StepNumber}"));
                        AddTestifyLog(plan.MapDisplayName, "CE Verify", "Succeeded", $"Correlated CE row reused after patch {step.StepNumber}.");
                    }

                    ceSucceeded = DidCeVerificationSucceedForCompletedRun(
                        createSucceeded,
                        patchesSucceeded,
                        plan.PatchSteps.Count,
                        correlatedCeVerificationSucceeded: correlatedCeRows is not null && fieldAssertionResults.All(result => result.Passed));
                    valid = true;
                    status = "Valid map.";
                    AddTestifyLog(plan.MapDisplayName, "Result", "Valid", status);
                }
                catch (Exception ex)
                {
                    status = await FinalizeTestifyFailureAsync(
                        plan.MapDisplayName,
                        plan.MapId,
                        plan.Configuration,
                        createdThisRun,
                        ex.Message,
                        cancellationToken);
                    AddTestifyLog(plan.MapDisplayName, "Result", "Failed", status);
                    _ctx.Logger.LogError(ex, "Testify failed for map {MapId} ({MapDisplayName})", plan.MapId, plan.MapDisplayName);
                }

                _testifyResultRows.Add(new TestifyResultRow(
                    plan.MapDisplayName,
                    plan.MapId,
                    valid,
                    createSucceeded,
                    plan.PatchSteps.Count,
                    patchesSucceeded,
                    ceSucceeded,
                    status,
                    plan.CoverageGaps,
                    fieldAssertionResults));
            }

            var validCount = _testifyResultRows.Count(r => r.Valid);
            var invalidCount = _testifyResultRows.Count - validCount;
            var createFailures = _testifyResultRows.Count(r => !r.CreateSucceeded);
            var ceFailures = _testifyResultRows.Count(r => !r.CeVerificationSucceeded && r.CreateSucceeded);
            TestifySummary = $"Testify complete. Maps: {_testifyResultRows.Count}. Valid: {validCount}. Invalid: {invalidCount}. Create failures: {createFailures}. CE verification failures: {ceFailures}.";
            StatusMessage = "Testify run complete.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TestifySummary = "Testify run cancelled.";
            StatusMessage = "Testify run cancelled.";
        }
        finally
        {
            IsRunningTestify = false;
        }
    }

    private async Task<TestifyMapPlan> BuildTestifyMapPlanAsync(DualWriteMapRecord map, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var blockingIssues = new List<string>();
        var coverageGaps = new List<TestifyEnumCoverageGap>();
        var configuration = await _testifyConfigStore.GetOrCreateAsync(_ctx.CurrentEnv.Id, map.Id, cancellationToken);

        var axToCrmLegs = map.MappingLegRows
            .Where(leg =>
                string.Equals(leg.SourceEnvironmentType, "AX", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(leg.DestinationEnvironmentType, "CRM", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (axToCrmLegs.Count == 0)
        {
            blockingIssues.Add("No AX->CRM legs found in map.");
        }

        var ceLegs = new List<TestifyLegPlan>();

        var foEntityCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var leg in axToCrmLegs)
        {
            var resolved = ResolveFoEntityName(leg.SourceSchemaDistinctName, leg.SourceSchema);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                foEntityCandidates.Add(resolved);
            }
        }

        if (foEntityCandidates.Count == 0)
        {
            blockingIssues.Add("Unable to resolve FO entity from AX->CRM legs.");
        }

        if (foEntityCandidates.Count > 1)
        {
            blockingIssues.Add($"Map resolves to multiple FO entities: {string.Join(", ", foEntityCandidates.OrderBy(v => v, StringComparer.OrdinalIgnoreCase))}.");
        }

        var foEntity = foEntityCandidates.FirstOrDefault() ?? string.Empty;
        var foFilter = string.Empty;

        if (!string.IsNullOrWhiteSpace(foEntity) && axToCrmLegs.Count > 0)
        {
            var converted = await ConvertSourceFilterToODataAsync(foEntity, axToCrmLegs[0].SourceFilter, cancellationToken);
            foFilter = converted.Filter;
            if (!string.IsNullOrWhiteSpace(converted.Note))
            {
                warnings.Add(converted.Note);
            }
        }

        ODataEntity? foEntityDetails = null;
        if (!string.IsNullOrWhiteSpace(foEntity))
        {
            foEntityDetails = await GetFoEntityDetailsCachedAsync(foEntity, cancellationToken);
            if (foEntityDetails is null)
            {
                blockingIssues.Add($"FO entity '{foEntity}' was not found in metadata.");
            }
        }

        var enumMembersByType = TestifyRunner.BuildEnumMembersByTypeLookup(_foEnumLookup);
        var rawMapProperties = TestifyPlanner.ExtractMapPropertyCandidates(map.MappingRaw, map.PropertiesRaw);

        var createValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var enumFieldPlans = new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase);
        var fieldAssertions = new List<TestifyFieldAssertionPlan>();
        var patchSteps = Array.Empty<TestifyPatchStep>();
        var createPayloadJson = string.Empty;

        if (foEntityDetails is not null)
        {
            var normalizedMapProperties = TestifyPlanner.NormalizeMapProperties(rawMapProperties, foEntityDetails.Properties, out var normalizeWarnings);
            warnings.AddRange(normalizeWarnings);

            foreach (var pair in normalizedMapProperties)
            {
                createValues[pair.Key] = pair.Value;
            }

            ApplyLearnedConfigToCreateValues(foEntityDetails, configuration, createValues, warnings);

            var fieldNameLookup = foEntityDetails.Properties
                .GroupBy(p => TestifyPlanner.NormalizeKey(p.Name), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

            var equalityConstraintsRaw = TestifyPlanner.ExtractEqualityConstraints(foFilter);
            var equalityConstraints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in equalityConstraintsRaw)
            {
                var normalized = TestifyPlanner.NormalizeKey(pair.Key);
                if (fieldNameLookup.TryGetValue(normalized, out var actualField))
                {
                    equalityConstraints[actualField] = pair.Value;
                }
            }

            foreach (var pair in equalityConstraints)
            {
                createValues[pair.Key] = pair.Value;
            }

            foreach (var leg in axToCrmLegs.Where(leg => !string.IsNullOrWhiteSpace(leg.DestinationSchema)))
            {
                var correlation = TryBuildCeCorrelationPlan(leg, map.MappingFieldRows, fieldNameLookup, equalityConstraints);
                if (correlation is null)
                {
                    blockingIssues.Add($"Unable to determine deterministic CE correlation for leg '{leg.LegId}' ({leg.DestinationSchema}).");
                    continue;
                }

                ceLegs.Add(correlation);
            }

            var axLegIds = new HashSet<string>(axToCrmLegs.Select(l => l.LegId), StringComparer.OrdinalIgnoreCase);
            var transformsByLegAndSource = map.MappingValueTransformRows
                .Where(t => axLegIds.Contains(t.LegId))
                .GroupBy(t => BuildLegFieldKey(t.LegId, t.SourceField), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var enumFieldAggregates = new Dictionary<string, (ODataEnumType EnumType, HashSet<string> Keys, List<string> ParseErrors, string? FixedValue)>(StringComparer.OrdinalIgnoreCase);

            foreach (var fieldMapping in map.MappingFieldRows)
            {
                if (!axLegIds.Contains(fieldMapping.LegId) || fieldMapping.ValueTransforms <= 0)
                {
                    continue;
                }

                var normalizedSource = TestifyPlanner.NormalizeKey(fieldMapping.SourceField);
                if (!fieldNameLookup.TryGetValue(normalizedSource, out var actualFoField))
                {
                    warnings.Add($"Could not resolve FO source field '{fieldMapping.SourceField}' for transform coverage.");
                    continue;
                }

                var foProperty = foEntityDetails.Properties.FirstOrDefault(p => string.Equals(p.Name, actualFoField, StringComparison.OrdinalIgnoreCase));
                if (foProperty is null)
                {
                    continue;
                }

                var enumType = ResolveEnumType(_foEnumLookup, foProperty.Type);
                if (enumType is null)
                {
                    continue;
                }

                var transformLookupKey = BuildLegFieldKey(fieldMapping.LegId, fieldMapping.SourceField);
                if (!transformsByLegAndSource.TryGetValue(transformLookupKey, out var transforms) || transforms.Count == 0)
                {
                    blockingIssues.Add($"Enum field '{actualFoField}' has transform count but no valueMap definition.");
                    continue;
                }

                if (!enumFieldAggregates.TryGetValue(actualFoField, out var aggregate))
                {
                    aggregate = (enumType, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new List<string>(), equalityConstraints.TryGetValue(actualFoField, out var fixedValue) ? fixedValue : null);
                }

                foreach (var transform in transforms)
                {
                    if (!TestifyValueMapParser.TryExtractKeys(transform.ValueMap, out var keys, out var parseError))
                    {
                        aggregate.ParseErrors.Add($"Field '{actualFoField}': {parseError}");
                        continue;
                    }

                    aggregate.Keys.UnionWith(keys);
                }

                enumFieldAggregates[actualFoField] = aggregate;
            }

            foreach (var aggregate in enumFieldAggregates.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            {
                var missingMembers = aggregate.Value.EnumType.Members
                    .Where(member => !aggregate.Value.Keys.Contains(member))
                    .ToList();

                var parseFailed = aggregate.Value.ParseErrors.Count > 0;
                var parseError = parseFailed ? string.Join(" ", aggregate.Value.ParseErrors) : string.Empty;
                var plan = new TestifyEnumFieldPlan(
                    fieldName: aggregate.Key,
                    enumType: aggregate.Value.EnumType.Name,
                    enumMembers: aggregate.Value.EnumType.Members,
                    transformKeys: aggregate.Value.Keys,
                    missingMembers: missingMembers,
                    fixedValue: aggregate.Value.FixedValue,
                    parseFailed: parseFailed,
                    parseError: parseError);

                enumFieldPlans[aggregate.Key] = plan;

                if (parseFailed)
                {
                    blockingIssues.Add(parseError);
                }

                if (missingMembers.Count > 0)
                {
                    coverageGaps.AddRange(missingMembers.Select(member => new TestifyEnumCoverageGap(aggregate.Key, member)));
                    if (configuration.AllowPartialEnumCoverage)
                        warnings.Add($"Enum coverage partial for field '{aggregate.Key}': {string.Join(", ", missingMembers)} not mapped. Running with mapped values only.");
                    else
                        blockingIssues.Add($"Enum coverage missing for field '{aggregate.Key}'.");
                }
            }

            foreach (var enumField in enumFieldPlans.Values)
            {
                var initialValue = !string.IsNullOrWhiteSpace(enumField.FixedValue)
                    ? enumField.FixedValue!
                    : enumField.EnumMembers.FirstOrDefault() ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(initialValue))
                {
                    createValues[enumField.FieldName] = initialValue;
                }
            }

            foreach (var fieldMapping in map.MappingFieldRows.Where(row => axLegIds.Contains(row.LegId)))
            {
                if (string.IsNullOrWhiteSpace(fieldMapping.SourceField) || string.IsNullOrWhiteSpace(fieldMapping.DestinationField))
                {
                    continue;
                }

                var normalizedSource = TestifyPlanner.NormalizeKey(fieldMapping.SourceField);
                if (!fieldNameLookup.TryGetValue(normalizedSource, out var actualFoField))
                {
                    continue;
                }

                var foProperty = foEntityDetails.Properties.FirstOrDefault(p => string.Equals(p.Name, actualFoField, StringComparison.OrdinalIgnoreCase));
                if (foProperty is null)
                {
                    continue;
                }

                var transformLookupKey = BuildLegFieldKey(fieldMapping.LegId, fieldMapping.SourceField);
                var mappedTargetValues = transformsByLegAndSource.TryGetValue(transformLookupKey, out var transforms)
                    ? BuildMappedTargetValues(transforms)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                fieldAssertions.Add(new TestifyFieldAssertionPlan(
                    fieldMapping.LegId,
                    actualFoField,
                    fieldMapping.DestinationField.Trim(),
                    foProperty.Type,
                    ceFieldType: null,
                    hasValueMap: mappedTargetValues.Count > 0,
                    mappedTargetValues));
            }

            var runToken = $"TESTIFY{DateTime.UtcNow:yyyyMMddHHmmss}";
            foreach (var property in foEntityDetails.Properties)
            {
                if (configuration.OmitCreateFields.Contains(property.Name))
                {
                    createValues.Remove(property.Name);
                    continue;
                }

                if (createValues.TryGetValue(property.Name, out var existing) && !string.IsNullOrWhiteSpace(existing))
                {
                    createValues[property.Name] = TestifyPlanner.TrimToMaxLength(property, existing.Trim());
                    continue;
                }

                if (!property.Mandatory)
                {
                    continue;
                }

                var generated = TestifyPlanner.GenerateDefaultValue(property, runToken, enumMembersByType, _ctx.CurrentEnv.DefaultCompany);
                if (string.IsNullOrWhiteSpace(generated))
                {
                    if (string.Equals(property.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase))
                    {
                        blockingIssues.Add("Cannot determine legal entity for 'dataAreaId'. Set the FO Default Company in Profiles, or ensure the map/source filter provides company.");
                    }
                    else
                    {
                        blockingIssues.Add($"Unable to generate mandatory value for '{property.Name}'.");
                    }
                    continue;
                }

                createValues[property.Name] = TestifyPlanner.TrimToMaxLength(property, generated);
            }

            ApplyBestEffortRunTag(foEntityDetails, createValues, runToken);

            foreach (var keyProp in foEntityDetails.Properties.Where(p => p.IsKey))
            {
                if (!createValues.TryGetValue(keyProp.Name, out var keyValue) || string.IsNullOrWhiteSpace(keyValue))
                {
                    if (configuration.OmitCreateFields.Contains(keyProp.Name))
                    {
                        warnings.Add($"Create key '{keyProp.Name}' is configured to omit. Testify expects FO to assign it and return it in create response.");
                    }
                    else
                    {
                        blockingIssues.Add($"Missing key value '{keyProp.Name}' for create/update flow.");
                    }
                }
            }

            var enumMembersByField = enumFieldPlans.ToDictionary(
                p => p.Key,
                p => (IReadOnlyList<string>)p.Value.EnumMembers,
                StringComparer.OrdinalIgnoreCase);
            var fixedValues = enumFieldPlans
                .Where(p => !string.IsNullOrWhiteSpace(p.Value.FixedValue))
                .ToDictionary(p => p.Key, p => p.Value.FixedValue!, StringComparer.OrdinalIgnoreCase);

            foreach (var issue in TestifyPlanner.ValidateFixedEnumCoverage(enumMembersByField, fixedValues))
            {
                blockingIssues.Add(issue);
            }

            patchSteps = TestifyPlanner.BuildMinimalPatchSteps(enumMembersByField, fixedValues).ToArray();

            if (!TestifyRunner.TryBuildPayload(foEntityDetails, createValues, enumMembersByType, enforceMandatory: true, out createPayloadJson, out var payloadIssues))
            {
                foreach (var issue in payloadIssues)
                {
                    blockingIssues.Add($"Payload: {issue}");
                }
            }
        }

        return new TestifyMapPlan(
            mapId: map.Id,
            mapDisplayName: map.DisplayName,
            foEntity: foEntity,
            foEntityDetails: foEntityDetails,
            configuration: configuration,
            foFilter: foFilter,
            ceLegs: ceLegs,
            createValues: createValues,
            createPayloadJson: createPayloadJson,
            enumFields: enumFieldPlans,
            fieldAssertions: fieldAssertions,
            patchSteps: patchSteps,
            warnings: warnings,
            coverageGaps: coverageGaps,
            blockingIssues: blockingIssues);
    }

    private static string GetBlockedStatus(TestifyMapPlan plan)
    {
        if (plan.FoEntityDetails is null)
        {
            return "Blocked: missing entity";
        }

        if (plan.CoverageGaps.Count > 0 && !plan.Configuration.AllowPartialEnumCoverage)
        {
            return "Blocked: incomplete coverage";
        }

        return "Blocked";
    }

    private static string FormatBlockingIssue(TestifyMapPlan plan)
    {
        var issues = new List<string>();
        if (plan.BlockingIssues.Count > 0)
        {
            issues.AddRange(plan.BlockingIssues);
        }

        if (plan.CoverageGaps.Count > 0)
        {
            issues.AddRange(FormatCoverageGapIssues(plan));
        }

        return issues.Count == 0 ? "Map blocked during preflight." : string.Join(" ", issues);
    }

    private static IEnumerable<string> FormatCoverageGapIssues(TestifyMapPlan plan)
    {
        if (plan.EnumFields.Count > 0)
        {
            return plan.EnumFields.Values
                .Where(field => field.HasCoverageGap)
                .OrderBy(field => field.FieldName, StringComparer.OrdinalIgnoreCase)
                .Select(field => field.CoverageGapDetail);
        }

        return plan.CoverageGapsByField.Select(gap =>
            $"Unmapped enum members for field '{gap.FieldName}': {string.Join(", ", gap.EnumValues.Select(value => $"'{value}'"))}.");
    }

    internal async Task<Dictionary<string, TestifyCorrelatedCeRow>> WaitForCorrelatedCeRowsAsync(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> foValues,
        IReadOnlyDictionary<string, TestifyCorrelatedCeRow>? correlatedRows,
        CancellationToken cancellationToken,
        string phase)
    {
        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var timeoutMinutes = plan.Configuration.CePollTimeoutMinutes > 0 ? plan.Configuration.CePollTimeoutMinutes : 5;
        var deadline = TestifyUtcNow().AddMinutes(timeoutMinutes);

        while (TestifyUtcNow() <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolved = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase);
            string? pendingReason = null;

            foreach (var leg in plan.CeLegs)
            {
                var row = await TryGetCorrelatedCeRowAsync(plan, leg, foValues, correlatedRows, dataverseHttp, apiBase, cancellationToken);
                if (row is null)
                {
                    pendingReason = $"Waiting for correlated CE row on leg '{leg.LegId}'.";
                    break;
                }

                resolved[leg.LegId] = row;
            }

            if (pendingReason is null)
            {
                return resolved;
            }

            await TestifyDelayAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }

        throw new InvalidOperationException($"CE verification timed out ({phase}) after {timeoutMinutes} minute(s). Increase CePollTimeoutMinutes in Testify configuration if sync is slow.");
    }

    private async Task<TestifyCorrelatedCeRow?> TryGetCorrelatedCeRowAsync(
        TestifyMapPlan plan,
        TestifyLegPlan leg,
        IReadOnlyDictionary<string, string> foValues,
        IReadOnlyDictionary<string, TestifyCorrelatedCeRow>? correlatedRows,
        HttpClient dataverseHttp,
        string apiBase,
        CancellationToken cancellationToken)
    {
        if (!foValues.TryGetValue(leg.FoCorrelationField, out var correlationValue) || string.IsNullOrWhiteSpace(correlationValue))
        {
            throw new InvalidOperationException($"Missing FO correlation value '{leg.FoCorrelationField}' for leg '{leg.LegId}'.");
        }

        var deterministicKey = BuildDeterministicCorrelationKey(leg, correlationValue);
        var filter = BuildCorrelationFilter(leg.CeCorrelationFilter, leg.CeCorrelationField, correlationValue);
        var selectColumns = BuildCorrelationSelectColumns(leg);
        var rows = await QueryDataverseRowsAsync(dataverseHttp, apiBase, leg.CeEntity, filter, selectColumns, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        if (rows.Count > 1)
        {
            throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': expected one correlated row for {leg.CeCorrelationField}='{correlationValue}' but found {rows.Count}.");
        }

        var row = rows[0];
        var rowCorrelationValue = GetJsonStringValue(row, leg.CeCorrelationField);
        if (!string.Equals(rowCorrelationValue, correlationValue, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': correlated row did not match FO value '{correlationValue}'.");
        }

        var rowId = ResolveDataverseRowId(leg, row);
        if (string.IsNullOrWhiteSpace(rowId))
        {
            throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': correlated row did not expose a stable CE id.");
        }

        if (correlatedRows is not null && correlatedRows.TryGetValue(leg.LegId, out var existing))
        {
            if (!string.Equals(existing.DeterministicKey, deterministicKey, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': expected deterministic key '{existing.DeterministicKey}' but resolved '{deterministicKey}'.");
            }

            if (!string.Equals(existing.RowId, rowId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': expected CE row '{existing.RowId}' but found '{rowId}'.");
            }

            if (!string.Equals(existing.CorrelationValue, correlationValue, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"CE verification failed for leg '{leg.LegId}': expected correlation value '{existing.CorrelationValue}' but FO field '{leg.FoCorrelationField}' resolved to '{correlationValue}'.");
            }
        }

        return new TestifyCorrelatedCeRow(
            leg.LegId,
            leg.CeEntity,
            rowId,
            deterministicKey,
            correlationValue,
            leg.FoCorrelationField,
            leg.CeCorrelationField);
    }

    private static string BuildDeterministicCorrelationKey(TestifyLegPlan leg, string correlationValue)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{leg.LegId}|{leg.CeEntity}|{leg.FoCorrelationField}|{leg.CeCorrelationField}|{correlationValue}");
    }

    private static string ResolveDataverseRowId(TestifyLegPlan leg, JsonElement row)
    {
        if (!string.IsNullOrWhiteSpace(leg.CorrelatedRowIdField))
        {
            var explicitId = GetJsonStringValue(row, leg.CorrelatedRowIdField!);
            if (!string.IsNullOrWhiteSpace(explicitId))
            {
                return explicitId;
            }

            return string.Empty;
        }

        var conventionalId = GetJsonStringValue(row, $"{leg.CeEntity}id");
        if (!string.IsNullOrWhiteSpace(conventionalId))
        {
            return conventionalId;
        }

        return string.Empty;
    }

    private static string BuildCorrelationFilter(string existingFilter, string ceCorrelationField, string correlationValue)
    {
        var escaped = correlationValue.Replace("'", "''", StringComparison.Ordinal);
        var correlationClause = $"{ceCorrelationField} eq '{escaped}'";
        return string.IsNullOrWhiteSpace(existingFilter)
            ? correlationClause
            : $"({existingFilter}) and ({correlationClause})";
    }

    private static IReadOnlyList<string> BuildCorrelationSelectColumns(TestifyLegPlan leg)
    {
        var columns = new List<string>();

        AddUniqueCorrelationColumn(columns, leg.CeCorrelationField);
        AddUniqueCorrelationColumn(columns, leg.CorrelatedRowIdField);
        AddUniqueCorrelationColumn(columns, $"{leg.CeEntity}id");

        return columns;
    }

    private static void AddUniqueCorrelationColumn(List<string> columns, string? column)
    {
        if (string.IsNullOrWhiteSpace(column))
        {
            return;
        }

        if (columns.Contains(column, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        columns.Add(column);
    }

    private static async Task<List<JsonElement>> QueryDataverseRowsAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        IReadOnlyList<string> selectColumns,
        CancellationToken cancellationToken)
    {
        var url = BuildDataversePagedCountStartUrl(apiBase, entitySetName, oDataFilter, selectColumns);
        var rows = new List<JsonElement>();

        while (!string.IsNullOrWhiteSpace(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.TryAddWithoutValidation("Prefer", "odata.maxpagesize=2");

            using var response = await dataverseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Dataverse correlated row query failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Dataverse correlated row response did not contain a 'value' array.");
            }

            rows.AddRange(valueArray.EnumerateArray().Select(item => item.Clone()));
            url = root.TryGetProperty("@odata.nextLink", out var nextLinkElement) && nextLinkElement.ValueKind == JsonValueKind.String
                ? nextLinkElement.GetString()
                : null;
        }

        return rows;
    }

    private static string GetJsonStringValue(JsonElement row, string propertyName)
    {
        foreach (var property in row.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return JsonElementToString(property.Value);
            }
        }

        return string.Empty;
    }

    private static string BuildDataversePagedCountStartUrl(
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        IReadOnlyList<string> selectColumns)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            parts.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        if (selectColumns.Count > 0)
        {
            parts.Add($"$select={Uri.EscapeDataString(string.Join(",", selectColumns))}");
        }

        var query = parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
        return $"{apiBase}/{entitySetName}{query}";
    }

    private async Task<ODataWriteResponse> SendCreateWithRetryAsync(
        TestifyMapPlan plan,
        Dictionary<string, string> runtimeCreateValues,
        TestifyMapConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var createUrl = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, plan.FoEntity) + "?cross-company=true";
        var configurationChanged = false;
        var currentResponse = await _write!.ODataWrite.SendAsync(
            new ODataWriteRequest(HttpMethod.Post, createUrl, plan.CreatePayloadJson),
            cancellationToken);

        if (IsSuccessfulStatusCode(currentResponse.StatusCode))
        {
            LearnFromSuccessfulCreate(plan, configuration, runtimeCreateValues, null);
            await _testifyConfigStore.SaveAsync(configuration, cancellationToken);
            return currentResponse;
        }

        if (TryBuildRetryCreatePayload(plan, runtimeCreateValues, currentResponse, out var retryPayload, out var removedFields, out var retryReason))
        {
            foreach (var field in removedFields)
            {
                runtimeCreateValues.Remove(field);
            }
            configurationChanged |= LearnOmittedFields(configuration, removedFields);

            AddTestifyLog(
                plan.MapDisplayName,
                "Create Retry",
                "Started",
                $"Retrying FO create without field(s): {string.Join(", ", removedFields)}. Reason: {retryReason}");

            currentResponse = await _write.ODataWrite.SendAsync(
                new ODataWriteRequest(HttpMethod.Post, createUrl, retryPayload),
                cancellationToken);

            if (IsSuccessfulStatusCode(currentResponse.StatusCode))
            {
                AddTestifyLog(plan.MapDisplayName, "Create Retry", "Succeeded", $"FO create retry returned HTTP {currentResponse.StatusCode}.");
                LearnFromSuccessfulCreate(plan, configuration, runtimeCreateValues, ResolveEffectiveCompany(runtimeCreateValues));
                await _testifyConfigStore.SaveAsync(configuration, cancellationToken);
                return currentResponse;
            }
        }

        if (TryBuildMandatoryFieldRetryPayload(plan, runtimeCreateValues, currentResponse, out var mandatoryRetryPayload, out var addedFields, out var mandatoryRetryReason))
        {
            foreach (var added in addedFields)
            {
                runtimeCreateValues[added.Key] = added.Value;
            }
            configurationChanged |= LearnPreferredValues(configuration, addedFields, ResolveEffectiveCompany(runtimeCreateValues), companyScoped: false);

            AddTestifyLog(
                plan.MapDisplayName,
                "Create Retry",
                "Started",
                $"Retrying FO create with inferred mandatory field(s): {string.Join(", ", addedFields.Keys)}. Reason: {mandatoryRetryReason}");

            currentResponse = await _write.ODataWrite.SendAsync(
                new ODataWriteRequest(HttpMethod.Post, createUrl, mandatoryRetryPayload),
                cancellationToken);

            if (IsSuccessfulStatusCode(currentResponse.StatusCode))
            {
                AddTestifyLog(plan.MapDisplayName, "Create Retry", "Succeeded", $"FO create mandatory retry returned HTTP {currentResponse.StatusCode}.");
                LearnFromSuccessfulCreate(plan, configuration, runtimeCreateValues, ResolveEffectiveCompany(runtimeCreateValues));
                await _testifyConfigStore.SaveAsync(configuration, cancellationToken);
                return currentResponse;
            }
        }

        var lookupRetry = await TryBuildLookupRetryCreatePayloadAsync(plan, runtimeCreateValues, currentResponse, cancellationToken);
        if (lookupRetry.CanRetry)
        {
            foreach (var added in lookupRetry.AddedFields)
            {
                runtimeCreateValues[added.Key] = added.Value;
            }
            configurationChanged |= LearnPreferredValues(configuration, lookupRetry.AddedFields, ResolveEffectiveCompany(runtimeCreateValues), companyScoped: true);

            AddTestifyLog(
                plan.MapDisplayName,
                "Create Retry",
                "Started",
                $"Retrying FO create with resolved lookup field(s): {string.Join(", ", lookupRetry.AddedFields.Select(p => $"{p.Key}={p.Value}"))}. Reason: {lookupRetry.Reason}");

            currentResponse = await _write.ODataWrite.SendAsync(
                new ODataWriteRequest(HttpMethod.Post, createUrl, lookupRetry.PayloadJson),
                cancellationToken);

            if (IsSuccessfulStatusCode(currentResponse.StatusCode))
            {
                AddTestifyLog(plan.MapDisplayName, "Create Retry", "Succeeded", $"FO create lookup retry returned HTTP {currentResponse.StatusCode}.");
                LearnFromSuccessfulCreate(plan, configuration, runtimeCreateValues, ResolveEffectiveCompany(runtimeCreateValues));
                await _testifyConfigStore.SaveAsync(configuration, cancellationToken);
                return currentResponse;
            }
        }

        if (configurationChanged)
        {
            await _testifyConfigStore.SaveAsync(configuration, cancellationToken);
        }

        return currentResponse;
    }

    private bool TryBuildRetryCreatePayload(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        ODataWriteResponse failedCreateResponse,
        out string retryPayload,
        out List<string> removedFields,
        out string reason)
    {
        retryPayload = string.Empty;
        removedFields = new List<string>();
        reason = string.Empty;
        var fieldsToRemove = new List<string>();

        if (failedCreateResponse.StatusCode != 400 || plan.FoEntityDetails is null)
        {
            return false;
        }

        var body = failedCreateResponse.Body ?? string.Empty;
        if (!body.Contains("does not match format", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var candidateFields = ExtractCreateRetryFieldCandidates(body);
        if (candidateFields.Count == 0)
        {
            return false;
        }

        var keyLookup = plan.FoEntityDetails.Properties
            .GroupBy(p => TestifyPlanner.NormalizeKey(p.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidateFields)
        {
            var normalized = TestifyPlanner.NormalizeKey(candidate);
            if (!keyLookup.TryGetValue(normalized, out var property))
            {
                continue;
            }

            if (!string.Equals(property.Type, "Edm.String", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!runtimeCreateValues.TryGetValue(property.Name, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            // Retry only for synthetic Testify values so we do not silently discard user/map-provided values.
            if (!value.StartsWith("TESTIFY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            fieldsToRemove.Add(property.Name);
        }

        if (fieldsToRemove.Count == 0)
        {
            return false;
        }

        var reducedValues = runtimeCreateValues
            .Where(p => !fieldsToRemove.Contains(p.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        var enumMembersByType = TestifyRunner.BuildEnumMembersByTypeLookup(_foEnumLookup);
        if (!TestifyRunner.TryBuildPayload(plan.FoEntityDetails, reducedValues, enumMembersByType, enforceMandatory: false, out retryPayload, out var issues))
        {
            reason = issues.Count == 0 ? "Could not build retry payload." : string.Join(" ", issues);
            return false;
        }

        removedFields = fieldsToRemove;
        reason = "Field format validation failed for synthetic value(s); retrying to let FO number sequence/defaulting populate values.";
        return true;
    }

    private bool TryBuildMandatoryFieldRetryPayload(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        ODataWriteResponse failedCreateResponse,
        out string retryPayload,
        out Dictionary<string, string> addedFields,
        out string reason)
    {
        retryPayload = string.Empty;
        addedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        reason = string.Empty;

        if (failedCreateResponse.StatusCode != 400 || plan.FoEntityDetails is null)
        {
            return false;
        }

        var labels = TestifyPlanner.ExtractMandatoryFieldLabels(failedCreateResponse.Body ?? string.Empty);
        if (labels.Count == 0)
        {
            return false;
        }

        var enumMembersByType = TestifyRunner.BuildEnumMembersByTypeLookup(_foEnumLookup);
        var runToken = $"TESTIFY{DateTime.UtcNow:yyyyMMddHHmmss}";

        foreach (var label in labels)
        {
            var resolvedField = TestifyPlanner.ResolveFieldByLabel(label, plan.FoEntityDetails.Properties, runtimeCreateValues);
            if (string.IsNullOrWhiteSpace(resolvedField))
            {
                continue;
            }

            if (runtimeCreateValues.TryGetValue(resolvedField, out var existingValue) && !string.IsNullOrWhiteSpace(existingValue))
            {
                continue;
            }

            var property = plan.FoEntityDetails.Properties.FirstOrDefault(p => string.Equals(p.Name, resolvedField, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                continue;
            }

            var generated = TestifyPlanner.GenerateDefaultValue(property, runToken, enumMembersByType, _ctx.CurrentEnv.DefaultCompany);
            if (string.IsNullOrWhiteSpace(generated) && string.Equals(property.Type, "Edm.String", StringComparison.OrdinalIgnoreCase))
            {
                generated = $"{runToken}_{resolvedField}";
            }

            if (string.IsNullOrWhiteSpace(generated))
            {
                continue;
            }

            addedFields[resolvedField] = TestifyPlanner.TrimToMaxLength(property, generated);
        }

        if (addedFields.Count == 0)
        {
            reason = $"Mandatory labels could not be mapped to writable FO fields: {string.Join(", ", labels)}.";
            return false;
        }

        var merged = runtimeCreateValues
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var added in addedFields)
        {
            merged[added.Key] = added.Value;
        }

        if (!TestifyRunner.TryBuildPayload(plan.FoEntityDetails, merged, enumMembersByType, enforceMandatory: false, out retryPayload, out var issues))
        {
            reason = issues.Count == 0 ? "Could not build mandatory retry payload." : string.Join(" ", issues);
            return false;
        }

        reason = $"FO reported missing mandatory field(s): {string.Join(", ", labels)}.";
        return true;
    }

    private async Task<(bool CanRetry, string PayloadJson, Dictionary<string, string> AddedFields, string Reason)> TryBuildLookupRetryCreatePayloadAsync(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        ODataWriteResponse failedCreateResponse,
        CancellationToken cancellationToken)
    {
        var retryPayload = string.Empty;
        var addedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reason = string.Empty;

        if (failedCreateResponse.StatusCode != 400 || plan.FoEntityDetails is null)
        {
            return (false, retryPayload, addedFields, reason);
        }

        var issues = TestifyPlanner.ExtractLookupValidationIssues(failedCreateResponse.Body ?? string.Empty);
        if (issues.Count == 0)
        {
            return (false, retryPayload, addedFields, reason);
        }

        foreach (var issue in issues)
        {
            var resolvedField = TestifyPlanner.ResolveFieldByLabel(issue.FieldLabel, plan.FoEntityDetails.Properties, runtimeCreateValues);
            if (string.IsNullOrWhiteSpace(resolvedField))
            {
                continue;
            }

            var lookupEntity = await ResolveLookupEntityFromNavigationAsync(plan, issue, cancellationToken);
            if (string.IsNullOrWhiteSpace(lookupEntity))
            {
                continue;
            }

            var lookupValue = await ResolveLookupValueAsync(lookupEntity, runtimeCreateValues, cancellationToken);
            if (string.IsNullOrWhiteSpace(lookupValue))
            {
                continue;
            }

            addedFields[resolvedField] = lookupValue;
        }

        if (addedFields.Count == 0)
        {
            return (false, retryPayload, addedFields, reason);
        }

        var merged = runtimeCreateValues.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in addedFields)
        {
            merged[pair.Key] = pair.Value;
        }

        var enumMembersByType = TestifyRunner.BuildEnumMembersByTypeLookup(_foEnumLookup);
        if (!TestifyRunner.TryBuildPayload(plan.FoEntityDetails, merged, enumMembersByType, enforceMandatory: false, out retryPayload, out var payloadIssues))
        {
            reason = payloadIssues.Count == 0 ? "Could not build lookup retry payload." : string.Join(" ", payloadIssues);
            return (false, retryPayload, addedFields, reason);
        }

        reason = "FO lookup validation failed; retried with top(1) lookup key values from related FO entity.";
        return (true, retryPayload, addedFields, reason);
    }

    private async Task<string?> ResolveLookupEntityFromNavigationAsync(
        TestifyMapPlan plan,
        TestifyPlanner.LookupValidationIssue issue,
        CancellationToken cancellationToken)
    {
        if (plan.FoEntityDetails is null)
        {
            return null;
        }

        await EnsureFoEntityLookupAsync(cancellationToken);

        var fieldTokens = TokenizeName(issue.FieldLabel).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tableTokens = TokenizeName(issue.RelatedTable).ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? bestEntity = null;
        var bestScore = int.MinValue;

        foreach (var nav in plan.FoEntityDetails.Navigations)
        {
            var navTypeShort = ExtractNavTypeShortName(nav.Type);
            var navTokens = TokenizeName($"{nav.Name} {navTypeShort}").ToHashSet(StringComparer.OrdinalIgnoreCase);
            var score = navTokens.Intersect(tableTokens, StringComparer.OrdinalIgnoreCase).Count() * 20 +
                        navTokens.Intersect(fieldTokens, StringComparer.OrdinalIgnoreCase).Count() * 10;

            if (score <= 0)
            {
                continue;
            }

            var resolved = ResolveFoEntityName(nav.Name, navTypeShort, issue.RelatedTable, issue.FieldLabel);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                continue;
            }

            if (string.Equals(resolved, plan.FoEntity, StringComparison.OrdinalIgnoreCase))
            {
                score -= 100;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestEntity = resolved;
            }
        }

        if (!string.IsNullOrWhiteSpace(bestEntity))
        {
            return bestEntity;
        }

        return ResolveFoEntityName(issue.RelatedTable, issue.FieldLabel);
    }

    private async Task<string?> ResolveLookupValueAsync(
        string lookupEntity,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        CancellationToken cancellationToken)
    {
        var details = await GetFoEntityDetailsCachedAsync(lookupEntity, cancellationToken);
        if (details is null)
        {
            return null;
        }

        var keyProperties = details.Properties.Where(p => p.IsKey).ToList();
        if (keyProperties.Count == 0)
        {
            return null;
        }

        var keyProperty = keyProperties.FirstOrDefault(p => !string.Equals(p.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase))
                          ?? keyProperties.First();

        var hasDataAreaId = details.Properties.Any(p => string.Equals(p.Name, "dataAreaId", StringComparison.OrdinalIgnoreCase));
        runtimeCreateValues.TryGetValue("dataAreaId", out var company);
        company ??= string.Empty;

        var select = new List<string> { keyProperty.Name };
        if (hasDataAreaId && !select.Contains("dataAreaId", StringComparer.OrdinalIgnoreCase))
        {
            select.Add("dataAreaId");
        }

        var filter = hasDataAreaId && !string.IsNullOrWhiteSpace(company)
            ? $"dataAreaId eq '{EscapeSingleQuoted(company)}'"
            : null;

        var value = await GetFirstValueFromEntityAsync(lookupEntity, keyProperty.Name, select, filter, cancellationToken);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            // Fallback for global/shared lookup tables.
            return await GetFirstValueFromEntityAsync(lookupEntity, keyProperty.Name, select, null, cancellationToken);
        }

        return null;
    }

    private async Task<string?> GetFirstValueFromEntityAsync(
        string entityName,
        string valueField,
        IReadOnlyList<string> select,
        string? filter,
        CancellationToken cancellationToken)
    {
        var spec = new QuerySpec(
            Entity: entityName,
            CrossCompany: true,
            Select: select,
            Top: 1,
            Filter: string.IsNullOrWhiteSpace(filter) ? null : filter);

        var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        await foreach (var page in _ctx.OData.StreamAsync(request, cancellationToken))
        {
            var row = page.Rows.FirstOrDefault();
            if (row is null)
            {
                return null;
            }

            if (TryGetRowValueIgnoreCase(row, valueField, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return null;
        }

        return null;
    }

    private static bool TryGetRowValueIgnoreCase(IReadOnlyDictionary<string, object?> row, string fieldName, out string value)
    {
        foreach (var pair in row)
        {
            if (!string.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value is null)
            {
                value = string.Empty;
                return true;
            }

            value = pair.Value switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                _ => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? pair.Value.ToString() ?? string.Empty
            };

            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string ExtractNavTypeShortName(string navType)
    {
        if (string.IsNullOrWhiteSpace(navType))
        {
            return string.Empty;
        }

        var type = navType.Trim();
        if (type.StartsWith("Collection(", StringComparison.OrdinalIgnoreCase) && type.EndsWith(")", StringComparison.Ordinal))
        {
            type = type.Substring("Collection(".Length, type.Length - "Collection(".Length - 1);
        }

        var shortName = type.Split('.').LastOrDefault();
        return shortName ?? type;
    }

    private static List<string> ExtractCreateRetryFieldCandidates(string body)
    {
        var fields = new List<string>();
        if (string.IsNullOrWhiteSpace(body))
        {
            return fields;
        }

        var match = Regex.Match(
            body,
            @"fields:\s*(?<fields>.+?)(?:\.\s*Infolog:|\.)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return fields;
        }

        var fieldText = match.Groups["fields"].Value;
        foreach (var token in fieldText.Split(new[] { ",", " and " }, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                fields.Add(trimmed);
            }
        }

        return fields;
    }

    private static void MergeKeyValuesFromCreateResponse(ODataEntity entity, string? responseBody, Dictionary<string, string> values)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var keyProp in entity.Properties.Where(p => p.IsKey))
            {
                if (!TryGetPropertyIgnoreCase(root, keyProp.Name, out var keyValueElement))
                {
                    continue;
                }

                var keyValue = JsonElementToString(keyValueElement);
                if (!string.IsNullOrWhiteSpace(keyValue))
                {
                    values[keyProp.Name] = keyValue;
                }
            }
        }
        catch (JsonException)
        {
            // Ignore response parsing failures; downstream key-url generation will report missing keys if needed.
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string propertyName, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => element.ToString()
        };
    }

    private static IReadOnlyDictionary<string, string> BuildMappedTargetValues(IReadOnlyList<MappingValueTransformRow> transforms)
    {
        var mapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transform in transforms)
        {
            if (string.IsNullOrWhiteSpace(transform.ValueMap))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(transform.ValueMap);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    mapped[property.Name] = JsonElementToString(property.Value);
                }
            }
            catch (JsonException)
            {
            }
        }

        return mapped;
    }

    private async Task<IReadOnlyList<TestifyFieldAssertionResult>> VerifyCeFieldAssertionsAsync(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> foValues,
        IReadOnlyDictionary<string, TestifyCorrelatedCeRow> correlatedRows,
        CancellationToken cancellationToken,
        string phase)
    {
        if (plan.FieldAssertions.Count == 0)
        {
            return Array.Empty<TestifyFieldAssertionResult>();
        }

        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var results = new List<TestifyFieldAssertionResult>();

        foreach (var legGroup in plan.FieldAssertions.GroupBy(assertion => assertion.LegId, StringComparer.OrdinalIgnoreCase))
        {
            if (!correlatedRows.TryGetValue(legGroup.Key, out var correlatedRow))
            {
                throw new InvalidOperationException($"CE verification failed for leg '{legGroup.Key}': no correlated row was available for field assertions.");
            }

            var row = await ReadDataverseRowAsync(dataverseHttp, apiBase, correlatedRow.EntityName, correlatedRow.RowId, legGroup.Select(assertion => assertion.CeField).ToArray(), cancellationToken);

            foreach (var assertion in legGroup)
            {
                var expectedValue = ResolveExpectedCeValue(assertion, foValues);
                var actualValue = GetJsonStringValue(row, assertion.CeField);
                var passed = ValuesMatch(assertion, expectedValue, actualValue);
                var detail = $"{phase}: {assertion.FoField}->{assertion.CeField} {(passed ? "PASS" : "FAIL")} expected='{expectedValue}' actual='{actualValue}'";
                results.Add(new TestifyFieldAssertionResult(assertion.LegId, assertion.FoField, assertion.CeField, phase, passed, expectedValue, actualValue, detail));

                if (!passed)
                {
                    throw new InvalidOperationException($"CE verification failed for leg '{assertion.LegId}' field '{assertion.CeField}' {phase}: expected '{expectedValue}' but found '{actualValue}'.");
                }
            }
        }

        return results;
    }

    private static string ResolveExpectedCeValue(TestifyFieldAssertionPlan assertion, IReadOnlyDictionary<string, string> foValues)
    {
        foValues.TryGetValue(assertion.FoField, out var foValue);
        foValue ??= string.Empty;

        if (assertion.HasValueMap && assertion.MappedTargetValues.TryGetValue(foValue, out var mapped))
        {
            return mapped ?? string.Empty;
        }

        return foValue;
    }

    private static bool ValuesMatch(TestifyFieldAssertionPlan assertion, string expectedValue, string actualValue)
    {
        if (string.Equals(assertion.FoType, "Edm.Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeBoolean(expectedValue), NormalizeBoolean(actualValue), StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(assertion.FoType, "Edm.Date", StringComparison.OrdinalIgnoreCase) || string.Equals(assertion.FoType, "Edm.DateTimeOffset", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeDateLike(expectedValue), NormalizeDateLike(actualValue), StringComparison.OrdinalIgnoreCase);
        }

        if (assertion.FoType.StartsWith("Edm.Int", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assertion.FoType, "Edm.Decimal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assertion.FoType, "Edm.Double", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(assertion.FoType, "Edm.Single", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeNumber(expectedValue), NormalizeNumber(actualValue), StringComparison.OrdinalIgnoreCase);
        }

        if (string.IsNullOrWhiteSpace(expectedValue) && string.IsNullOrWhiteSpace(actualValue))
        {
            return true;
        }

        return string.Equals(expectedValue?.Trim(), actualValue?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBoolean(string value)
    {
        return value.Trim().Equals("1", StringComparison.OrdinalIgnoreCase) || value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
            ? "true"
            : "false";
    }

    private static string NormalizeNumber(string value)
    {
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : value.Trim();
    }

    private static string NormalizeDateLike(string value)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dto))
        {
            return dto.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return value.Trim();
    }

    private static async Task<JsonElement> ReadDataverseRowAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string rowId,
        IReadOnlyList<string> selectColumns,
        CancellationToken cancellationToken)
    {
        var query = selectColumns.Count == 0
            ? string.Empty
            : $"?$select={Uri.EscapeDataString(string.Join(",", selectColumns.Distinct(StringComparer.OrdinalIgnoreCase)))}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/{entitySetName}({rowId}){query}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await dataverseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"Dataverse correlated row read failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }

    private void ApplyLearnedConfigToCreateValues(
        ODataEntity entity,
        TestifyMapConfiguration configuration,
        Dictionary<string, string> createValues,
        List<string> warnings)
    {
        if (configuration.OmitCreateFields.Count > 0)
        {
            var removed = 0;
            foreach (var field in configuration.OmitCreateFields)
            {
                if (createValues.Remove(field))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                warnings.Add($"Applied Testify config: omitted {removed} create field(s) learned from previous runs.");
            }
        }

        var propertyNames = entity.Properties
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appliedGlobal = 0;
        foreach (var pair in configuration.PreferredCreateValues)
        {
            if (!propertyNames.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            if (!createValues.TryGetValue(pair.Key, out var existing) || string.IsNullOrWhiteSpace(existing))
            {
                createValues[pair.Key] = pair.Value;
                appliedGlobal++;
            }
        }

        var company = ResolveEffectiveCompany(createValues);
        var appliedCompany = 0;
        if (!string.IsNullOrWhiteSpace(company) &&
            configuration.PreferredCreateValuesByCompany.TryGetValue(company, out var companyValues))
        {
            foreach (var pair in companyValues)
            {
                if (!propertyNames.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                if (!createValues.TryGetValue(pair.Key, out var existing) || string.IsNullOrWhiteSpace(existing))
                {
                    createValues[pair.Key] = pair.Value;
                    appliedCompany++;
                }
            }
        }

        if (appliedGlobal > 0 || appliedCompany > 0)
        {
            warnings.Add($"Applied Testify config: reused {appliedGlobal} global and {appliedCompany} company-specific learned value(s).");
        }
    }

    private bool LearnOmittedFields(TestifyMapConfiguration configuration, IEnumerable<string> fields)
    {
        var changed = false;
        foreach (var field in fields.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            if (configuration.OmitCreateFields.Add(field))
            {
                changed = true;
            }

            if (configuration.PreferredCreateValues.Remove(field))
            {
                changed = true;
            }

            foreach (var companyValues in configuration.PreferredCreateValuesByCompany.Values)
            {
                if (companyValues.Remove(field))
                {
                    changed = true;
                }
            }
        }

        return changed;
    }

    private bool LearnPreferredValues(
        TestifyMapConfiguration configuration,
        IReadOnlyDictionary<string, string> values,
        string? company,
        bool companyScoped)
    {
        var changed = false;
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value) || IsSyntheticValue(pair.Value))
            {
                continue;
            }

            if (configuration.OmitCreateFields.Remove(pair.Key))
            {
                changed = true;
            }

            if (companyScoped && !string.IsNullOrWhiteSpace(company))
            {
                if (!configuration.PreferredCreateValuesByCompany.TryGetValue(company, out var companyValues))
                {
                    companyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    configuration.PreferredCreateValuesByCompany[company] = companyValues;
                    changed = true;
                }

                if (!companyValues.TryGetValue(pair.Key, out var existing) || !string.Equals(existing, pair.Value, StringComparison.Ordinal))
                {
                    companyValues[pair.Key] = pair.Value;
                    changed = true;
                }
            }
            else
            {
                if (!configuration.PreferredCreateValues.TryGetValue(pair.Key, out var existing) || !string.Equals(existing, pair.Value, StringComparison.Ordinal))
                {
                    configuration.PreferredCreateValues[pair.Key] = pair.Value;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private void LearnFromSuccessfulCreate(
        TestifyMapPlan plan,
        TestifyMapConfiguration configuration,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        string? company)
    {
        if (plan.FoEntityDetails is null)
        {
            return;
        }

        var nonKeyFields = plan.FoEntityDetails.Properties
            .Where(p => !p.IsKey)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stableValues = runtimeCreateValues
            .Where(p => nonKeyFields.Contains(p.Key))
            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);

        LearnPreferredValues(configuration, stableValues, company, companyScoped: !string.IsNullOrWhiteSpace(company));
    }

    private string? ResolveEffectiveCompany(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("dataAreaId", out var company) && !string.IsNullOrWhiteSpace(company))
        {
            return company.Trim();
        }

        return string.IsNullOrWhiteSpace(_ctx.CurrentEnv.DefaultCompany) ? null : _ctx.CurrentEnv.DefaultCompany!.Trim();
    }

    private static bool IsSyntheticValue(string value)
    {
        return value.StartsWith("TESTIFY", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryBuildPatchPayload(TestifyMapPlan plan, TestifyPatchStep step, out string patchJson, out string error)
    {
        patchJson = string.Empty;
        error = string.Empty;

        if (plan.FoEntityDetails is null)
        {
            error = "Missing FO entity metadata for PATCH payload generation.";
            return false;
        }

        var enumMembersByType = TestifyRunner.BuildEnumMembersByTypeLookup(_foEnumLookup);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in step.EnumValues)
        {
            var property = plan.FoEntityDetails.Properties.FirstOrDefault(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                continue;
            }

            values[property.Name] = TestifyPlanner.TrimToMaxLength(property, pair.Value);
        }

        if (!TestifyRunner.TryBuildPayload(plan.FoEntityDetails, values, enumMembersByType, enforceMandatory: false, out patchJson, out var issues))
        {
            error = issues.Count == 0
                ? "Could not build PATCH payload."
                : string.Join(" ", issues);
            return false;
        }

        return true;
    }

    private static TestifyLegPlan? TryBuildCeCorrelationPlan(
        MappingLegRow leg,
        IReadOnlyList<MappingFieldRow> fieldMappings,
        IReadOnlyDictionary<string, string> foFieldLookup,
        IReadOnlyDictionary<string, string> equalityConstraints)
    {
        var legMappings = fieldMappings
            .Where(row => string.Equals(row.LegId, leg.LegId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var preferredFoFields = new[] { "FOTBTestifyRunId", "TestifyRunId", "Description", "Name" };

        foreach (var preferredField in preferredFoFields)
        {
            if (!equalityConstraints.ContainsKey(preferredField))
            {
                continue;
            }

            var preferredMapping = legMappings.FirstOrDefault(row =>
                string.Equals(TestifyPlanner.NormalizeKey(row.SourceField), TestifyPlanner.NormalizeKey(preferredField), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.DestinationField));

            if (preferredMapping is null)
            {
                continue;
            }

            return new TestifyLegPlan(
                leg.LegId,
                leg.DestinationSchema,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                preferredField,
                preferredMapping.DestinationField.Trim(),
                correlatedRowIdField: null);
        }

        foreach (var fieldMapping in legMappings)
        {
            var normalizedSource = TestifyPlanner.NormalizeKey(fieldMapping.SourceField);
            if (!foFieldLookup.TryGetValue(normalizedSource, out var foField))
            {
                continue;
            }

            if (!equalityConstraints.ContainsKey(foField))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(fieldMapping.DestinationField))
            {
                continue;
            }

            return new TestifyLegPlan(
                leg.LegId,
                leg.DestinationSchema,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                foField,
                fieldMapping.DestinationField.Trim(),
                correlatedRowIdField: null);
        }

        foreach (var pair in equalityConstraints)
        {
            var directField = legMappings.FirstOrDefault(row =>
                string.Equals(TestifyPlanner.NormalizeKey(row.SourceField), TestifyPlanner.NormalizeKey(pair.Key), StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(row.DestinationField));

            if (directField is null)
            {
                continue;
            }

            return new TestifyLegPlan(
                leg.LegId,
                leg.DestinationSchema,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                pair.Key,
                directField.DestinationField.Trim(),
                correlatedRowIdField: null);
        }

        return null;
    }

    internal static bool DidCeVerificationSucceedForCompletedRun(
        bool createSucceeded,
        int patchesSucceeded,
        int patchesPlanned,
        bool correlatedCeVerificationSucceeded) =>
        createSucceeded &&
        patchesSucceeded == patchesPlanned &&
        correlatedCeVerificationSucceeded;

    private static bool IsSuccessfulStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;
    private static bool IsDeleteSuccessfulStatusCode(int statusCode) => IsSuccessfulStatusCode(statusCode) || statusCode == 404;

    private static string BuildLegFieldKey(string legId, string field) =>
        $"{legId}|{TestifyPlanner.NormalizeKey(field)}";

    private void AddTestifyLog(string mapDisplayName, string phase, string status, string detail)
    {
        var row = new TestifyExecutionLogRow(DateTimeOffset.UtcNow, mapDisplayName, phase, status, detail);
        _testifyLogRows.Add(row);
    }

    internal async Task<string> FinalizeTestifyFailureAsync(
        string mapDisplayName,
        string mapId,
        TestifyMapConfiguration configuration,
        bool createdThisRun,
        string failureStatus,
        CancellationToken cancellationToken)
    {
        if (!createdThisRun || string.IsNullOrWhiteSpace(configuration.LastEntityInstanceUrl))
        {
            return failureStatus;
        }

        var rollbackSucceeded = await TryDeleteTestifyRecordAsync(
            mapDisplayName,
            mapId,
            configuration,
            configuration.LastEntityInstanceUrl,
            "Rollback",
            cancellationToken);

        return rollbackSucceeded
            ? $"{failureStatus} Created record rolled back."
            : $"{failureStatus} Rollback failed; manual cleanup may be required.";
    }

    internal async Task<bool> TryDeleteTestifyRecordAsync(
        string mapDisplayName,
        string mapId,
        TestifyMapConfiguration? configurationToClear,
        string entityInstanceUrl,
        string phase,
        CancellationToken cancellationToken)
    {
        if (_write?.ODataWrite is null || string.IsNullOrWhiteSpace(entityInstanceUrl))
        {
            return false;
        }

        try
        {
            var deleteResponse = await _write.ODataWrite.SendAsync(
                new ODataWriteRequest(HttpMethod.Delete, entityInstanceUrl),
                cancellationToken);

            if (!IsDeleteSuccessfulStatusCode(deleteResponse.StatusCode))
            {
                AddTestifyLog(mapDisplayName, phase, "Failed", $"DELETE {entityInstanceUrl} → HTTP {deleteResponse.StatusCode}. {TrimForStatus(deleteResponse.Body ?? string.Empty)}");
                return false;
            }

            AddTestifyLog(mapDisplayName, phase, "Succeeded", $"DELETE {entityInstanceUrl} → HTTP {deleteResponse.StatusCode}.");
            if (configurationToClear is not null)
            {
                configurationToClear.LastEntityInstanceUrl = null;
                configurationToClear.LastRunToken = null;
                await _testifyConfigStore.SaveAsync(configurationToClear, cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Testify {Phase} DELETE failed for map {MapId} at {Url}", phase, mapId, entityInstanceUrl);
            AddTestifyLog(mapDisplayName, phase, "Error", $"DELETE {entityInstanceUrl}: {ex.Message}");
            return false;
        }
    }

    private async Task CleanupTestifyAsync(CancellationToken cancellationToken)
    {
        if (_write?.ODataWrite is null)
        {
            StatusMessage = "Testify cleanup requires OData.Write capability, but it is not available in this host context.";
            return;
        }

        if (_testifyPlans.Count == 0)
        {
            await PrepareTestifyAsync(cancellationToken);
            if (_testifyPlans.Count == 0)
            {
                StatusMessage = "No Testify plans available for cleanup. Run 'Prepare Testify' first.";
                return;
            }
        }

        // Collect cleanup targets: stored instance URLs + live query results.
        var deleteUrls = new List<(string MapName, string Url)>();

        foreach (var plan in _testifyPlans.Values.Where(p => p.FoEntityDetails is not null))
        {
            // Include stored URL from last run.
            if (!string.IsNullOrWhiteSpace(plan.Configuration.LastEntityInstanceUrl))
            {
                deleteUrls.Add((plan.MapDisplayName, plan.Configuration.LastEntityInstanceUrl!));
            }

            // Live query: find records tagged with TESTIFY prefix.
            var tagField = FindTagField(plan.FoEntityDetails!);
            if (tagField is not null)
            {
                try
                {
                    var collectionUrl = _ctx.Catalog.BuildODataEntityUrl(_ctx.CurrentEnv, plan.FoEntity);
                    var keyNames = plan.FoEntityDetails!.Properties
                        .Where(p => p.IsKey)
                        .Select(p => p.Name)
                        .ToList();
                    var selectFields = keyNames.Concat(new[] { tagField }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    var filterExpr = $"startswith({tagField},'TESTIFY-')";
                    var queryUrl = $"{collectionUrl}?$filter={Uri.EscapeDataString(filterExpr)}&$select={string.Join(",", selectFields)}&$top=100&cross-company=true";

                    await foreach (var page in _ctx.OData.StreamAsync(new QueryRequest(queryUrl), cancellationToken))
                    {
                        foreach (var row in page.Rows)
                        {
                            var stringRow = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var pair in row)
                            {
                                if (TryGetRowValueIgnoreCase(row, pair.Key, out var sv))
                                {
                                    stringRow[pair.Key] = sv;
                                }
                            }

                            if (TestifyRunner.TryBuildEntityInstanceUrl(collectionUrl, plan.FoEntityDetails!, stringRow, out var instanceUrl, out _))
                            {
                                if (!deleteUrls.Any(d => string.Equals(d.Url, instanceUrl, StringComparison.OrdinalIgnoreCase)))
                                {
                                    deleteUrls.Add((plan.MapDisplayName, instanceUrl));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _ctx.Logger.LogWarning(ex, "Testify cleanup query failed for {Entity}", plan.FoEntity);
                }
            }
        }

        if (deleteUrls.Count == 0)
        {
            StatusMessage = "No Testify test records found to clean up.";
            TestifySummary = "Cleanup: no records found.";
            return;
        }

        var breakdown = string.Join(Environment.NewLine,
            deleteUrls.GroupBy(d => d.MapName)
                .Select(g => $"- {g.Key}: {g.Count()} record(s)"));

        var confirmation = MessageBox.Show(
            $"Delete {deleteUrls.Count} Testify test record(s)?\n\n{breakdown}\n\nThis will permanently delete FO records.",
            "Confirm Testify Cleanup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
        {
            StatusMessage = "Testify cleanup cancelled.";
            return;
        }

        var deleted = 0;
        var failed = 0;
        foreach (var (mapName, url) in deleteUrls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matchingPlan = _testifyPlans.Values.FirstOrDefault(plan =>
                string.Equals(plan.MapDisplayName, mapName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(plan.Configuration.LastEntityInstanceUrl, url, StringComparison.OrdinalIgnoreCase));
            var deleteSucceeded = await TryDeleteTestifyRecordAsync(
                mapName,
                matchingPlan?.MapId ?? string.Empty,
                matchingPlan?.Configuration,
                url,
                "Cleanup",
                cancellationToken);
            if (deleteSucceeded)
            {
                deleted++;
            }
            else
            {
                failed++;
            }
        }

        TestifySummary = $"Cleanup complete. Deleted: {deleted}. Failed: {failed}.";
        StatusMessage = $"Testify cleanup complete. Deleted {deleted} record(s).";
    }

    internal async Task<bool> CheckFoRecordExistsAsync(string instanceUrl, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _ctx.OData.StreamAsync(new QueryRequest(instanceUrl), cancellationToken))
            {
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Any error (404, network issue) means we can't confirm existence.
        }

        return false;
    }

    private static string? FindTagField(ODataEntity entity)
    {
        var candidates = new[] { "FOTBTestifyRunId", "TestifyRunId", "Description", "Name" };
        foreach (var candidate in candidates)
        {
            var property = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Type, "Edm.String", StringComparison.OrdinalIgnoreCase) &&
                !p.IsKey);

            if (property is not null)
            {
                return property.Name;
            }
        }

        return null;
    }

    private void ClearTestifyState()
    {
        _testifyPlans.Clear();
        _testifyPreflightRows.Clear();
        _testifyLogRows.Clear();
        _testifyResultRows.Clear();
        TestifySummary = "No Testify run yet.";
    }

    private static void ApplyBestEffortRunTag(ODataEntity entity, Dictionary<string, string> values, string runToken)
    {
        var tag = $"TESTIFY-{runToken}";
        var candidates = new[]
        {
            "FOTBTestifyRunId",
            "TestifyRunId",
            "Description",
            "Name"
        };

        foreach (var candidate in candidates)
        {
            var property = entity.Properties.FirstOrDefault(p =>
                string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Type, "Edm.String", StringComparison.OrdinalIgnoreCase) &&
                !p.IsKey);

            if (property is null)
            {
                continue;
            }

            if (values.ContainsKey(property.Name) && !string.IsNullOrWhiteSpace(values[property.Name]))
            {
                return;
            }

            values[property.Name] = TestifyPlanner.TrimToMaxLength(property, tag);
            return;
        }
    }
}
