using System;

namespace ToolBax.Core.Models;

/// <summary>How a map differs between two environments (Dual-Write Compare §5).</summary>
public enum DiffKind
{
    InSync,
    VersionDrift,
    StateDiffers,
    RowDelta,
    OnlyInSource,
    OnlyInTarget,
}

/// <summary>One environment's view of a map (state / template version / 24h rows).</summary>
public sealed record DiffSide(MapState State, string Version, long Rows);

/// <summary>A single compare row: a map and its source/target sides plus the diff verdict.</summary>
public sealed record CompareRow(
    string FoEntity,
    string DvEntity,
    DwDirection Direction,
    DiffSide? Source,
    DiffSide? Target,
    DiffKind Diff)
{
    public string MapDisplay => $"{FoEntity} {Direction.Arrow()} {DvEntity}";

    public string SourceState => Source?.State.ToString().ToLowerInvariant() ?? "absent";
    public string SourceVersion => Source is null ? "—" : $"v{Source.Version}";
    public string SourceRows => Source is null ? "—" : Source.Rows.ToString("N0");

    public string TargetState => Target?.State.ToString().ToLowerInvariant() ?? "absent";
    public string TargetVersion => Target is null ? "—" : $"v{Target.Version}";
    public string TargetRows => Target is null ? "—" : Target.Rows.ToString("N0");

    public string DiffLabel => DiffClassifier.Label(Diff);
}

/// <summary>Classifies a source/target pair into a <see cref="DiffKind"/> (row delta &gt; 200 rows).</summary>
public static class DiffClassifier
{
    public const long RowDeltaThreshold = 200;

    public static DiffKind Classify(DiffSide? source, DiffSide? target)
    {
        if (target is null)
        {
            return DiffKind.OnlyInSource;
        }

        if (source is null)
        {
            return DiffKind.OnlyInTarget;
        }

        if (source.Version != target.Version)
        {
            return DiffKind.VersionDrift;
        }

        if (source.State != target.State)
        {
            return DiffKind.StateDiffers;
        }

        return Math.Abs(source.Rows - target.Rows) > RowDeltaThreshold ? DiffKind.RowDelta : DiffKind.InSync;
    }

    public static string Label(DiffKind kind) => kind switch
    {
        DiffKind.InSync => "in sync",
        DiffKind.VersionDrift => "version drift",
        DiffKind.StateDiffers => "state differs",
        DiffKind.RowDelta => "row delta",
        DiffKind.OnlyInSource => "only in source",
        DiffKind.OnlyInTarget => "only in target",
        _ => kind.ToString(),
    };
}
