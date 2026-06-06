using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="DualWriteFilterConverter.XppToOData"/> — the X++-to-OData translation used to
/// preview a dual-write map leg's source filter. Pure lexer, faithfully ported from the WPF plugin.
/// </summary>
public class DualWriteFilterConverterTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Empty_input_yields_empty(string? input, string expected) =>
        Assert.Equal(expected, DualWriteFilterConverter.XppToOData(input));

    [Fact]
    public void Translates_equality_and_logical_operators()
    {
        Assert.Equal("VendGroup eq 'DOM' and Blocked eq 0",
            DualWriteFilterConverter.XppToOData("VendGroup == \"DOM\" && Blocked == 0"));
    }

    [Fact]
    public void Translates_or_and_inequality()
    {
        Assert.Equal("A ne 1 or B eq 2",
            DualWriteFilterConverter.XppToOData("A != 1 || B == 2"));
    }

    [Fact]
    public void Translates_comparison_operators()
    {
        Assert.Equal("Amount ge 10 and Amount le 100 and X gt 1 and Y lt 2",
            DualWriteFilterConverter.XppToOData("Amount >= 10 && Amount <= 100 && X > 1 && Y < 2"));
    }

    [Fact]
    public void A_single_equals_becomes_eq()
    {
        Assert.Equal("Name eq 'abc'", DualWriteFilterConverter.XppToOData("Name = \"abc\""));
    }

    [Fact]
    public void Double_quotes_become_single_quotes()
    {
        Assert.Equal("Status eq 'Active'", DualWriteFilterConverter.XppToOData("Status == \"Active\""));
    }

    [Fact]
    public void Single_quotes_inside_a_string_are_doubled()
    {
        // O'Brien inside a double-quoted X++ literal → OData single-quoted with the quote doubled.
        Assert.Equal("Name eq 'O''Brien'", DualWriteFilterConverter.XppToOData("Name == \"O'Brien\""));
    }

    [Fact]
    public void Operators_inside_a_double_quoted_string_are_not_translated()
    {
        Assert.Equal("Note eq 'a && b == c'", DualWriteFilterConverter.XppToOData("Note == \"a && b == c\""));
    }

    [Fact]
    public void Operators_inside_a_single_quoted_string_are_not_translated()
    {
        // Source filters often already use OData-style single-quoted literals; operators inside them
        // must be left alone, and the literal preserved verbatim.
        Assert.Equal("Note eq 'a && b == c'", DualWriteFilterConverter.XppToOData("Note == 'a && b == c'"));
        Assert.Equal("VendGroup eq 'DOM'", DualWriteFilterConverter.XppToOData("VendGroup == 'DOM'"));
    }

    [Fact]
    public void Collapses_whitespace_and_newlines()
    {
        Assert.Equal("A eq 1 and B eq 2",
            DualWriteFilterConverter.XppToOData("A == 1\n\t&&   B == 2"));
    }
}
