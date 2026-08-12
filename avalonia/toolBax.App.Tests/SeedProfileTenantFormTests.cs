using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using FoToolbox.Core.Auth;
using ToolBax.App.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// #168 low: the tenant a profile carries is often not a GUID — the app's own seed profiles use the
/// domain form. A token's <c>tid</c> claim always is a GUID, so the post-acquisition tenant check
/// compared unlike things and rejected a sign-in that had already succeeded, with a non-retryable
/// "wrong tenant" error naming a tenant that was not wrong. These tests pin the seed's shape to the
/// validator so the two cannot drift apart again.
/// </summary>
public class SeedProfileTenantFormTests
{
    private const string RealTenantGuid = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Every_seeded_profile_tenant_validates_against_a_guid_tid_token()
    {
        var seed = FakeProfileStore.Seed();
        Assert.NotEmpty(seed);

        foreach (var profile in seed)
        {
            // Guard the premise: if the seed ever switches to GUID tenants this test stops covering the
            // domain-form case and should be revisited rather than passing vacuously.
            Assert.False(Guid.TryParse(profile.Tenant, out _),
                $"Seed profile '{profile.Id}' no longer uses a non-GUID tenant form.");

            // The assertion: this must not throw. Before the fix every one of these threw
            // TenantMismatchException immediately after a successful sign-in.
            AuthService.ValidateTokenTenant(JwtWithTid(RealTenantGuid), profile.Tenant);
        }
    }

    [Fact]
    public void A_guid_tenant_on_a_profile_still_gets_the_strict_cross_tenant_check()
    {
        // The tolerance is scoped to forms that cannot be compared — it must not disable misroute
        // detection for a profile that does carry a tenant GUID.
        Assert.Throws<TenantMismatchException>(() => AuthService.ValidateTokenTenant(
            JwtWithTid("22222222-2222-2222-2222-222222222222"),
            RealTenantGuid));
    }

    private static string JwtWithTid(string tid)
    {
        static string B64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payload = B64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
            new Dictionary<string, object?> { ["tid"] = tid, ["iss"] = $"https://sts.windows.net/{tid}/" })));
        return $"{header}.{payload}.signature";
    }
}
