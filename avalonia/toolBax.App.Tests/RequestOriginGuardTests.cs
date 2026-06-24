using System;
using FoToolbox.Core.Net;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Pins the origin check that stops an environment-scoped bearer token from being sent to a foreign
/// origin via a server-supplied @odata.nextLink. Scheme + host + port must all match.
/// </summary>
public class RequestOriginGuardTests
{
    [Theory]
    [InlineData("https://contoso.crm.dynamics.com", "https://contoso.crm.dynamics.com/api/data/v9.2/x")]
    [InlineData("contoso.crm.dynamics.com", "https://contoso.crm.dynamics.com/api/data/v9.2/x")] // bare host ⇒ https
    [InlineData("https://contoso.crm.dynamics.com/api/data", "https://contoso.crm.dynamics.com/anything")] // path ignored
    [InlineData("https://contoso.crm.dynamics.com:443", "https://contoso.crm.dynamics.com/x")] // explicit default port
    public void Same_origin_is_allowed(string expected, string candidate)
        => Assert.True(RequestOriginGuard.IsSameOrigin(expected, new Uri(candidate)));

    [Theory]
    [InlineData("https://contoso.crm.dynamics.com", "https://evil.example.com/x")]              // different host
    [InlineData("https://contoso.crm.dynamics.com", "http://contoso.crm.dynamics.com/x")]       // scheme downgrade
    [InlineData("https://contoso.crm.dynamics.com", "https://contoso.crm.dynamics.com:8443/x")] // alternate port
    public void Different_origin_is_refused(string expected, string candidate)
        => Assert.False(RequestOriginGuard.IsSameOrigin(expected, new Uri(candidate)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_expected_base_is_refused(string? expected)
        => Assert.False(RequestOriginGuard.IsSameOrigin(expected, new Uri("https://contoso.crm.dynamics.com/x")));

    [Fact]
    public void Null_candidate_is_refused()
        => Assert.False(RequestOriginGuard.IsSameOrigin("https://contoso.crm.dynamics.com", null));
}
