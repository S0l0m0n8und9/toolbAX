using System;

namespace FoToolbox.Core.Auth;

/// <summary>
/// Raised when the tenant id (`tid`) embedded in an acquired access token does not match
/// the tenant id configured on the calling environment profile. The token is rejected
/// before any downstream API call so cross-tenant misroutes never reach plugin operations.
/// </summary>
public sealed class TenantMismatchException : InvalidOperationException
{
    public TenantMismatchException(string expectedTenantId, string actualTenantId)
        : base(BuildMessage(expectedTenantId, actualTenantId))
    {
        ExpectedTenantId = expectedTenantId ?? string.Empty;
        ActualTenantId = actualTenantId ?? string.Empty;
    }

    public string ExpectedTenantId { get; }
    public string ActualTenantId { get; }

    private static string BuildMessage(string expected, string actual)
    {
        var expectedDisplay = string.IsNullOrWhiteSpace(expected) ? "<none>" : expected;
        var actualDisplay = string.IsNullOrWhiteSpace(actual) ? "<none>" : actual;
        return $"Token was issued by tenant '{actualDisplay}' but this environment is configured for tenant '{expectedDisplay}'. " +
               "Sign in with an account from the expected tenant, or update the environment's TenantId before retrying.";
    }
}
