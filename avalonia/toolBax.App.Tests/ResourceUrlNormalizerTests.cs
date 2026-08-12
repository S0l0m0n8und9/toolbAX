using FoToolbox.Core.Auth;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Covers the URL normalization that determines the OAuth token audience/scope and the request base for
/// every F&amp;O / Dataverse call. Previously only exercised transitively via the consuming services.
/// </summary>
public class ResourceUrlNormalizerTests
{
    [Theory]
    [InlineData("https://contoso.operations.dynamics.com", "https://contoso.operations.dynamics.com")]
    [InlineData("https://contoso.operations.dynamics.com/", "https://contoso.operations.dynamics.com")]
    [InlineData("https://contoso.operations.dynamics.com/data", "https://contoso.operations.dynamics.com")]
    [InlineData("https://contoso.operations.dynamics.com/data/", "https://contoso.operations.dynamics.com")]
    public void NormalizeFoBaseUrl_strips_trailing_slash_and_data_suffix(string input, string expected)
        => Assert.Equal(expected, ResourceUrlNormalizer.NormalizeFoBaseUrl(input));

    [Theory]
    [InlineData("https://contoso.crm.dynamics.com", "https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/", "https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/api/data", "https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2", "https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/api/data/v9.1/", "https://contoso.crm.dynamics.com")]
    public void NormalizeDataverseResourceBaseUrl_strips_api_data_and_version_suffix(string input, string expected)
        => Assert.Equal(expected, ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(input));

    [Theory]
    [InlineData("https://contoso.crm.dynamics.com")]
    [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2")]
    public void BuildDataverseApiBaseUrl_appends_a_single_v9_2_suffix(string input)
        => Assert.Equal("https://contoso.crm.dynamics.com/api/data/v9.2",
            ResourceUrlNormalizer.BuildDataverseApiBaseUrl(input));

    // -----------------------------------------------------------------------
    // #168 low: a scheme-less URL (the shape the Profiles placeholders show) broke the token scope
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("contoso.crm.dynamics.com")]
    [InlineData("contoso.crm.dynamics.com/")]
    [InlineData("contoso.crm.dynamics.com/api/data/v9.2")]
    [InlineData("  contoso.crm.dynamics.com  ")]
    public void NormalizeDataverseResourceBaseUrl_defaults_a_scheme_less_host_to_https(string input)
        // Without the scheme the scope became "contoso.crm.dynamics.com/.default", which AAD rejects
        // with "resource principal not found" — surfaced raw, with nothing pointing at the URL.
        => Assert.Equal("https://contoso.crm.dynamics.com",
            ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(input));

    [Theory]
    [InlineData("contoso.operations.dynamics.com")]
    [InlineData("contoso.operations.dynamics.com/data")]
    public void NormalizeFoBaseUrl_defaults_a_scheme_less_host_to_https(string input)
        // Symmetry: F&O accepted the scheme-less form only because every consumer repaired it locally.
        => Assert.Equal("https://contoso.operations.dynamics.com",
            ResourceUrlNormalizer.NormalizeFoBaseUrl(input));

    [Fact]
    public void Scheme_less_input_produces_a_parseable_absolute_dataverse_api_base()
        => Assert.Equal("https://contoso.crm.dynamics.com/api/data/v9.2",
            ResourceUrlNormalizer.BuildDataverseApiBaseUrl("contoso.crm.dynamics.com"));

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("https://contoso.crm.dynamics.com")]
    public void An_explicit_scheme_is_left_alone(string input)
        // Notably http:// must survive — an on-prem/proxied host is not silently upgraded here.
        => Assert.Equal(input, ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_unset_url_stays_empty_rather_than_becoming_a_bare_scheme(string? input)
    {
        Assert.Equal(string.Empty, ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(input!));
        Assert.Equal(string.Empty, ResourceUrlNormalizer.NormalizeFoBaseUrl(input!));
    }
}
