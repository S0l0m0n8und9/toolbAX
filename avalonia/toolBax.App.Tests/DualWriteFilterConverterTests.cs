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

    [Fact]
    public void Whitespace_inside_a_literal_is_preserved()
    {
        // A double space inside the literal is data, not operator padding: collapsing it silently changes
        // the row set F&O returns (200 OK, count 0) and corrupts what the Legs grid shows.
        Assert.Equal("Name eq 'ACME  Corp'", DualWriteFilterConverter.XppToOData("Name == \"ACME  Corp\""));
        Assert.Equal("Name eq 'ACME  Corp'", DualWriteFilterConverter.XppToOData("Name == 'ACME  Corp'"));
    }

    [Fact]
    public void Tabs_and_newlines_inside_a_literal_are_preserved()
    {
        Assert.Equal("Note eq 'a\tb\nc'", DualWriteFilterConverter.XppToOData("Note == \"a\tb\nc\""));
    }

    [Fact]
    public void Leading_and_trailing_whitespace_inside_a_literal_is_preserved()
    {
        Assert.Equal("Name eq '  padded  '", DualWriteFilterConverter.XppToOData("  Name ==   \"  padded  \"  "));
    }

    [Fact]
    public void Whitespace_outside_literals_still_collapses_around_preserved_literals()
    {
        Assert.Equal("A eq 'x  y' and B eq 2",
            DualWriteFilterConverter.XppToOData("A == \"x  y\"\n\t&&   B == 2"));
    }

    [Fact]
    public void Doubled_quotes_inside_a_literal_do_not_confuse_the_literal_tracking()
    {
        // The escaped quote must not read as the end of the literal — otherwise the whitespace after it
        // would be treated as literal content (or the following operators as literal text).
        Assert.Equal("Name eq 'O''Brien  Jr' and B eq 2",
            DualWriteFilterConverter.XppToOData("Name == \"O'Brien  Jr\"  &&  B == 2"));

        // Literal ending in a quote: the trailing '' is escaped and the *next* quote closes the literal.
        Assert.Equal("Name eq 'abc''' and B eq 2",
            DualWriteFilterConverter.XppToOData("Name == \"abc'\"  &&  B == 2"));

        // A literal that is nothing but a quote.
        Assert.Equal("Name eq '''' and B eq 2",
            DualWriteFilterConverter.XppToOData("Name == \"'\"  &&  B == 2"));
    }

    [Theory]
    [InlineData("!(A == 1)", "not (A eq 1)")]
    [InlineData("!(A == 1) && B == 2", "not (A eq 1) and B eq 2")]
    [InlineData("!Blocked", "not Blocked")]
    [InlineData("! Blocked", "not Blocked")]
    [InlineData("A == 1 && !Blocked", "A eq 1 and not Blocked")]
    public void A_bare_bang_becomes_not(string input, string expected) =>
        Assert.Equal(expected, DualWriteFilterConverter.XppToOData(input));

    [Theory]
    [InlineData("A != 1", "A ne 1")]
    [InlineData("A!=1", "A ne 1")]
    [InlineData("A != 1 && !B", "A ne 1 and not B")]
    public void Bang_equals_is_still_ne(string input, string expected) =>
        Assert.Equal(expected, DualWriteFilterConverter.XppToOData(input));

    [Fact]
    public void A_bang_inside_a_literal_is_not_translated()
    {
        Assert.Equal("Note eq '!a != b'", DualWriteFilterConverter.XppToOData("Note == \"!a != b\""));
    }
}
