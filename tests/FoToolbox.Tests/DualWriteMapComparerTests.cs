using System;
using System.Collections.Generic;
using System.Linq;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteMapComparerTests
{
    private static DualWriteMap Map(string name, string version, string state, string ceEntity = "") =>
        Named(name, name, version, state, ceEntity);

    private static DualWriteMap Named(string name, string displayName, string version, string state, string ceEntity = "") =>
        new($"id-{name}-{ceEntity}-{version}", name, displayName, $"pid-{name}", state,
            new DualWriteTemplate($"t-{version}", version, "MS"), Array.Empty<DualWriteTemplate>())
        {
            RightEntityName = ceEntity,
        };

    private static DualWriteMapComparisonRow Row(IReadOnlyList<DualWriteMapComparisonRow> rows, string name) =>
        rows.Single(r => r.MapName == name);

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_IdenticalMaps_AreIdentical()
    {
        var left = new[] { Map("Customers", "1.0", "Running") };
        var right = new[] { Map("Customers", "1.0", "Running") };

        var rows = DualWriteMapComparer.Compare(left, right);

        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "Customers").Verdict);
        Assert.False(Row(rows, "Customers").IsDifference);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_DetectsPresenceDifferences()
    {
        var left = new[] { Map("OnlyLeft", "1.0", "Running") };
        var right = new[] { Map("OnlyRight", "1.0", "Running") };

        var rows = DualWriteMapComparer.Compare(left, right);

        Assert.Equal(DualWriteComparisonVerdict.OnlyInLeft, Row(rows, "OnlyLeft").Verdict);
        Assert.Equal(DualWriteComparisonVerdict.OnlyInRight, Row(rows, "OnlyRight").Verdict);
        Assert.True(Row(rows, "OnlyLeft").InLeft);
        Assert.False(Row(rows, "OnlyLeft").InRight);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_DetectsVersionMismatch()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers", "1.0", "Running") },
            new[] { Map("Customers", "2.0", "Running") });

        var row = Row(rows, "Customers");
        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, row.Verdict);
        Assert.Equal("1.0", row.LeftVersion);
        Assert.Equal("2.0", row.RightVersion);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_VersionMatchButStateDiffers_IsStateMismatch()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers", "1.0", "Running") },
            new[] { Map("Customers", "1.0", "Stopped") });

        Assert.Equal(DualWriteComparisonVerdict.StateMismatch, Row(rows, "Customers").Verdict);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_MatchesByNameCaseInsensitively_AndSortsRows()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("customers", "1.0", "Running"), Map("Vendors", "1.0", "Running") },
            new[] { Map("Customers", "1.0", "Running") });

        Assert.Equal(2, rows.Count);
        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "customers").Verdict);
        Assert.Equal(DualWriteComparisonVerdict.OnlyInLeft, Row(rows, "Vendors").Verdict);
        Assert.Equal(new[] { "customers", "Vendors" }, rows.Select(r => r.MapName).ToArray());
    }

    // ── #160: map identity is name + CE target, not name alone. The F&O entity name repeats across CE
    // targets (CustCustomerV3Entity maps to both accounts and contacts), and the old name-only index
    // collapsed those maps last-wins: one vanished from the diff and the survivor was judged against the
    // wrong counterpart, fabricating a mismatch — or a clean "identical" — that was never true. ────────

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_OneFoNameTwoCeTargets_ComparesEachAgainstItsOwnCounterpart()
    {
        var left = new[]
        {
            Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
            Map("CustCustomerV3Entity", "2.0", "Running", "contacts"),
        };
        var right = new[]
        {
            Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
            Map("CustCustomerV3Entity", "3.0", "Running", "contacts"),
        };

        var rows = DualWriteMapComparer.Compare(left, right);

        // Both maps survive (name-only keying yielded a single row) and the CE target is shown so the two
        // rows are told apart, ordered by name then target.
        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new[] { "CustCustomerV3Entity → accounts", "CustCustomerV3Entity → contacts" },
            rows.Select(r => r.MapName).ToArray());

        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "CustCustomerV3Entity → accounts").Verdict);

        var contacts = Row(rows, "CustCustomerV3Entity → contacts");
        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, contacts.Verdict);
        Assert.Equal("2.0", contacts.LeftVersion);
        Assert.Equal("3.0", contacts.RightVersion);

        // A clean pairing carries no note — the note is reserved for rows that could not be compared.
        Assert.All(rows, r => Assert.Equal(string.Empty, r.Note));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_DriftUnderOneCeTarget_IsNotMaskedByTheOther()
    {
        // The fabricated-identical case: name-only keying kept whichever map came last, so the accounts
        // version drift disappeared entirely and the comparison read as in sync.
        var rows = DualWriteMapComparer.Compare(
            new[]
            {
                Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
                Map("CustCustomerV3Entity", "1.0", "Running", "contacts"),
            },
            new[]
            {
                Map("CustCustomerV3Entity", "2.0", "Running", "accounts"),
                Map("CustCustomerV3Entity", "1.0", "Running", "contacts"),
            });

        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, Row(rows, "CustCustomerV3Entity → accounts").Verdict);
        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "CustCustomerV3Entity → contacts").Verdict);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_MapMissingForOneCeTargetOnly_IsReportedAsMissing()
    {
        var rows = DualWriteMapComparer.Compare(
            new[]
            {
                Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
                Map("CustCustomerV3Entity", "1.0", "Running", "contacts"),
            },
            new[] { Map("CustCustomerV3Entity", "1.0", "Running", "accounts") });

        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "CustCustomerV3Entity → accounts").Verdict);

        var contacts = Row(rows, "CustCustomerV3Entity → contacts");
        Assert.Equal(DualWriteComparisonVerdict.OnlyInLeft, contacts.Verdict);
        Assert.True(contacts.InLeft);
        Assert.False(contacts.InRight);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_UniqueName_KeepsItsPlainLabelEvenWithACeTarget()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers V3", "1.0", "Running", "accounts") },
            new[] { Map("Customers V3", "1.0", "Running", "accounts") });

        Assert.Equal("Customers V3", Assert.Single(rows).MapName);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_ANameSplitBetweenACeTargetAndNone_LabelsBothRows()
    {
        var rows = DualWriteMapComparer.Compare(
            new[]
            {
                Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
                Map("CustCustomerV3Entity", "1.0", "Running"),
            },
            Array.Empty<DualWriteMap>());

        Assert.Equal(
            new[] { "CustCustomerV3Entity → (no CE target)", "CustCustomerV3Entity → accounts" },
            rows.Select(r => r.MapName).ToArray());
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_UsesDisplayNameWhenTheNameIsBlank()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Named(string.Empty, "Customers V3", "1.0", "Running", "accounts") },
            new[] { Named("   ", "Customers V3", "2.0", "Running", "accounts") });

        var row = Assert.Single(rows);
        Assert.Equal("Customers V3", row.MapName);
        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, row.Verdict);
    }

    // ── #160: where a pairing still cannot be made, say so instead of overwriting or dropping. ────────

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_DuplicateNameAndCeTarget_FlagsEveryMapInsteadOfOverwriting()
    {
        var left = new[]
        {
            Map("Customers V3", "1.0", "Running", "accounts"),
            Map("Customers V3", "2.0", "Paused", "accounts"),
        };
        var right = new[] { Map("Customers V3", "1.0", "Running", "accounts") };

        var rows = DualWriteMapComparer.Compare(left, right);

        // All three maps are on the grid (nothing overwritten) and none of them claims a verdict.
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal("Customers V3", r.MapName));
        Assert.All(rows, r => Assert.Equal(DualWriteComparisonVerdict.Ambiguous, r.Verdict));
        Assert.All(rows, r => Assert.True(r.IsDifference));
        Assert.All(rows, r => Assert.Contains("2 map(s) in source and 1 in target", r.Note));

        // Each row shows its own map's version/state, on its own side only — nothing is paired.
        Assert.Equal(new[] { "1.0", "2.0" }, rows.Where(r => r.InLeft).Select(r => r.LeftVersion).ToArray());
        Assert.Equal(new[] { "Running", "Paused" }, rows.Where(r => r.InLeft).Select(r => r.LeftState).ToArray());
        Assert.Equal(new[] { "1.0" }, rows.Where(r => r.InRight).Select(r => r.RightVersion).ToArray());
        Assert.All(rows, r => Assert.NotEqual(r.InLeft, r.InRight));
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_MapWithNoNameAndNoCeTarget_IsListedNotDropped()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers V3", "1.0", "Running"), Map(string.Empty, "1.0", "Running") },
            new[] { Map("Customers V3", "1.0", "Running") });

        // The unnamed map used to be skipped outright, so the diff silently under-reported the source.
        Assert.Equal(2, rows.Count);
        Assert.Equal(DualWriteComparisonVerdict.Identical, Row(rows, "Customers V3").Verdict);

        var unnamed = Row(rows, "(unnamed map)");
        Assert.Equal(DualWriteComparisonVerdict.Ambiguous, unnamed.Verdict);
        Assert.True(unnamed.InLeft);
        Assert.False(unnamed.InRight);
        Assert.Equal("1.0", unnamed.LeftVersion);
        Assert.Contains("cannot be matched across environments", unnamed.Note);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_UnnamedMapsOnBothSides_AreListedSeparatelyNotPaired()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map(string.Empty, "1.0", "Running") },
            new[] { Map("   ", "2.0", "Stopped") });

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("(unnamed map)", r.MapName));
        Assert.All(rows, r => Assert.Equal(DualWriteComparisonVerdict.Ambiguous, r.Verdict));
        Assert.Contains(rows, r => r.InLeft && r.LeftVersion == "1.0" && r.RightVersion.Length == 0);
        Assert.Contains(rows, r => r.InRight && r.RightVersion == "2.0" && r.LeftVersion.Length == 0);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_MapWithNoNameButACeTarget_IsStillMatchedOnThatTarget()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map(string.Empty, "1.0", "Running", "accounts") },
            new[] { Map(string.Empty, "2.0", "Running", "accounts") });

        var row = Assert.Single(rows);
        Assert.Equal("(unnamed map) → accounts", row.MapName);
        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, row.Verdict);
        Assert.Equal(string.Empty, row.Note);
    }

    // ── #160 follow-up: the two environments' gateways need not answer in the same shape. The flat
    // response carries the map name with no rightEntity block, so its CE target arrives empty, while the
    // nested response supplies it. An empty target is therefore a *degraded* form of the same identity,
    // not a different one — keying on it unconditionally split one logical map into OnlyInLeft +
    // OnlyInRight across a cross-version pair of environments. ─────────────────────────────────────────

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_SameMapUnderDifferentGatewayShapes_IsPairedNotSplit()
    {
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers V3", "1.0", "Running") },
            new[] { Map("Customers V3", "2.0", "Running", "accounts") });

        // One row with a real verdict: the drift is what the user came to see. Splitting it into
        // "only in source" + "only in target" reported two phantom maps and hid the version mismatch.
        var row = Assert.Single(rows);
        Assert.Equal(DualWriteComparisonVerdict.VersionMismatch, row.Verdict);
        Assert.True(row.InLeft);
        Assert.True(row.InRight);
        Assert.Equal("1.0", row.LeftVersion);
        Assert.Equal("2.0", row.RightVersion);

        // The name is unique on the grid, so it needs no disambiguating suffix, and a clean pairing
        // carries no note.
        Assert.Equal("Customers V3", row.MapName);
        Assert.Equal(string.Empty, row.Note);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_ShapeDifferenceTheOtherWayRound_IsAlsoPaired()
    {
        // Same scenario mirrored: the nested shape is the source and the flat one is the target.
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("Customers V3", "1.0", "Running", "accounts") },
            new[] { Map("Customers V3", "1.0", "Stopped") });

        var row = Assert.Single(rows);
        Assert.Equal(DualWriteComparisonVerdict.StateMismatch, row.Verdict);
        Assert.Equal("Running", row.LeftState);
        Assert.Equal("Stopped", row.RightState);
        Assert.Equal(string.Empty, row.Note);
    }

    [Trait("Category", "DualWrite")]
    [Fact]
    public void Compare_UntargetedMapAgainstTwoSameNameTargets_IsAmbiguousNotGuessed()
    {
        // Degraded identity only pairs where the pairing is unique. Here the flat side reports one
        // CustCustomerV3Entity with no target and the nested side reports two, so which one it is cannot
        // be known — and guessing is exactly the fabricated-verdict bug #160 fixed.
        var rows = DualWriteMapComparer.Compare(
            new[] { Map("CustCustomerV3Entity", "1.0", "Running") },
            new[]
            {
                Map("CustCustomerV3Entity", "1.0", "Running", "accounts"),
                Map("CustCustomerV3Entity", "2.0", "Running", "contacts"),
            });

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Equal(DualWriteComparisonVerdict.Ambiguous, r.Verdict));

        // Every map keeps its own identity on its own row, on the side it came from — nothing is paired.
        Assert.Equal(
            new[]
            {
                "CustCustomerV3Entity → (no CE target)",
                "CustCustomerV3Entity → accounts",
                "CustCustomerV3Entity → contacts",
            },
            rows.Select(r => r.MapName).ToArray());
        Assert.All(rows, r => Assert.NotEqual(r.InLeft, r.InRight));
        Assert.Equal(new[] { "1.0" }, rows.Where(r => r.InLeft).Select(r => r.LeftVersion).ToArray());
        Assert.Equal(new[] { "1.0", "2.0" }, rows.Where(r => r.InRight).Select(r => r.RightVersion).ToArray());

        // The note has to name the cause, or the rows look like the duplicate-identity case.
        Assert.All(rows, r => Assert.Contains("no CE target", r.Note));
        Assert.All(rows, r => Assert.Contains("2 map(s) with that name", r.Note));
    }
}
