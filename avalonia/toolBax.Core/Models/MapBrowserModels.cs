using System;
using System.Collections.Generic;
using System.Linq;

namespace ToolBax.Core.Models;

/// <summary>A field binding row in a dual-write map (Map Browser §4, Bindings tab).</summary>
public sealed record DwBinding(
    int Ordinal,
    string FoField,
    string DvField,
    string Transform,
    bool Required,
    bool IsKey,
    bool Skip)
{
    /// <summary>Two-digit ordinal, e.g. "01".</summary>
    public string OrdinalLabel => Ordinal.ToString("D2");

    /// <summary>"none" transforms render muted; this flags the non-trivial ones.</summary>
    public bool HasTransform => !string.Equals(Transform, "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>Flow cell: "skip" for excluded fields, otherwise a mapping glyph.</summary>
    public string FlowGlyph => Skip ? "skip" : "↦";

    /// <summary>Compact flags cell, e.g. "required key".</summary>
    public string Flags => string.Join(" ", new[]
    {
        Required ? "required" : null,
        IsKey ? "key" : null,
    }.Where(f => f is not null));
}

/// <summary>One F&amp;O → Dataverse value-map entry.</summary>
public sealed record DwValueMapEntry(string From, string To);

/// <summary>A named value/enum map with a sample of its entries (Map Browser §4, Value maps tab).</summary>
public sealed record DwValueMap(string Name, IReadOnlyList<DwValueMapEntry> Entries, int TotalSize)
{
    /// <summary>How many entries are not shown in the sample ("+N more").</summary>
    public int MoreCount => Math.Max(0, TotalSize - Entries.Count);

    public bool HasMore => MoreCount > 0;

    public string MoreLabel => $"+{MoreCount} more";
}

/// <summary>A dual-write map in the Map Browser master list.</summary>
public sealed record DwMapSummary(
    string Id,
    string FoEntity,
    string DvEntity,
    string Version,
    DwDirection Direction,
    MapState State,
    long Rows24h,
    int Errors24h,
    string LastRun)
{
    public string DirectionArrow => Direction switch
    {
        DwDirection.Both => "↔",
        DwDirection.FoToDv => "→",
        DwDirection.DvToFo => "←",
        _ => "·",
    };

    public string StateText => State.ToString().ToLowerInvariant();

    public string VersionLabel => $"v{Version}";

    public bool HasErrors => Errors24h > 0;
}

/// <summary>
/// The cached "template" detail for a map: KPIs, 24h activity series, field bindings, and value maps.
/// Run history + errors are loaded separately (live endpoints) — see <see cref="DwRun"/>/<see cref="DwError"/>.
/// </summary>
public sealed record DwMapDetail(
    DwMapSummary Summary,
    string LatencyP95,
    IReadOnlyList<double> Activity,
    IReadOnlyList<DwBinding> Bindings,
    IReadOnlyList<DwValueMap> ValueMaps);

/// <summary>Outcome of a sync run (Map Browser §4, Runs tab).</summary>
public enum DwRunResult
{
    Ok,
    Partial,
    Failed,
}

/// <summary>One run-history entry for a map (Map Browser §4, Runs tab).</summary>
public sealed record DwRun(
    string Time,
    string Trigger,
    bool InitialSync,
    long Rows,
    long Ok,
    long Failed,
    string Duration,
    DwRunResult Result)
{
    public string ResultText => Result.ToString().ToLowerInvariant();
}

/// <summary>Severity of a dual-write error (Map Browser §4, Errors tab).</summary>
public enum DwErrorSeverity
{
    Error,
    Warning,
}

/// <summary>One error/dead-letter entry for a map (Map Browser §4, Errors tab).</summary>
public sealed record DwError(
    DwErrorSeverity Severity,
    string Message,
    string Timestamp,
    string Code,
    string Key,
    string Field)
{
    /// <summary>The mono detail line: "ts · code · key · field".</summary>
    public string MetaLine => $"{Timestamp} · {Code} · {Key} · {Field}";
}
