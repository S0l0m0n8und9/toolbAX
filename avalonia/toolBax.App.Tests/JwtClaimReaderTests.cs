using System;
using System.Text;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="JwtClaimReader.ReadUsername"/> — reads a friendly account name from a delegated
/// access token's claims (for the interactive sign-in status). Pure; tolerant of malformed input.
/// </summary>
public class JwtClaimReaderTests
{
    // Builds a JWT (header.payload.signature) with the given payload JSON; segments are base64url.
    private static string Jwt(string payloadJson)
    {
        static string B64Url(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64Url("{\"alg\":\"none\"}")}.{B64Url(payloadJson)}.sig";
    }

    [Fact]
    public void Reads_preferred_username()
    {
        var token = Jwt("{\"preferred_username\":\"alice@contoso.com\",\"upn\":\"x\"}");
        Assert.Equal("alice@contoso.com", JwtClaimReader.ReadUsername(token));
    }

    [Fact]
    public void Falls_back_to_upn_then_email()
    {
        Assert.Equal("bob@contoso.com", JwtClaimReader.ReadUsername(Jwt("{\"upn\":\"bob@contoso.com\"}")));
        Assert.Equal("carol@contoso.com", JwtClaimReader.ReadUsername(Jwt("{\"email\":\"carol@contoso.com\"}")));
    }

    [Fact]
    public void Handles_base64url_padding()
    {
        // A payload whose base64 length isn't a multiple of 4 (needs padding restored).
        var token = Jwt("{\"preferred_username\":\"dave@x.io\"}");
        Assert.Equal("dave@x.io", JwtClaimReader.ReadUsername(token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]               // too few segments
    [InlineData("a.!!!.c")]           // payload not base64
    public void Returns_null_for_malformed_input(string? token) =>
        Assert.Null(JwtClaimReader.ReadUsername(token));

    [Fact]
    public void Returns_null_when_no_username_claim_present()
    {
        Assert.Null(JwtClaimReader.ReadUsername(Jwt("{\"sub\":\"123\",\"aud\":\"x\"}")));
    }
}
