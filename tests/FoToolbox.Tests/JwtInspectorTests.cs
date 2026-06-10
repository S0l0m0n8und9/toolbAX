using FoToolbox.Core.Auth;
using System;
using Xunit;

namespace FoToolbox.Tests;

public class JwtInspectorTests
{
    private static string CreateJwt(DateTimeOffset expiresUtc, string? tenantId = null)
    {
        static string B64Url(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64Url("{\"alg\":\"none\"}");
        var tid = tenantId is null ? "" : $",\"tid\":\"{tenantId}\"";
        var payload = B64Url($"{{\"exp\":{expiresUtc.ToUnixTimeSeconds()}{tid}}}");
        return $"{header}.{payload}.sig";
    }

    private static string MakeJwtWithRawPayload(string rawPayloadJson)
    {
        static string B64Url(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64Url("{\"alg\":\"none\"}");
        var payload = B64Url(rawPayloadJson);
        return $"{header}.{payload}.sig";
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetTenantId_Reads_Tid_Claim()
    {
        var jwt = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "11111111-2222-3333-4444-555555555555");
        Assert.True(JwtInspector.TryGetTenantId(jwt, out var tid));
        Assert.Equal("11111111-2222-3333-4444-555555555555", tid);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetTenantId_False_When_No_Tid()
    {
        var jwt = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        Assert.False(JwtInspector.TryGetTenantId(jwt, out _));
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetExpiryUtc_Reads_Exp_Claim()
    {
        var expires = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds());
        var jwt = CreateJwt(expires);
        Assert.True(JwtInspector.TryGetExpiryUtc(jwt, out var exp));
        Assert.Equal(expires, exp);
    }

    [Trait("Category", "Auth")]
    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("only.one")]
    public void TryGet_Handles_Garbage(string input)
    {
        Assert.False(JwtInspector.TryGetTenantId(input, out _));
        Assert.False(JwtInspector.TryGetExpiryUtc(input, out _));
    }

    [Trait("Category", "Auth")]
    [Theory]
    [InlineData("Bearer abc.def.ghi", "abc.def.ghi")]
    [InlineData("  bearer abc ", "abc")]
    [InlineData("abc\r\ndef", "abcdef")]
    [InlineData("plain", "plain")]
    public void Normalize_Strips_Prefix_And_Whitespace(string input, string expected)
    {
        Assert.Equal(expected, BearerTokenText.Normalize(input));
    }

    // Fix 1a: non-object JSON root (e.g. numeric literal) must not throw
    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetTenantId_NonObjectJsonRoot_ReturnsFalse_DoesNotThrow()
    {
        // payload segment is base64url of "123" — valid JSON but not an object
        var jwt = MakeJwtWithRawPayload("123");
        Assert.False(JwtInspector.TryGetTenantId(jwt, out _));
    }

    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetExpiryUtc_NonObjectJsonRoot_ReturnsFalse_DoesNotThrow()
    {
        // payload segment is base64url of "123" — valid JSON but not an object
        var jwt = MakeJwtWithRawPayload("123");
        Assert.False(JwtInspector.TryGetExpiryUtc(jwt, out _));
    }

    // Fix 1b: out-of-range exp (milliseconds-style value) must not throw
    [Trait("Category", "Auth")]
    [Fact]
    public void TryGetExpiryUtc_OutOfRangeExp_ReturnsFalse_DoesNotThrow()
    {
        // 1_780_000_000_000 is milliseconds-style — way outside the valid Unix seconds range
        var jwt = MakeJwtWithRawPayload("{\"exp\":1780000000000}");
        Assert.False(JwtInspector.TryGetExpiryUtc(jwt, out _));
    }
}
