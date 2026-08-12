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
    /// The row could not be paired with confidence, so no version/state verdict was reached: two maps in
    /// one environment share the same identity (name + CE target), a map carries no usable identity at
    /// all, or the two gateways answered in different response shapes and the same-name maps cannot be
    /// lined up. Reported instead of a fabricated diff — see
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
/// <para>
/// Maps are matched on <b>name + CE target</b>, not on name alone (#160). The F&amp;O entity name repeats
/// across CE targets — <c>CustCustomerV3Entity</c> maps to both <c>accounts</c> and <c>contacts</c> — so a
/// name-only index silently collapsed those two maps into one: one disappeared from the diff entirely and
/// the survivor was compared against the wrong counterpart, fabricating a version mismatch (or a clean
/// "identical") that does not exist.
/// </para>
/// <para>
/// The CE target is not always reported, though: the two environments' gateways need not answer in the
/// same shape. <see cref="DualWriteResponseParser"/> reads the target from the nested <c>rightEntity</c>
/// block and falls back to the older flat shape, which has no such block, so the same logical map can
/// arrive as <c>(name, "")</c> from one environment and <c>(name, "accounts")</c> from the other. An
/// <b>empty CE target is therefore a degraded form of the same identity, not a different one</b>: exact
/// composite matches pair first, then an unpaired untargeted map pairs with an unpaired same-name map
/// where that pairing is unique in both directions, keeping the richer side's target for the row.
/// </para>
/// <para>
/// Where a pairing still cannot be made — duplicate name + target inside one environment, a map with
/// neither, or one untargeted map against several same-name targets — every map is emitted as its own
/// <see cref="DualWriteComparisonVerdict.Ambiguous"/> row rather than overwritten, dropped, or guessed at.
/// </para>
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

    /// <summary>The absent side of a pairing that only one environment contributed to.</summary>
    private static readonly IReadOnlyList<DualWriteMap> NoMaps = Array.Empty<DualWriteMap>();

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

        // Line the two sides up, then order by name then CE target so the grid still reads alphabetically
        // by map name. Left identities win a case-only tie (e.g. "customers" vs "Customers"), matching the
        // previous name-only behaviour.
        var units = Pair(leftIndex.Keyed, rightIndex.Keyed);
        units.Sort((a, b) => ByNameThenTarget.Compare(a.Identity, b.Identity));

        // A name that appears on more than one row has to show the CE target in the grid, or the rows the
        // composite key just rescued would be indistinguishable from each other.
        var sharedNames = units
            .GroupBy(u => u.Identity.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<DualWriteMapComparisonRow>();
        foreach (var unit in units)
        {
            var label = Label(unit.Identity, sharedNames.Contains(unit.Identity.Name));
            var leftMaps = unit.LeftMaps;
            var rightMaps = unit.RightMaps;

            // Same-name maps the two response shapes cannot be lined up between: listed, never guessed at.
            if (unit.AmbiguityNote is { } shapeNote)
            {
                rows.AddRange(AmbiguousRows(label, leftMaps, rightMaps, shapeNote));
                continue;
            }

            // More than one map per side under one identity: no pairing is defensible, so list them all.
            if (leftMaps.Count > 1 || rightMaps.Count > 1)
            {
                rows.AddRange(AmbiguousRows(label, leftMaps, rightMaps, DuplicateNote(leftMaps, rightMaps)));
                continue;
            }

            var l = leftMaps.Count > 0 ? leftMaps[0] : null;
            var r = rightMaps.Count > 0 ? rightMaps[0] : null;

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

    /// <summary>
    /// Lines the two environments' identity groups up into one comparison unit per row: exact composite
    /// matches first, then the degraded-identity pass that reconciles a gateway which reported no CE
    /// target against one that did.
    /// </summary>
    private static List<PairUnit> Pair(
        Dictionary<string, MapGroup> left,
        Dictionary<string, MapGroup> right)
    {
        var units = new List<PairUnit>();
        var leftOver = new List<MapGroup>();
        var pairedRight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Exact name + CE target. This pass stays first and unconditional: where both gateways report
        // targets, one name under many targets keeps one row per target (#160) and nothing below can
        // re-pair what it already matched.
        foreach (var group in left.Values)
        {
            if (right.TryGetValue(group.Identity.Key, out var match))
            {
                units.Add(new PairUnit(group.Identity, group.Maps, match.Maps));
                pairedRight.Add(match.Identity.Key);
            }
            else
            {
                leftOver.Add(group);
            }
        }

        var rightOver = right.Values.Where(g => !pairedRight.Contains(g.Identity.Key)).ToList();

        // Whatever is still unpaired, bucketed by name: only a same-name pair can be the one logical map
        // seen through two response shapes.
        var byName = new Dictionary<string, NameBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in leftOver)
        {
            Bucket(byName, group).Left.Add(group);
        }

        foreach (var group in rightOver)
        {
            Bucket(byName, group).Right.Add(group);
        }

        foreach (var bucket in byName.Values)
        {
            // At most one group per side can carry an empty target (identities are unique within a side),
            // and never one on each — that is an exact key match, already paired above. A map with neither
            // a name nor a target never reaches here: it is unkeyable.
            var untargetedLeft = bucket.Left.FirstOrDefault(g => g.Identity.Target.Length == 0);
            var untargetedRight = bucket.Right.FirstOrDefault(g => g.Identity.Target.Length == 0);
            var fromLeft = untargetedLeft is not null;
            var untargeted = untargetedLeft ?? untargetedRight;

            // Candidates for the degraded pairing: the opposite side's same-name maps, which all carry a
            // target (an untargeted one there would have matched exactly).
            var candidates = fromLeft ? bucket.Right : bucket.Left;

            if (untargeted is null
                || (untargetedLeft is not null && untargetedRight is not null)
                || candidates.Count == 0)
            {
                // No shape difference to reconcile under this name, or the untargeted map has no same-name
                // counterpart at all — so it really is present on one side only.
                units.AddRange(bucket.Left.Select(g => OneSided(g, isLeft: true)));
                units.AddRange(bucket.Right.Select(g => OneSided(g, isLeft: false)));
                continue;
            }

            if (candidates.Count == 1)
            {
                // Unique in both directions — one untargeted map, one same-name counterpart — so pair them
                // and keep the richer side's identity, which is the one that knows the CE target.
                var only = candidates[0];
                units.Add(new PairUnit(
                    only.Identity,
                    fromLeft ? untargeted.Maps : only.Maps,
                    fromLeft ? only.Maps : untargeted.Maps));
            }
            else
            {
                // Several same-name targets on the other side: which one the untargeted map is cannot be
                // known, and guessing is the fabricated verdict #160 set out to stop.
                var note = ShapeMismatchNote(fromLeft, candidates.Count);
                units.Add(OneSided(untargeted, fromLeft) with { AmbiguityNote = note });
                units.AddRange(candidates.Select(c => OneSided(c, !fromLeft) with { AmbiguityNote = note }));
            }

            // Same-name maps that do carry a CE target on the untargeted side are untouched by the shape
            // difference: they keep their own identity and simply have no counterpart.
            foreach (var group in fromLeft ? bucket.Left : bucket.Right)
            {
                if (!ReferenceEquals(group, untargeted))
                {
                    units.Add(OneSided(group, fromLeft));
                }
            }
        }

        return units;
    }

    private static NameBucket Bucket(Dictionary<string, NameBucket> byName, MapGroup group)
    {
        if (!byName.TryGetValue(group.Identity.Name, out var bucket))
        {
            bucket = new NameBucket();
            byName[group.Identity.Name] = bucket;
        }

        return bucket;
    }

    private static PairUnit OneSided(MapGroup group, bool isLeft) => isLeft
        ? new PairUnit(group.Identity, group.Maps, NoMaps)
        : new PairUnit(group.Identity, NoMaps, group.Maps);

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
        IReadOnlyList<DualWriteMap> leftMaps,
        IReadOnlyList<DualWriteMap> rightMaps,
        string note)
    {
        foreach (var map in leftMaps)
        {
            yield return new DualWriteMapComparisonRow(
                label, true, false, map.CurrentVersion, string.Empty, map.State, string.Empty,
                DualWriteComparisonVerdict.Ambiguous) { Note = note };
        }

        foreach (var map in rightMaps)
        {
            yield return new DualWriteMapComparisonRow(
                label, false, true, string.Empty, map.CurrentVersion, string.Empty, map.State,
                DualWriteComparisonVerdict.Ambiguous) { Note = note };
        }
    }

    private static string DuplicateNote(IReadOnlyList<DualWriteMap> leftMaps, IReadOnlyList<DualWriteMap> rightMaps) =>
        $"{leftMaps.Count} map(s) in source and {rightMaps.Count} in target share this name and CE " +
        "target, so they cannot be paired. Each is listed on its own row and no version/state verdict was reached.";

    /// <summary>
    /// The cross-shape case: one gateway reported this map name with no CE target while the other reported
    /// several maps under that name, so there is no way to tell which one it is.
    /// </summary>
    private static string ShapeMismatchNote(bool untargetedIsLeft, int candidateCount) =>
        $"The {(untargetedIsLeft ? "source" : "target")} gateway reported no CE target for this map name and " +
        $"the {(untargetedIsLeft ? "target" : "source")} has {candidateCount} map(s) with that name under " +
        "different CE targets, so the two response shapes cannot be lined up. Each map is listed on its own " +
        "row and no version/state verdict was reached.";

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

    /// <summary>
    /// One row's worth of pairing: the maps from each side lined up under a single identity, which is the
    /// one that carries a CE target whenever either side supplied one. <see cref="AmbiguityNote"/> is set
    /// only where the pairing itself could not be made, and forces the row to
    /// <see cref="DualWriteComparisonVerdict.Ambiguous"/>.
    /// </summary>
    private sealed record PairUnit(
        MapIdentity Identity,
        IReadOnlyList<DualWriteMap> LeftMaps,
        IReadOnlyList<DualWriteMap> RightMaps)
    {
        public string? AmbiguityNote { get; init; }
    }

    /// <summary>The still-unpaired groups that share one map name, per side.</summary>
    private sealed class NameBucket
    {
        public List<MapGroup> Left { get; } = new();

        public List<MapGroup> Right { get; } = new();
    }

    /// <summary>One environment's maps, grouped by identity, plus the ones that have no identity at all.</summary>
    private sealed record MapIndex(Dictionary<string, MapGroup> Keyed, List<DualWriteMap> Unkeyable);
}
