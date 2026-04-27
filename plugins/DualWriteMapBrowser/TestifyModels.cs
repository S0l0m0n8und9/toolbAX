using System;
using System.Collections.Generic;
using System.Linq;

namespace DualWriteMapBrowserPlugin;

public sealed class TestifyMapPlan
{
    public TestifyMapPlan(
        string mapId,
        string mapDisplayName,
        string foEntity,
        FoToolbox.Core.OData.ODataEntity? foEntityDetails,
        TestifyMapConfiguration configuration,
        string foFilter,
        IReadOnlyList<TestifyLegPlan> ceLegs,
        IReadOnlyDictionary<string, string> createValues,
        string createPayloadJson,
        IReadOnlyDictionary<string, TestifyEnumFieldPlan> enumFields,
        IReadOnlyList<TestifyPatchStep> patchSteps,
        IReadOnlyList<string> warnings,
        IReadOnlyList<TestifyEnumCoverageGap> coverageGaps,
        IReadOnlyList<string> blockingIssues)
    {
        MapId = mapId;
        MapDisplayName = mapDisplayName;
        FoEntity = foEntity;
        FoEntityDetails = foEntityDetails;
        Configuration = configuration;
        FoFilter = foFilter;
        CeLegs = ceLegs;
        CreateValues = createValues;
        CreatePayloadJson = createPayloadJson;
        EnumFields = enumFields;
        PatchSteps = patchSteps;
        Warnings = warnings;
        CoverageGaps = coverageGaps;
        BlockingIssues = blockingIssues;
    }

