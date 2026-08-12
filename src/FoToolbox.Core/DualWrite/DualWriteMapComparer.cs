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
    StateMismatch,

    /// <summary>
    /// The row could not be paired with confidence, so no version/state verdict was reached: either two
    /// maps in one environment share the same identity (name + CE target), or a map carries no usable
    /// identity at all. Reported instead of a fabricated diff — see
    /// <see cref="DualWriteMapComparisonRow.Note"/> for which case it is. Appended last so the existing
    /// members keep their values (the summary chips order by them).
    /// </summary>
    Ambiguous
}

/// <summary>One row of an environment comparison, keyed by map name + CE (right-side) target.</summary>
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

    /// <summary>
    /// Why this row could not be compared, for the rows that carry
    /// <see cref="DualWriteComparisonVerdict.Ambiguous"/>. Empty for every ordinary row.
    /// </summary>
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// Pure client-side diff of two environments' dual-write maps. Mirrors the read-only
/// comparison the MS tool performs (<c>DWComparison.runComparison</c>): match maps across the two
/// environments and classify each by presence, active version, and state.
/// </summary>
/// <remarks>
/// Maps are matched on <b>name + CE target</b>, not on name alone (#160). The F&amp;O entity name repeats
/// across CE targets — <c>CustCustomerV3Entity</c> maps to both <c>accounts</c> and <c>contacts</c> — so a
/// name-only index silently collapsed those two maps into one: one disappeared from the diff entirely and
/// the survivor was compared against the wrong counterpart, fabricating a version mismatch (or a clean
/// "identical") that does not exist. Where a pairing still cannot be made — duplicate name + target inside
/// one environment, or a map with neither — every map is emitted as its own
/// <see cref="DualWriteComparisonVerdict.Ambiguous"/> row rather than overwritten or dropped.
/// </remarks>
public static class DualWriteMapComparer
{
    /// <summary>Label for a map that carries no name and no CE target.</summary>
    private const string UnnamedLabel = "(unnamed map)";

    /// <summary>Shown in place of an empty CE target when a label has to be disambiguated.</summary>
    private const string NoTargetLabel = "(no CE target)";

    /// <summary>
    /// Joins the two identity parts. NUL cannot appear in a gateway-supplied name or entity name, so
    /// ("a", "b|c") and ("a|b", "c") can never collide the way a printable separator would allow.
    /// </summary>
    private const string KeySeparator = "\0";

    private static readonly IComparer<MapIdentity> ByNameThenTarget = Comparer<MapIdentity>.Create((a, b) =>
    {
        var byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        return byName != 0 ? byName : string.Compare(a.Target, b.Target, StringComparison.OrdinalIgnoreCase);
    });

