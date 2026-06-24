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
}
