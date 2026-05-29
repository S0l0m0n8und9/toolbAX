using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Core.DualWrite;

/// <summary>How a single map differs (or not) between two environments.</summary>
public enum DualWriteComparisonVerdict
{
    Identical,
    OnlyInLeft,
    OnlyInRight,
    VersionMismatch,
    StateMismatch
}

/// <summary>One row of an environment comparison, keyed by map name.</summary>
public sealed record DualWriteMapComparisonRow(
    string MapName,
    bool InLeft,
    bool InRight,
    string LeftVersion,
    string RightVersion,
    string LeftState,
    string RightState,
    DualWriteComparisonVerdict Verdict)
{
    public bool IsDifference => Verdict != DualWriteComparisonVerdict.Identical;
}

/// <summary>
/// Pure client-side diff of two environments' dual-write maps. Mirrors the read-only
/// comparison the MS tool performs (<c>DWComparison.runComparison</c>): match maps by name
/// across the two environments and classify each by presence, active version, and state.
/// </summary>
public static class DualWriteMapComparer
{
    public static IReadOnlyList<DualWriteMapComparisonRow> Compare(
        IReadOnlyList<DualWriteMap> left,
        IReadOnlyList<DualWriteMap> right)
    {
        var leftByName = Index(left);
        var rightByName = Index(right);

        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        names.UnionWith(leftByName.Keys);
        names.UnionWith(rightByName.Keys);

        var rows = new List<DualWriteMapComparisonRow>();
        foreach (var name in names)
        {
            var hasLeft = leftByName.TryGetValue(name, out var l);
            var hasRight = rightByName.TryGetValue(name, out var r);

            var leftVersion = hasLeft ? l!.CurrentVersion : string.Empty;
            var rightVersion = hasRight ? r!.CurrentVersion : string.Empty;
            var leftState = hasLeft ? l!.State : string.Empty;
            var rightState = hasRight ? r!.State : string.Empty;

            var verdict = Classify(hasLeft, hasRight, leftVersion, rightVersion, leftState, rightState);
            rows.Add(new DualWriteMapComparisonRow(
                name, hasLeft, hasRight, leftVersion, rightVersion, leftState, rightState, verdict));
        }

        return rows;
    }

    private static DualWriteComparisonVerdict Classify(
        bool hasLeft, bool hasRight,
        string leftVersion, string rightVersion,
        string leftState, string rightState)
    {
        if (hasLeft && !hasRight)
        {
            return DualWriteComparisonVerdict.OnlyInLeft;
        }

        if (!hasLeft && hasRight)
        {
            return DualWriteComparisonVerdict.OnlyInRight;
        }

        if (!string.Equals(leftVersion, rightVersion, StringComparison.OrdinalIgnoreCase))
        {
            return DualWriteComparisonVerdict.VersionMismatch;
        }

        if (!string.Equals(leftState, rightState, StringComparison.OrdinalIgnoreCase))
        {
            return DualWriteComparisonVerdict.StateMismatch;
        }

        return DualWriteComparisonVerdict.Identical;
    }

    private static Dictionary<string, DualWriteMap> Index(IReadOnlyList<DualWriteMap> maps)
    {
        var dict = new Dictionary<string, DualWriteMap>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
        {
            var key = string.IsNullOrWhiteSpace(map.Name) ? map.DisplayName : map.Name;
            if (!string.IsNullOrWhiteSpace(key))
            {
                dict[key] = map;
            }
        }

        return dict;
    }
}