    public static IReadOnlyList<DualWriteMapComparisonRow> Compare(
        IReadOnlyList<DualWriteMap> left,
        IReadOnlyList<DualWriteMap> right)
    {
        var leftIndex = Index(left);
        var rightIndex = Index(right);

        // Union of the composite keys, ordered by name then CE target so the grid still reads
        // alphabetically by map name. Left identities win a case-only tie (e.g. "customers" vs
        // "Customers"), matching the previous name-only behaviour.
        var identities = leftIndex.Keyed.Values.Select(g => g.Identity)
            .Concat(rightIndex.Keyed.Values.Select(g => g.Identity)
                .Where(i => !leftIndex.Keyed.ContainsKey(i.Key)))
            .ToList();
        identities.Sort(ByNameThenTarget);

        // A name that appears under more than one CE target has to show the target in the grid, or the
        // two rows the composite key just rescued would be indistinguishable from each other.
        var sharedNames = identities
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<DualWriteMapComparisonRow>();
        foreach (var identity in identities)
        {
            var label = Label(identity, sharedNames.Contains(identity.Name));
            leftIndex.Keyed.TryGetValue(identity.Key, out var leftGroup);
            rightIndex.Keyed.TryGetValue(identity.Key, out var rightGroup);

            var leftMaps = leftGroup?.Maps;
            var rightMaps = rightGroup?.Maps;

            // More than one map per side under one identity: no pairing is defensible, so list them all.
            if (leftMaps?.Count > 1 || rightMaps?.Count > 1)
            {
                rows.AddRange(AmbiguousRows(label, leftMaps, rightMaps, DuplicateNote(leftMaps, rightMaps)));
                continue;
            }

            var l = leftMaps is { Count: > 0 } ? leftMaps[0] : null;
            var r = rightMaps is { Count: > 0 } ? rightMaps[0] : null;

            var leftVersion = l?.CurrentVersion ?? string.Empty;
            var rightVersion = r?.CurrentVersion ?? string.Empty;
            var leftState = l?.State ?? string.Empty;
            var rightState = r?.State ?? string.Empty;

            var verdict = Classify(l is not null, r is not null, leftVersion, rightVersion, leftState, rightState);
            rows.Add(new DualWriteMapComparisonRow(
                label, l is not null, r is not null, leftVersion, rightVersion, leftState, rightState, verdict));
        }

        // Unkeyable maps can't be matched to anything, but dropping them hides real configuration.
        rows.AddRange(AmbiguousRows(UnnamedLabel, leftIndex.Unkeyable, rightIndex.Unkeyable, UnnamedNote));

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

    /// <summary>
    /// One row per map, on the side it actually came from, with the opposite side's columns blank. The
    /// presence flags describe <i>this map</i>, not the identity — the point of the row is that we cannot
    /// say what it lines up with, so nothing is paired and no version/state verdict is invented.
    /// </summary>
    private static IEnumerable<DualWriteMapComparisonRow> AmbiguousRows(
        string label,
        IReadOnlyList<DualWriteMap>? leftMaps,
        IReadOnlyList<DualWriteMap>? rightMaps,
        string note)
    {
        foreach (var map in leftMaps ?? Array.Empty<DualWriteMap>())
        {
            yield return new DualWriteMapComparisonRow(
                label, true, false, map.CurrentVersion, string.Empty, map.State, string.Empty,
                DualWriteComparisonVerdict.Ambiguous) { Note = note };
        }

        foreach (var map in rightMaps ?? Array.Empty<DualWriteMap>())
        {
            yield return new DualWriteMapComparisonRow(
                label, false, true, string.Empty, map.CurrentVersion, string.Empty, map.State,
                DualWriteComparisonVerdict.Ambiguous) { Note = note };
        }
    }

    private static string DuplicateNote(IReadOnlyList<DualWriteMap>? leftMaps, IReadOnlyList<DualWriteMap>? rightMaps) =>
        $"{leftMaps?.Count ?? 0} map(s) in source and {rightMaps?.Count ?? 0} in target share this name and CE " +
        "target, so they cannot be paired. Each is listed on its own row and no version/state verdict was reached.";

    private const string UnnamedNote =
        "This map has neither a name nor a CE target, so it cannot be matched across environments. " +
        "It is listed so it is not silently dropped from the comparison.";

    private static string Label(MapIdentity identity, bool disambiguate)
    {
        if (identity.Name.Length == 0)
        {
            // Keyed on its CE target alone — still comparable, but it needs something to show.
            return $"{UnnamedLabel} → {identity.Target}";
        }

        if (!disambiguate)
        {
            return identity.Name;
        }

        return $"{identity.Name} → {(identity.Target.Length == 0 ? NoTargetLabel : identity.Target)}";
    }

    private static MapIndex Index(IReadOnlyList<DualWriteMap> maps)
    {
        var keyed = new Dictionary<string, MapGroup>(StringComparer.OrdinalIgnoreCase);
        var unkeyable = new List<DualWriteMap>();

        foreach (var map in maps)
        {
            var name = Usable(map.Name) ?? Usable(map.DisplayName) ?? string.Empty;
            var target = Usable(map.RightEntityName) ?? string.Empty;

            if (name.Length == 0 && target.Length == 0)
            {
                unkeyable.Add(map);
                continue;
            }

            var identity = new MapIdentity(name, target);
            if (!keyed.TryGetValue(identity.Key, out var group))
            {
                group = new MapGroup(identity);
                keyed[identity.Key] = group;
            }

            group.Maps.Add(map);
        }

        return new MapIndex(keyed, unkeyable);
    }

    private static string? Usable(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>A map's cross-environment identity: its name plus the CE entity it writes to.</summary>
    private sealed record MapIdentity(string Name, string Target)
    {
        public string Key { get; } = Name + KeySeparator + Target;
    }

    /// <summary>Every map in one environment that shares a single identity (normally exactly one).</summary>
    private sealed class MapGroup(MapIdentity identity)
    {
        public MapIdentity Identity { get; } = identity;

        public List<DualWriteMap> Maps { get; } = new();
    }

    /// <summary>One environment's maps, grouped by identity, plus the ones that have no identity at all.</summary>
    private sealed record MapIndex(Dictionary<string, MapGroup> Keyed, List<DualWriteMap> Unkeyable);
}
