using FoToolbox.Core.Auth;
using Microsoft.Identity.Client;
using System;
using Xunit;

namespace FoToolbox.Tests;

public sealed class InteractiveSignInErrorTests
{
    [Fact]
    public void Describe_RedirectUriMismatch_ExplainsLoopbackRedirect()
    {
        var ex = new MsalServiceException("invalid_request", "AADSTS50011: The redirect URI 'http://localhost' specified in the request does not match.");

        var message = InteractiveSignInError.Describe(ex);

        Assert.NotNull(message);
        Assert.Contains("http://localhost", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redirect", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_UnauthorizedClient_ExplainsPublicClientRequirement()
    {
        var ex = new MsalServiceException("unauthorized_client", "The client does not exist or is not enabled for consumers.");

        var message = InteractiveSignInError.Describe(ex);

        Assert.NotNull(message);
        Assert.Contains("public client", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_ConfidentialClientAssertionRequired_ExplainsPublicClientRequirement()
    {
        // AADSTS7000218 = request must contain client_assertion/client_secret => app is registered
        // as confidential, not as a public client.
        var ex = new MsalServiceException("invalid_client", "AADSTS7000218: The request body must contain the following parameter: 'client_assertion' or 'client_secret'.");

        var message = InteractiveSignInError.Describe(ex);

        Assert.NotNull(message);
        Assert.Contains("public client", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_GenericInvalidClient_ReturnsNull()
    {
        // invalid_client covers many unrelated problems (expired secret, disabled app, cert
        // mismatch). Without the specific AADSTS7000218 signal we must not steer the user to
        // "enable public client flows".
        var ex = new MsalServiceException("invalid_client", "The client credential keys are expired.");

        Assert.Null(InteractiveSignInError.Describe(ex));
    }

    [Fact]
    public void Describe_UnrelatedException_ReturnsNull()
    {
        Assert.Null(InteractiveSignInError.Describe(new InvalidOperationException("something else")));
    }
}
