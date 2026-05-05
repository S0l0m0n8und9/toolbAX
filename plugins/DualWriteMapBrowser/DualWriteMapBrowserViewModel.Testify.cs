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
                    warnings: plan.Warnings,
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
                        warnings: plan.Warnings,
                        coverageGaps: plan.CoverageGaps,
                        ceFieldAssertions: Array.Empty<TestifyCeFieldAssertion>()));
                    continue;
                }

                var createSucceeded = false;
                var patchesSucceeded = 0;
                var ceSucceeded = false;
                var valid = false;
                var status = "Unknown error.";
                var createdThisRun = false;
                IReadOnlyDictionary<string, string>? correlatedCeRowIdentities = null;
                var ceFieldAssertions = new List<TestifyCeFieldAssertion>();

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

                        AddTestifyLog(plan.MapDisplayName, "Create Readback", "Started", "Verifying FO persisted values after create.");
                        await VerifyFoPersistedValuesAsync(entityInstanceUrl, plan.FoEntityDetails!, runtimeCreateValues, "FO create", cancellationToken);
                        AddTestifyLog(plan.MapDisplayName, "Create Readback", "Succeeded", "FO readback matched expected create values.");
                        createSucceeded = true;

                        correlatedCeRowIdentities = await WaitForCeCorrelationAsync(plan, runtimeCreateValues, expectedRowIdentities: null, cancellationToken, "after create");
                        AddTestifyLog(plan.MapDisplayName, "CE Verify", "Succeeded", "Correlated CE row matched after create.");
                        var ceRowsAfterCreate = await ReadCorrelatedCeRowsAsync(plan, runtimeCreateValues, correlatedCeRowIdentities, cancellationToken);
                        var createAssertions = EvaluateCeFieldAssertions(plan, runtimeCreateValues, ceRowsAfterCreate, "Create");
                        ceFieldAssertions.AddRange(createAssertions);
                        AddCeAssertionLog(plan.MapDisplayName, "Create", createAssertions.Count);
                    }
                    else
                    {
                        entityInstanceUrl = plan.Configuration.LastEntityInstanceUrl!;
                        await HydrateCorrelationValuesFromFoRecordAsync(plan, entityInstanceUrl, runtimeCreateValues, cancellationToken);
                        correlatedCeRowIdentities = await WaitForCeCorrelationAsync(plan, runtimeCreateValues, expectedRowIdentities: null, cancellationToken, "before patch");
                        AddTestifyLog(plan.MapDisplayName, "CE Verify", "Succeeded", "Correlated CE row matched before patching reused record.");
                    }

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

                        AddTestifyLog(plan.MapDisplayName, "Patch", "Succeeded", $"PATCH step {step.StepNumber} returned HTTP {patchResponse.StatusCode}.");
                        AddTestifyLog(plan.MapDisplayName, "Patch Readback", "Started", $"Verifying FO persisted values after patch {step.StepNumber}.");
                        await VerifyFoPersistedValuesAsync(entityInstanceUrl, plan.FoEntityDetails!, step.EnumValues, $"FO PATCH step {step.StepNumber}", cancellationToken);
                        AddTestifyLog(plan.MapDisplayName, "Patch Readback", "Succeeded", $"FO readback matched expected values after patch {step.StepNumber}.");
                        patchesSucceeded++;
                        foreach (var pair in step.EnumValues)
                        {
                            runtimeCreateValues[pair.Key] = pair.Value;
                        }

                        correlatedCeRowIdentities = await WaitForCeCorrelationAsync(
                            plan,
                            runtimeCreateValues,
                            correlatedCeRowIdentities,
                            cancellationToken,
                            $"after patch {step.StepNumber}");
                        AddTestifyLog(plan.MapDisplayName, "CE Verify", "Succeeded", $"Correlated CE row remained stable after patch {step.StepNumber}.");
                        var ceRowsAfterPatch = await ReadCorrelatedCeRowsAsync(plan, runtimeCreateValues, correlatedCeRowIdentities, cancellationToken);
                        var patchAssertions = EvaluateCeFieldAssertions(plan, runtimeCreateValues, ceRowsAfterPatch, $"Patch {step.StepNumber}");
                        ceFieldAssertions.AddRange(patchAssertions);
                        AddCeAssertionLog(plan.MapDisplayName, $"Patch {step.StepNumber}", patchAssertions.Count);
                    }

                    if (!DidCeVerificationSucceedForCompletedRun(createSucceeded, patchesSucceeded, plan.PatchSteps.Count, ceFieldAssertions.Count))
                    {
                        throw new InvalidOperationException("CE verification completed with zero assertable CE assertions evaluated. Resolve skipped CE assertion coverage in preflight before running this map.");
                    }

                    ceSucceeded = ceFieldAssertions.All(a => a.Passed);
                    valid = true;
                    status = $"Valid map. CE assertions: {ceFieldAssertions.Count(a => a.Passed)}/{ceFieldAssertions.Count}.";
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
                    plan.Warnings,
                    plan.CoverageGaps,
                    ceFieldAssertions));
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
        var patchSteps = Array.Empty<TestifyPatchStep>();
        var ceFieldPlans = new List<TestifyCeFieldPlan>();
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

            var axLegIds = new HashSet<string>(axToCrmLegs.Select(l => l.LegId), StringComparer.OrdinalIgnoreCase);
            var transformsByLegAndSource = map.MappingValueTransformRows
                .Where(t => axLegIds.Contains(t.LegId))
                .GroupBy(t => BuildLegFieldKey(t.LegId, t.SourceField), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var transformsByLegSourceAndDestination = map.MappingValueTransformRows
                .Where(t => axLegIds.Contains(t.LegId))
                .GroupBy(t => BuildLegSourceDestinationKey(t.LegId, t.SourceField, t.DestinationField), StringComparer.OrdinalIgnoreCase)
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

            var keyFoFields = foEntityDetails.Properties
                .Where(p => p.IsKey)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var leg in axToCrmLegs)
            {
                if (string.IsNullOrWhiteSpace(leg.DestinationSchema))
                {
                    blockingIssues.Add($"Missing CE entity for leg '{leg.LegId}'.");
                    continue;
                }

                if (!TryResolveCeCorrelationDescriptor(
                    map,
                    leg,
                    createValues,
                    keyFoFields,
                    out var correlationFoField,
                    out var correlationCeField,
                    out var correlationError))
                {
                    blockingIssues.Add(correlationError);
                    continue;
                }

                ceLegs.Add(new TestifyLegPlan(
                    leg.LegId,
                    leg.DestinationSchema.Trim(),
                    NormalizeCeFilterExpression(leg.ReversedSourceFilter),
                    correlationFoField,
                    correlationCeField));
            }

            var ceFieldPlanLookup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fieldMapping in map.MappingFieldRows.Where(f => axLegIds.Contains(f.LegId)))
            {
                var normalizedSource = TestifyPlanner.NormalizeKey(fieldMapping.SourceField);
                if (!fieldNameLookup.TryGetValue(normalizedSource, out var actualFoField))
                {
                    continue;
                }

                var foProperty = foEntityDetails.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, actualFoField, StringComparison.OrdinalIgnoreCase));
                if (foProperty is null)
                {
                    continue;
                }

                var ceField = fieldMapping.DestinationField.Trim();
                if (!Regex.IsMatch(ceField, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                {
                    continue;
                }

                var dedupeKey = $"{fieldMapping.LegId}|{actualFoField}|{ceField}";
                if (!ceFieldPlanLookup.Add(dedupeKey))
                {
                    continue;
                }

                var transformKey = BuildLegSourceDestinationKey(fieldMapping.LegId, fieldMapping.SourceField, fieldMapping.DestinationField);
                if (transformsByLegSourceAndDestination.TryGetValue(transformKey, out var transforms) && transforms.Count > 0)
                {
                    var valueMapTransforms = transforms
                        .Where(t => !string.IsNullOrWhiteSpace(t.ValueMap))
                        .ToList();
                    if (valueMapTransforms.Count > 0)
                    {
                        if (TryBuildCeValueMapAssertionPlan(
                            fieldMapping.LegId,
                            actualFoField,
                            foProperty.Type,
                            ceField,
                            valueMapTransforms,
                            out var ceFieldPlan,
                            out var planWarning))
                        {
                            ceFieldPlans.Add(ceFieldPlan);
                        }
                        else if (!string.IsNullOrWhiteSpace(planWarning))
                        {
                            warnings.Add(planWarning);
                        }

                        continue;
                    }

                    if (IsSupportedDirectCeScalarType(foProperty.Type))
                    {
                        ceFieldPlans.Add(new TestifyCeFieldPlan(
                            legId: fieldMapping.LegId,
                            foField: actualFoField,
                            foFieldType: foProperty.Type,
                            ceField: ceField,
                            kind: TestifyCeFieldAssertionKind.DirectScalar,
                            valueMap: null,
                            defaultValue: null));
                    }
                    else
                    {
                        warnings.Add(BuildUnsupportedDirectCeAssertionWarning(fieldMapping.LegId, actualFoField, ceField, foProperty.Type));
                    }
                }
                else if (IsSupportedDirectCeScalarType(foProperty.Type))
                {
                    ceFieldPlans.Add(new TestifyCeFieldPlan(
                        legId: fieldMapping.LegId,
                        foField: actualFoField,
                        foFieldType: foProperty.Type,
                        ceField: ceField,
                        kind: TestifyCeFieldAssertionKind.DirectScalar,
                        valueMap: null,
                        defaultValue: null));
                }
                else
                {
                    warnings.Add(BuildUnsupportedDirectCeAssertionWarning(fieldMapping.LegId, actualFoField, ceField, foProperty.Type));
                }
            }

            if (ceLegs.Count == 0)
            {
                blockingIssues.Add("No AX->CRM leg produced a deterministic CE correlation descriptor.");
            }
            else if (ceFieldPlans.Count == 0)
            {
                blockingIssues.Add(BuildNoAssertableCeCoverageIssue(warnings));
            }

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
            patchSteps: patchSteps,
            ceFieldPlans: ceFieldPlans,
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

        if (plan.HasAssertableCeCoverageGap)
        {
            return "Blocked: no assertable CE coverage";
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

    private static string BuildNoAssertableCeCoverageIssue(IReadOnlyList<string> warnings)
    {
        var skippedAssertionWarnings = warnings
            .Where(IsSkippedCeAssertionWarning)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (skippedAssertionWarnings.Length == 0)
        {
            return "No assertable CE field coverage could be generated for runnable AX->CRM legs.";
        }

        return $"No assertable CE field coverage could be generated for runnable AX->CRM legs. {string.Join(" ", skippedAssertionWarnings)}";
    }

    private static bool IsSkippedCeAssertionWarning(string warning) =>
        warning.StartsWith("Skipped CE assertion", StringComparison.OrdinalIgnoreCase);

    private static string BuildUnsupportedDirectCeAssertionWarning(string legId, string foField, string ceField, string foFieldType) =>
        $"Skipped CE assertion for '{foField}->{ceField}' on leg '{legId}' because FO type '{foFieldType}' is not yet supported for direct CE assertions.";

    private void AddCeAssertionLog(string mapDisplayName, string phase, int assertionCount)
    {
        if (assertionCount > 0)
        {
            AddTestifyLog(mapDisplayName, "CE Assert", "Succeeded", $"{phase} CE field assertions passed ({assertionCount}/{assertionCount}).");
            return;
        }

        AddTestifyLog(mapDisplayName, "CE Assert", "Blocked", $"{phase} completed with no assertable CE field assertions evaluated.");
    }

    internal async Task<IReadOnlyDictionary<string, string>> WaitForCeCorrelationAsync(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        IReadOnlyDictionary<string, string>? expectedRowIdentities,
        CancellationToken cancellationToken,
        string phase)
    {
        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var timeoutMinutes = plan.Configuration.CePollTimeoutMinutes > 0 ? plan.Configuration.CePollTimeoutMinutes : 5;
        var deadline = TestifyUtcNow().AddMinutes(timeoutMinutes);
        var criteriaByLeg = BuildCeCorrelationCriteria(plan, runtimeCreateValues, expectedRowIdentities);

        while (TestifyUtcNow() <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var allReached = true;
            var matchedRowIdentities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var criteria in criteriaByLeg)
            {
                var rows = await QueryDataverseRowsAsync(dataverseHttp, apiBase, criteria.CeEntity, criteria.CorrelationFilter, top: 2, selectColumns: null, cancellationToken);
                if (rows.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"CE correlation for leg '{criteria.LegId}' matched {rows.Count} rows ({phase}). Expected exactly one row for {criteria.CorrelationCeField}='{criteria.ExpectedValue}'.");
                }

                if (rows.Count == 0)
                {
                    allReached = false;
                    continue;
                }

                var row = rows[0];
                if (!TryGetRowValueIgnoreCase(row, criteria.CorrelationCeField, out var actualCorrelationValue) ||
                    !string.Equals(actualCorrelationValue, criteria.ExpectedValue, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"CE correlation for leg '{criteria.LegId}' returned an unrelated row ({phase}). Expected {criteria.CorrelationCeField}='{criteria.ExpectedValue}'.");
                }

                var rowIdentity = TryGetDataverseRowIdentity(criteria.CeEntity, row);
                if (string.IsNullOrWhiteSpace(rowIdentity))
                {
                    throw new InvalidOperationException(
                        $"CE correlation for leg '{criteria.LegId}' ({criteria.CeEntity}) matched a row, but no stable row identity field was found.");
                }

                if (!string.IsNullOrWhiteSpace(criteria.ExpectedRowIdentity) &&
                    !string.Equals(criteria.ExpectedRowIdentity, rowIdentity, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"CE correlation for leg '{criteria.LegId}' matched a different row after update ({phase}). Expected '{criteria.ExpectedRowIdentity}' but found '{rowIdentity}'.");
                }

                matchedRowIdentities[criteria.LegId] = rowIdentity;
            }

            if (allReached)
            {
                return matchedRowIdentities;
            }

            await TestifyDelayAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }

        throw new InvalidOperationException($"CE correlation verification timed out ({phase}) after {timeoutMinutes} minute(s). Increase CePollTimeoutMinutes in Testify configuration if sync is slow.");
    }

    private static IReadOnlyList<TestifyCeCorrelationCriteria> BuildCeCorrelationCriteria(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        IReadOnlyDictionary<string, string>? expectedRowIdentities)
    {
        var criteria = new List<TestifyCeCorrelationCriteria>(plan.CeLegs.Count);

        foreach (var leg in plan.CeLegs)
        {
            if (!runtimeCreateValues.TryGetValue(leg.CorrelationFoField, out var expectedValue) || string.IsNullOrWhiteSpace(expectedValue))
            {
                throw new InvalidOperationException(
                    $"CE correlation for leg '{leg.LegId}' requires FO field '{leg.CorrelationFoField}', but no runtime value was available.");
            }

            var normalizedExpected = expectedValue.Trim();
            var correlationFilter = BuildCeCorrelationFilter(leg.CeFilter, leg.CorrelationCeField, normalizedExpected);
            string? expectedRowIdentity = null;
            expectedRowIdentities?.TryGetValue(leg.LegId, out expectedRowIdentity);

            criteria.Add(new TestifyCeCorrelationCriteria(
                leg.LegId,
                leg.CeEntity,
                leg.CorrelationCeField,
                normalizedExpected,
                correlationFilter,
                expectedRowIdentity));
        }

        return criteria;
    }

    private static string BuildCeCorrelationFilter(string baseFilter, string correlationCeField, string expectedValue)
    {
        var normalizedBaseFilter = NormalizeCeFilterExpression(baseFilter);
        var escapedValue = expectedValue.Replace("'", "''", StringComparison.Ordinal);
        var correlationClause = $"{correlationCeField} eq '{escapedValue}'";

        if (string.IsNullOrWhiteSpace(normalizedBaseFilter))
        {
            return correlationClause;
        }

        return $"({normalizedBaseFilter}) and ({correlationClause})";
    }

    internal async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>> ReadCorrelatedCeRowsAsync(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        IReadOnlyDictionary<string, string> expectedRowIdentities,
        CancellationToken cancellationToken)
    {
        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(_dataverse.CurrentDataverseEnv!.BaseUrl);
        var criteriaByLeg = BuildCeCorrelationCriteria(plan, runtimeCreateValues, expectedRowIdentities);
        var ceFieldsByLeg = plan.CeFieldPlans
            .GroupBy(p => p.LegId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<string>)g.Select(p => p.CeField)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var rowsByLeg = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var criteria in criteriaByLeg)
        {
            var selectColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                criteria.CorrelationCeField
            };
            if (ceFieldsByLeg.TryGetValue(criteria.LegId, out var ceFields))
            {
                foreach (var field in ceFields)
                {
                    if (!string.IsNullOrWhiteSpace(field) && Regex.IsMatch(field, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                    {
                        selectColumns.Add(field);
                    }
                }
            }

            var rows = await QueryDataverseRowsAsync(
                dataverseHttp,
                apiBase,
                criteria.CeEntity,
                criteria.CorrelationFilter,
                top: 2,
                selectColumns,
                cancellationToken);
            if (rows.Count != 1)
            {
                throw new InvalidOperationException(
                    $"CE correlation for leg '{criteria.LegId}' could not be read for assertion because {rows.Count} rows matched.");
            }

            var row = rows[0];
            var rowIdentity = TryGetDataverseRowIdentity(criteria.CeEntity, row);
            if (string.IsNullOrWhiteSpace(rowIdentity))
            {
                throw new InvalidOperationException(
                    $"CE correlation for leg '{criteria.LegId}' could not be read for assertion because row identity was not found.");
            }

            if (!string.IsNullOrWhiteSpace(criteria.ExpectedRowIdentity) &&
                !string.Equals(criteria.ExpectedRowIdentity, rowIdentity, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CE assertion readback for leg '{criteria.LegId}' resolved a different row. Expected '{criteria.ExpectedRowIdentity}' but found '{rowIdentity}'.");
            }

            rowsByLeg[criteria.LegId] = row;
        }

        return rowsByLeg;
    }

    internal static IReadOnlyList<TestifyCeFieldAssertion> EvaluateCeFieldAssertions(
        TestifyMapPlan plan,
        IReadOnlyDictionary<string, string> runtimeCreateValues,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> ceRowsByLeg,
        string phase)
    {
        if (plan.CeFieldPlans.Count == 0)
        {
            return Array.Empty<TestifyCeFieldAssertion>();
        }

        var assertions = new List<TestifyCeFieldAssertion>();
        foreach (var cePlan in plan.CeFieldPlans)
        {
            if (!ceRowsByLeg.TryGetValue(cePlan.LegId, out var ceRow))
            {
                continue;
            }

            if (!runtimeCreateValues.TryGetValue(cePlan.FoField, out var sourceValue) || string.IsNullOrWhiteSpace(sourceValue))
            {
                continue;
            }

            if (!TryResolveExpectedCeValue(cePlan, sourceValue, out var expectedValue, out var resolutionError))
            {
                throw new InvalidOperationException(resolutionError);
            }

            if (!TryGetRowValueIgnoreCase(ceRow, cePlan.CeField, out var actualValue))
            {
                throw new InvalidOperationException(
                    $"CE {phase} assertion failed for leg '{cePlan.LegId}' field '{cePlan.CeField}': readback row did not include the field.");
            }

            var passed = AreEquivalentCeValues(cePlan, expectedValue, actualValue);
            assertions.Add(new TestifyCeFieldAssertion(
                phase,
                cePlan.LegId,
                cePlan.FoField,
                cePlan.CeField,
                expectedValue,
                actualValue,
                passed));
        }

        var failed = assertions.Where(a => !a.Passed).ToList();
        if (failed.Count > 0)
        {
            var first = failed[0];
            throw new InvalidOperationException(
                $"CE {first.Phase} assertion failed for leg '{first.LegId}' field '{first.CeField}': expected '{first.ExpectedDisplay}' but found '{first.ActualDisplay}'.");
        }

        return assertions;
    }

    private static bool TryResolveExpectedCeValue(
        TestifyCeFieldPlan cePlan,
        string sourceValue,
        out string? expectedValue,
        out string error)
    {
        expectedValue = null;
        error = string.Empty;
        var normalizedSource = sourceValue.Trim();

        if (cePlan.Kind == TestifyCeFieldAssertionKind.ValueMap)
        {
            if (cePlan.ValueMap.TryGetValue(normalizedSource, out var mapped))
            {
                expectedValue = mapped;
                return true;
            }

            if (cePlan.DefaultValue is not null)
            {
                expectedValue = cePlan.DefaultValue;
                return true;
            }

            error = $"CE assertion could not resolve mapped value for FO field '{cePlan.FoField}'='{normalizedSource}' targeting '{cePlan.CeField}'.";
            return false;
        }

        expectedValue = NormalizeExpectedDirectCeValue(cePlan.FoFieldType, normalizedSource);
        return true;
    }

    private static string? NormalizeExpectedDirectCeValue(string foFieldType, string value)
    {
        if (string.Equals(foFieldType, "Edm.Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeBooleanString(value);
        }

        if (string.Equals(foFieldType, "Edm.Date", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDateOnlyString(value) ?? value.Trim();
        }

        if (string.Equals(foFieldType, "Edm.DateTimeOffset", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDateTimeOffsetString(value) ?? value.Trim();
        }

        if (IsNumericEdmType(foFieldType))
        {
            return NormalizeNumericString(value);
        }

        if (string.Equals(foFieldType, "Edm.Guid", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeGuidString(value) ?? value.Trim();
        }

        return value;
    }

    private static bool AreEquivalentCeValues(TestifyCeFieldPlan cePlan, string? expectedValue, string? actualValue)
    {
        if (string.Equals(cePlan.FoFieldType, "Edm.Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeBooleanString(expectedValue), NormalizeBooleanString(actualValue), StringComparison.Ordinal);
        }

        if (string.Equals(cePlan.FoFieldType, "Edm.Date", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeDateOnlyString(expectedValue), NormalizeDateOnlyString(actualValue), StringComparison.Ordinal);
        }

        if (string.Equals(cePlan.FoFieldType, "Edm.DateTimeOffset", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(NormalizeDateTimeOffsetString(expectedValue), NormalizeDateTimeOffsetString(actualValue), StringComparison.Ordinal);
        }

        var normalizedExpectedGuid = NormalizeGuidString(expectedValue);
        var normalizedActualGuid = NormalizeGuidString(actualValue);
        if (normalizedExpectedGuid is not null && normalizedActualGuid is not null)
        {
            return string.Equals(normalizedExpectedGuid, normalizedActualGuid, StringComparison.Ordinal);
        }

        if (cePlan.Kind == TestifyCeFieldAssertionKind.ValueMap &&
            TryNormalizeBooleanShapedValue(expectedValue, out var normalizedExpectedBoolean) &&
            TryNormalizeBooleanShapedValue(actualValue, out var normalizedActualBoolean))
        {
            return string.Equals(normalizedExpectedBoolean, normalizedActualBoolean, StringComparison.Ordinal);
        }

        var shouldCompareAsNumeric = cePlan.Kind == TestifyCeFieldAssertionKind.ValueMap || IsNumericEdmType(cePlan.FoFieldType);
        if (shouldCompareAsNumeric &&
            decimal.TryParse(expectedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNumber) &&
            decimal.TryParse(actualValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var actualNumber))
        {
            return expectedNumber == actualNumber;
        }

        return string.Equals((expectedValue ?? string.Empty).Trim(), (actualValue ?? string.Empty).Trim(), StringComparison.Ordinal);
    }

    private static bool TryNormalizeBooleanShapedValue(string? value, out string normalizedValue)
    {
        normalizedValue = string.Empty;
        if (value is null)
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!string.Equals(trimmed, "0", StringComparison.Ordinal) &&
            !string.Equals(trimmed, "1", StringComparison.Ordinal) &&
            !string.Equals(trimmed, bool.FalseString, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(trimmed, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedValue = NormalizeBooleanString(trimmed) ?? string.Empty;
        return true;
    }

    private static string? NormalizeBooleanString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed == "1")
        {
            return bool.TrueString.ToLowerInvariant();
        }

        if (trimmed == "0")
        {
            return bool.FalseString.ToLowerInvariant();
        }

        if (bool.TryParse(trimmed, out var parsed))
        {
            return parsed ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant();
        }

        return trimmed;
    }

    private static string? NormalizeDateOnlyString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsedOffset))
        {
            return DateOnly.FromDateTime(parsedOffset.Date).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsedDateTime))
        {
            return DateOnly.FromDateTime(parsedDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static string? NormalizeDateTimeOffsetString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (!DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    }

    private static string NormalizeNumericString(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric))
        {
            return value;
        }

        return numeric.ToString(CultureInfo.InvariantCulture);
    }

    private static string? NormalizeGuidString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        trimmed = trimmed.Trim('{', '}');
        return Guid.TryParse(trimmed, out var guid)
            ? guid.ToString("D")
            : null;
    }

    private static bool IsNumericEdmType(string? type) =>
        string.Equals(type, "Edm.Int16", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Int32", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Int64", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Decimal", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Double", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Single", StringComparison.OrdinalIgnoreCase);

    private static bool TryBuildCeValueMapAssertionPlan(
        string legId,
        string foField,
        string foFieldType,
        string ceField,
        IReadOnlyList<MappingValueTransformRow> transforms,
        out TestifyCeFieldPlan plan,
        out string warning)
    {
        plan = null!;
        warning = string.Empty;

        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? defaultValue = null;
        foreach (var transform in transforms)
        {
            if (!TestifyValueMapParser.TryExtractMappings(transform.ValueMap, out var mappings, out var parseError))
            {
                warning = $"Skipped CE assertion for '{foField}->{ceField}' on leg '{legId}' because valueMap parsing failed: {parseError}";
                return false;
            }

            foreach (var pair in mappings)
            {
                if (merged.TryGetValue(pair.Key, out var existing) &&
                    !string.Equals(existing ?? string.Empty, pair.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    warning = $"Skipped CE assertion for '{foField}->{ceField}' on leg '{legId}' because valueMap has conflicting target values for source '{pair.Key}'.";
                    return false;
                }

                merged[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(transform.DefaultValue))
            {
                if (defaultValue is null)
                {
                    defaultValue = transform.DefaultValue.Trim();
                }
                else if (!string.Equals(defaultValue, transform.DefaultValue.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    warning = $"Skipped CE assertion for '{foField}->{ceField}' on leg '{legId}' because valueMap default values conflict.";
                    return false;
                }
            }
        }

        if (merged.Count == 0 && defaultValue is null)
        {
            warning = $"Skipped CE assertion for '{foField}->{ceField}' on leg '{legId}' because valueMap had no assertable outputs.";
            return false;
        }

        plan = new TestifyCeFieldPlan(
            legId: legId,
            foField: foField,
            foFieldType: foFieldType,
            ceField: ceField,
            kind: TestifyCeFieldAssertionKind.ValueMap,
            valueMap: merged,
            defaultValue: defaultValue);
        return true;
    }

    private static bool IsSupportedDirectCeScalarType(string type) =>
        string.Equals(type, "Edm.String", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Boolean", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.Date", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "Edm.DateTimeOffset", StringComparison.OrdinalIgnoreCase) ||
        IsNumericEdmType(type) ||
        string.Equals(type, "Edm.Guid", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCeFilterExpression(string? rawFilter)
    {
        if (string.IsNullOrWhiteSpace(rawFilter))
        {
            return string.Empty;
        }

        var trimmed = rawFilter.Trim();
        return trimmed.StartsWith("$filter=", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring("$filter=".Length).Trim()
            : trimmed;
    }

    private static async Task<IReadOnlyList<IReadOnlyDictionary<string, object?>>> QueryDataverseRowsAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        int top,
        IReadOnlyCollection<string>? selectColumns,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { $"$top={Math.Max(1, top)}" };
        if (selectColumns is not null && selectColumns.Count > 0)
        {
            var select = string.Join(",", selectColumns.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(select))
            {
                query.Add($"$select={Uri.EscapeDataString(select)}");
            }
        }
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        var url = $"{apiBase}/{entitySetName}?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("Prefer", $"odata.maxpagesize={Math.Max(1, top)}");

        using var response = await dataverseHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Dataverse row query failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Dataverse row query response did not contain a 'value' array.");
        }

        var rows = new List<IReadOnlyDictionary<string, object?>>(valueArray.GetArrayLength());
        foreach (var rowElement in valueArray.EnumerateArray())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in rowElement.EnumerateObject())
            {
                row[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
                    JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };
            }

            rows.Add(row);
        }

        return rows;
    }

    private static string? TryGetDataverseRowIdentity(string entitySetName, IReadOnlyDictionary<string, object?> row)
    {
        if (TryGetRowValueIgnoreCase(row, "@odata.id", out var odataId) && !string.IsNullOrWhiteSpace(odataId))
        {
            return odataId;
        }

        var candidatePrimary = entitySetName.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            ? $"{entitySetName.Substring(0, entitySetName.Length - 1)}id"
            : $"{entitySetName}id";
        if (TryGetRowValueIgnoreCase(row, candidatePrimary, out var primaryId) && !string.IsNullOrWhiteSpace(primaryId))
        {
            return primaryId;
        }

        foreach (var pair in row.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!pair.Key.EndsWith("id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryGetRowValueIgnoreCase(row, pair.Key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private sealed class TestifyCeCorrelationCriteria
    {
        public TestifyCeCorrelationCriteria(
            string legId,
            string ceEntity,
            string correlationCeField,
            string expectedValue,
            string correlationFilter,
            string? expectedRowIdentity)
        {
            LegId = legId;
            CeEntity = ceEntity;
            CorrelationCeField = correlationCeField;
            ExpectedValue = expectedValue;
            CorrelationFilter = correlationFilter;
            ExpectedRowIdentity = expectedRowIdentity;
        }

        public string LegId { get; }
        public string CeEntity { get; }
        public string CorrelationCeField { get; }
        public string ExpectedValue { get; }
        public string CorrelationFilter { get; }
        public string? ExpectedRowIdentity { get; }
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

    internal static bool DidCeVerificationSucceedForCompletedRun(bool createSucceeded, int patchesSucceeded, int patchesPlanned, int ceAssertionsEvaluated = 1) =>
        createSucceeded && patchesSucceeded == patchesPlanned && ceAssertionsEvaluated > 0;

    private static bool IsSuccessfulStatusCode(int statusCode) => statusCode >= 200 && statusCode <= 299;
    private static bool IsDeleteSuccessfulStatusCode(int statusCode) => IsSuccessfulStatusCode(statusCode) || statusCode == 404;

    private static bool TryResolveCeCorrelationDescriptor(
        DualWriteMapRecord map,
        MappingLegRow leg,
        IReadOnlyDictionary<string, string> createValues,
        IReadOnlySet<string> keyFoFields,
        out string correlationFoField,
        out string correlationCeField,
        out string error)
    {
        correlationFoField = string.Empty;
        correlationCeField = string.Empty;
        error = string.Empty;

        var createFieldsByNormalized = createValues.Keys
            .GroupBy(TestifyPlanner.NormalizeKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(int Score, string FoField, string CeField)>();

        foreach (var mapping in map.MappingFieldRows.Where(r =>
                     string.Equals(r.LegId, leg.LegId, StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(r.SourceField) &&
                     !string.IsNullOrWhiteSpace(r.DestinationField)))
        {
            var normalizedSource = TestifyPlanner.NormalizeKey(mapping.SourceField);
            if (!createFieldsByNormalized.TryGetValue(normalizedSource, out var foField) ||
                !createValues.TryGetValue(foField, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var ceField = mapping.DestinationField.Trim();
            if (!Regex.IsMatch(ceField, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            {
                continue;
            }

            var score = 0;
            if (IsRunTagField(foField))
            {
                score += 300;
            }

            if (value.StartsWith("TESTIFY", StringComparison.OrdinalIgnoreCase))
            {
                score += 200;
            }

            if (keyFoFields.Contains(foField))
            {
                score += 100;
            }

            if (!string.IsNullOrWhiteSpace(mapping.SyncDirection) &&
                (mapping.SyncDirection.Contains("Source", StringComparison.OrdinalIgnoreCase) ||
                 mapping.SyncDirection.Contains("Bidirectional", StringComparison.OrdinalIgnoreCase)))
            {
                score += 10;
            }

            candidates.Add((score, foField, ceField));
        }

        if (candidates.Count == 0)
        {
            error = $"Leg '{leg.LegId}' is missing a deterministic CE correlation field. Add an AX->CRM field mapping that carries a run-tag or key field value into CE.";
            return false;
        }

        var selected = candidates
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.FoField, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.CeField, StringComparer.OrdinalIgnoreCase)
            .First();

        if (!createValues.TryGetValue(selected.FoField, out var selectedValue) || string.IsNullOrWhiteSpace(selectedValue))
        {
            error = $"Leg '{leg.LegId}' selected FO field '{selected.FoField}' for CE correlation but no create value is available.";
            return false;
        }

        var isDeterministic = selectedValue.StartsWith("TESTIFY", StringComparison.OrdinalIgnoreCase) ||
                              IsRunTagField(selected.FoField) ||
                              keyFoFields.Contains(selected.FoField);
        if (!isDeterministic)
        {
            error = $"Leg '{leg.LegId}' correlation candidate '{selected.FoField}->{selected.CeField}' is not deterministic. Use a run-tag or key field mapping.";
            return false;
        }

        correlationFoField = selected.FoField;
        correlationCeField = selected.CeField;
        return true;
    }

    private static bool IsRunTagField(string fieldName)
    {
        return fieldName.Equals("FOTBTestifyRunId", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("TestifyRunId", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("Description", StringComparison.OrdinalIgnoreCase) ||
               fieldName.Equals("Name", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildLegFieldKey(string legId, string field) =>
        $"{legId}|{TestifyPlanner.NormalizeKey(field)}";

    private static string BuildLegSourceDestinationKey(string legId, string sourceField, string destinationField) =>
        $"{legId}|{TestifyPlanner.NormalizeKey(sourceField)}|{TestifyPlanner.NormalizeKey(destinationField)}";

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

    internal async Task VerifyFoPersistedValuesAsync(
        string entityInstanceUrl,
        ODataEntity entity,
        IReadOnlyDictionary<string, string> expectedValues,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        var row = await ReadFoRecordAsync(entityInstanceUrl, operationLabel, cancellationToken);

        foreach (var property in entity.Properties.Where(p => expectedValues.ContainsKey(p.Name)))
        {
            var expectedValue = NormalizeExpectedFoValue(property, expectedValues[property.Name]);
            if (!TryGetRowValueIgnoreCase(row, property.Name, out var actualValue))
            {
                throw new InvalidOperationException($"{operationLabel} succeeded but FO readback did not include persisted field '{property.Name}'.");
            }

            if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{operationLabel} succeeded but persisted value for '{property.Name}' was '{actualValue}' instead of expected '{expectedValue}'.");
            }
        }
    }

    private async Task HydrateCorrelationValuesFromFoRecordAsync(
        TestifyMapPlan plan,
        string entityInstanceUrl,
        Dictionary<string, string> runtimeCreateValues,
        CancellationToken cancellationToken)
    {
        var row = await ReadFoRecordAsync(entityInstanceUrl, "FO correlation readback", cancellationToken);
        foreach (var foField in plan.CeLegs
                     .Select(leg => leg.CorrelationFoField)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (TryGetRowValueIgnoreCase(row, foField, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                runtimeCreateValues[foField] = value;
            }
        }
    }

    private async Task<IReadOnlyDictionary<string, object?>> ReadFoRecordAsync(
        string entityInstanceUrl,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var page in _ctx.OData.StreamAsync(new QueryRequest(entityInstanceUrl), cancellationToken))
            {
                var row = page.Rows.FirstOrDefault();
                if (row is not null)
                {
                    return row;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{operationLabel} succeeded but FO readback failed: {TrimForStatus(ex.Message)}", ex);
        }

        throw new InvalidOperationException($"{operationLabel} succeeded but FO readback returned no rows.");
    }

    private static string NormalizeExpectedFoValue(ODataProperty property, string value)
    {
        if (string.Equals(property.Type, "Edm.Boolean", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(value, bool.TrueString, StringComparison.OrdinalIgnoreCase) ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant();
        }

        return value;
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
