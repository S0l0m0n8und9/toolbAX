using System;
using System.Collections.Generic;
using System.Linq;
using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteMapComparerTests
{
    private static DualWriteMap Map(string name, string version, string state) =>
        new($"id-{name}-{version}", name, name, $"pid-{name}", state,
            new DualWriteTemplate($"t-{version}", version, "MS"), Array.Empty<DualWriteTemplate>());

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
}