    public string MapId { get; }
    public string MapDisplayName { get; }
    public string FoEntity { get; }
    public FoToolbox.Core.OData.ODataEntity? FoEntityDetails { get; }
    public TestifyMapConfiguration Configuration { get; }
    public string FoFilter { get; }
    public IReadOnlyList<TestifyLegPlan> CeLegs { get; }
    public IReadOnlyDictionary<string, string> CreateValues { get; }
    public string CreatePayloadJson { get; }
    public IReadOnlyDictionary<string, TestifyEnumFieldPlan> EnumFields { get; }
    public IReadOnlyList<TestifyPatchStep> PatchSteps { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<TestifyEnumCoverageGap> CoverageGaps { get; }
    public IReadOnlyList<TestifyFieldCoverageGap> CoverageGapsByField => CoverageGaps
        .GroupBy(gap => gap.FieldName, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.First().FieldName, StringComparer.OrdinalIgnoreCase)
        .Select(group => new TestifyFieldCoverageGap(
            group.First().FieldName,
            group.Select(gap => gap.EnumValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
        .ToArray();
    public string CoverageGapFieldDetail => CoverageGapsByField.Count == 0
        ? string.Empty
        : string.Join("; ", CoverageGapsByField.Select(gap => gap.Detail));
    public IReadOnlyList<string> BlockingIssues { get; }
    public bool CanRun =>
        BlockingIssues.Count == 0 &&
        FoEntityDetails is not null &&
        !string.IsNullOrWhiteSpace(CreatePayloadJson) &&
        (CoverageGaps.Count == 0 || Configuration.AllowPartialEnumCoverage);
}

public sealed class TestifyEnumCoverageGap
{
    public TestifyEnumCoverageGap(string fieldName, string enumValue)
    {
        FieldName = fieldName;
        EnumValue = enumValue;
    }

    public string FieldName { get; }
    public string EnumValue { get; }
    public string Detail => $"{FieldName}={EnumValue}";
}

public sealed class TestifyFieldCoverageGap
{
    public TestifyFieldCoverageGap(string fieldName, IReadOnlyList<string> enumValues)
    {
        FieldName = fieldName;
        EnumValues = enumValues;
    }

    public string FieldName { get; }
    public IReadOnlyList<string> EnumValues { get; }
    public string Detail => $"{FieldName}: {string.Join(", ", EnumValues)}";
}

public sealed class TestifyLegPlan
{
    public TestifyLegPlan(string legId, string ceEntity, string ceFilter)
    {
        LegId = legId;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
    }

    public string LegId { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }
}

public sealed class TestifyEnumFieldPlan
{
    public TestifyEnumFieldPlan(
        string fieldName,
        string enumType,
        IReadOnlyList<string> enumMembers,
        IReadOnlySet<string> transformKeys,
        IReadOnlyList<string> missingMembers,
        string? fixedValue,
        bool parseFailed,
        string parseError)
    {
        FieldName = fieldName;
        EnumType = enumType;
        EnumMembers = enumMembers;
        TransformKeys = transformKeys;
        MissingMembers = missingMembers;
        FixedValue = fixedValue;
        ParseFailed = parseFailed;
        ParseError = parseError;
    }

    public string FieldName { get; }
    public string EnumType { get; }
    public IReadOnlyList<string> EnumMembers { get; }
    public IReadOnlySet<string> TransformKeys { get; }
    public IReadOnlyList<string> MissingMembers { get; }
    public string? FixedValue { get; }
    public bool ParseFailed { get; }
    public string ParseError { get; }
    public bool HasCoverageGap => MissingMembers.Count > 0;
    public string CoverageGapDetail => HasCoverageGap
        ? $"Unmapped enum members for field '{FieldName}': {string.Join(", ", MissingMembers.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(value => $"'{value}'"))}."
        : string.Empty;
}

public sealed class TestifyPatchStep
{
    public TestifyPatchStep(int stepNumber, IReadOnlyDictionary<string, string> enumValues)
    {
        StepNumber = stepNumber;
        EnumValues = enumValues;
    }

    public int StepNumber { get; }
    public IReadOnlyDictionary<string, string> EnumValues { get; }
}

public sealed class TestifyPreflightRow
{
    public TestifyPreflightRow(
        string mapDisplayName,
        string mapId,
        string foEntity,
        int enumFields,
        int plannedUpdates,
        bool isReady,
        string status,
        string blockingIssue,
        IReadOnlyList<TestifyEnumCoverageGap> coverageGaps)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        FoEntity = foEntity;
        EnumFields = enumFields;
        PlannedUpdates = plannedUpdates;
        IsReady = isReady;
        Status = status;
        BlockingIssue = blockingIssue;
        CoverageGaps = coverageGaps;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string FoEntity { get; }
    public int EnumFields { get; }
    public int PlannedUpdates { get; }
    public bool IsReady { get; }
    public string Status { get; }
    public string BlockingIssue { get; }
    public IReadOnlyList<TestifyEnumCoverageGap> CoverageGaps { get; }
    public IReadOnlyList<TestifyFieldCoverageGap> CoverageGapsByField => CoverageGaps
        .GroupBy(g => g.FieldName, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.First().FieldName, StringComparer.OrdinalIgnoreCase)
        .Select(group => new TestifyFieldCoverageGap(
            group.First().FieldName,
            group.Select(g => g.EnumValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
        .ToArray();
    public string CoverageGapDetail => CoverageGaps.Count == 0
        ? string.Empty
        : string.Join("; ", CoverageGaps.Select(g => g.Detail));
    public string CoverageGapFieldDetail => CoverageGapsByField.Count == 0
        ? string.Empty
        : string.Join("; ", CoverageGapsByField.Select(g => g.Detail));
}

public sealed class TestifyExecutionLogRow
{
    public TestifyExecutionLogRow(DateTimeOffset timestampUtc, string mapDisplayName, string phase, string status, string detail)
    {
        TimestampUtc = timestampUtc;
        MapDisplayName = mapDisplayName;
        Phase = phase;
        Status = status;
        Detail = detail;
    }

    public DateTimeOffset TimestampUtc { get; }
    public string MapDisplayName { get; }
    public string Phase { get; }
    public string Status { get; }
    public string Detail { get; }
    public string TimestampDisplay => TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
}

public sealed class TestifyResultRow
{
    public TestifyResultRow(
        string mapDisplayName,
        string mapId,
        bool valid,
        bool createSucceeded,
        int patchesPlanned,
        int patchesSucceeded,
        bool ceVerificationSucceeded,
        string status,
        IReadOnlyList<TestifyEnumCoverageGap> coverageGaps)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        Valid = valid;
        CreateSucceeded = createSucceeded;
        PatchesPlanned = patchesPlanned;
        PatchesSucceeded = patchesSucceeded;
        CeVerificationSucceeded = ceVerificationSucceeded;
        Status = status;
        CoverageGaps = coverageGaps;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public bool Valid { get; }
    public bool CreateSucceeded { get; }
    public int PatchesPlanned { get; }
    public int PatchesSucceeded { get; }
    public bool CeVerificationSucceeded { get; }
    public string Status { get; }
    public IReadOnlyList<TestifyEnumCoverageGap> CoverageGaps { get; }
    public IReadOnlyList<TestifyFieldCoverageGap> CoverageGapsByField => CoverageGaps
        .GroupBy(g => g.FieldName, StringComparer.OrdinalIgnoreCase)
        .OrderBy(group => group.First().FieldName, StringComparer.OrdinalIgnoreCase)
        .Select(group => new TestifyFieldCoverageGap(
            group.First().FieldName,
            group.Select(g => g.EnumValue)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()))
        .ToArray();
    public string CoverageGapDetail => CoverageGaps.Count == 0
        ? string.Empty
        : string.Join("; ", CoverageGaps.Select(g => g.Detail));
    public string CoverageGapFieldDetail => CoverageGapsByField.Count == 0
        ? string.Empty
        : string.Join("; ", CoverageGapsByField.Select(g => g.Detail));
}
