using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthServiceTests
{
    private sealed class FakeTokenProvider : ITokenProvider
    {
        private readonly string _token;
        private readonly int _failures;
        private int _callCount;
        public TokenRequest? LastRequest { get; private set; }

        public FakeTokenProvider(string token, int failures = 0)
        {
            _token = token;
            _failures = failures;
        }

        public Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            _callCount++;
            if (_callCount <= _failures)
            {
                throw new InvalidOperationException("Transient");
            }

            return Task.FromResult(_token);
        }
    }

    private sealed class FailingTokenProvider : ITokenProvider
    {
        private readonly Exception _failure;

        public FailingTokenProvider(Exception failure)
        {
            _failure = failure;
        }

        public Task<string> GetTokenAsync(TokenRequest request, CancellationToken cancellationToken = default)
        {
            throw _failure;
        }
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Uses_TokenProvider()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-123");
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(env, sp);
        Assert.Equal("token-123", token);
        Assert.Equal("https://example.operations.dynamics.com/.default", provider.LastRequest?.Scope);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Retries_On_Failure()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-456", failures: 1);
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(env, sp);
        Assert.Equal("token-456", token);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Overload_Uses_Provided_Resource_BaseUrl()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FakeTokenProvider("token-789");
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp);
        Assert.Equal("token-789", token);
        Assert.Equal("https://org.crm.dynamics.com/.default", provider.LastRequest?.Scope);
        Assert.Equal("tenant", provider.LastRequest?.TenantId);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_Uses_Environment_TenantId_For_Authority_Resolution()
    {
        var env = new FoEnvironment("id", "name", "https://example.operations.dynamics.com", "tenant-guid-or-domain", null);
        var sp = new ServicePrincipal("sp", env.Id, "client", AuthMode.ClientSecret, "secretRef", null);

        var provider = new FakeTokenProvider("token-123");
        var svc = new AuthService(provider);

        _ = await svc.AcquireTokenAsync(env, sp);

        Assert.Equal(env.TenantId, provider.LastRequest?.TenantId);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_InvalidGrant_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalServiceException("invalid_grant", "refresh token expired"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Finance and Operations", recovery => prompted = recovery);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("switch to Profiles", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Finance and Operations", failure.ServiceName);
        Assert.Equal("Finance and Operations sign-in required", failure.PromptTitle);
        Assert.True(failure.RequiresInteractiveReauth);
        Assert.Same(failure, prompted);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_UiRequired_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalUiRequiredException("user_null", "user interaction required"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Dataverse", recovery => prompted = recovery);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("interactive sign-in", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Dataverse", failure.ServiceName);
        Assert.IsType<MsalUiRequiredException>(failure.InnerException);
        Assert.True(failure.RequiresInteractiveReauth);
        Assert.Same(failure, prompted);
    }

    [Trait("Category", "Auth")]
    [Fact]
    public async Task AcquireToken_ExpiredRefreshTokenMessage_Throws_Reauth_Message()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalServiceException("temporarily_unavailable", "The refresh token has expired and user interaction is required."));
        var svc = new AuthService(provider, "Finance and Operations");

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));
        Assert.Contains("interactive sign-in", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.IsType<MsalServiceException>(failure.InnerException);
    }

    [Trait("Category", "AuthFallback")]
    [Fact]
    public async Task AuthFallback_InvalidGrant_TriggersInteractiveRecoveryEvent()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalServiceException("invalid_grant", "refresh token expired"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Finance and Operations", ex => prompted = ex);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));

        Assert.NotNull(prompted);
        Assert.Same(failure, prompted);
        Assert.True(failure.RequiresInteractiveReauth);
        Assert.IsType<MsalServiceException>(failure.InnerException);
    }

    [Trait("Category", "AuthFallback")]
    [Fact]
    public async Task AuthFallback_UiRequired_RaisesRecoveryEventBeforeReturningFailure()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FailingTokenProvider(new MsalUiRequiredException("user_null", "user interaction required"));
        AuthRecoveryException? prompted = null;
        var svc = new AuthService(provider, "Dataverse", ex => prompted = ex);

        var failure = await Assert.ThrowsAsync<AuthRecoveryException>(() => svc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));

        Assert.NotNull(prompted);
        Assert.Same(failure, prompted);
        Assert.IsType<MsalUiRequiredException>(failure.InnerException);
    }

    [Trait("Category", "AuthFallback")]
    [Fact]
    public async Task AuthFallback_SuccessfulReauthAfterRecovery_AllowsOperationToProceed()
    {
        // Simulate the silent path failing once, then after the host completes interactive
        // re-auth the operation retries against a fresh provider that succeeds.
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var failing = new FailingTokenProvider(new MsalUiRequiredException("user_null", "user interaction required"));
        var failingSvc = new AuthService(failing, "Finance and Operations");

        await Assert.ThrowsAsync<AuthRecoveryException>(() =>
            failingSvc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp));

        var freshProvider = new FakeTokenProvider(BuildJwtWithTid("tenant"));
        var freshSvc = new AuthService(freshProvider, "Finance and Operations");
        var token = await freshSvc.AcquireTokenAsync("https://org.crm.dynamics.com", "tenant", sp);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public async Task TenantValidation_MatchingTokenTid_AllowsTokenReturn()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FakeTokenProvider(BuildJwtWithTid("11111111-1111-1111-1111-111111111111"));
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(
            "https://org.crm.dynamics.com",
            "11111111-1111-1111-1111-111111111111",
            sp);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public async Task TenantValidation_MismatchedTokenTid_ThrowsNamedErrorBeforeApiCall()
    {
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FakeTokenProvider(BuildJwtWithTid("22222222-2222-2222-2222-222222222222"));
        var svc = new AuthService(provider);

        var ex = await Assert.ThrowsAsync<TenantMismatchException>(() =>
            svc.AcquireTokenAsync(
                "https://org.crm.dynamics.com",
                "11111111-1111-1111-1111-111111111111",
                sp));

        Assert.Equal("11111111-1111-1111-1111-111111111111", ex.ExpectedTenantId);
        Assert.Equal("22222222-2222-2222-2222-222222222222", ex.ActualTenantId);
        Assert.Contains("11111111", ex.Message, StringComparison.Ordinal);
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public void TenantValidation_DirectValidator_IgnoresMissingExpectedTenant()
    {
        // When the environment has no configured TenantId we cannot detect a misroute —
        // do not throw; defer to other checks. This keeps test-only scenarios working
        // without papering over real environment configuration gaps.
        AuthService.ValidateTokenTenant(BuildJwtWithTid("aaaa"), string.Empty);
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public void TenantValidation_DirectValidator_IgnoresUnparseableToken()
    {
        // Opaque/non-JWT tokens are tolerated (e.g. AAD v1 tokens, test fixtures).
        // The provider-level tests already cover the misroute path with a real JWT.
        AuthService.ValidateTokenTenant("not-a-jwt", "tenant-x");
    }

    // -----------------------------------------------------------------------
    // #168 low: a domain-form / meta tenant used to fail AFTER a successful sign-in
    // -----------------------------------------------------------------------

    [Trait("Category", "TenantValidation")]
    [Theory]
    [InlineData("contoso.onmicrosoft.com")]
    [InlineData("CONTOSO.ONMICROSOFT.COM")]
    [InlineData("contoso.com")]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    public void TenantValidation_NonGuidTenantForm_IsAcceptedAgainstAGuidTid(string configuredTenant)
    {
        // The `tid` claim is always a GUID, so comparing it to any of these forms could never match:
        // sign-in succeeded and this validator then rejected it, telling the user to fix a tenant that
        // is not wrong. The authority is built from the configured tenant, so the STS already enforced it.
        AuthService.ValidateTokenTenant(
            BuildJwtWithTid("11111111-1111-1111-1111-111111111111"),
            configuredTenant);
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public async Task TenantValidation_DomainFormTenant_CompletesAcquisitionInsteadOfThrowing()
    {
        // End-to-end through the acquire path: the token carries a real GUID tid and the profile carries
        // the domain form. This is the exact shape that used to throw a non-retryable TenantMismatch.
        var sp = new ServicePrincipal("sp", "env", "client", AuthMode.ClientSecret, "secretRef", null);
        var provider = new FakeTokenProvider(BuildJwtWithTid("11111111-1111-1111-1111-111111111111"));
        var svc = new AuthService(provider);

        var token = await svc.AcquireTokenAsync(
            "https://org.crm.dynamics.com",
            "contoso.onmicrosoft.com",
            sp);

        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Trait("Category", "TenantValidation")]
    [Theory]
    [InlineData("{11111111-1111-1111-1111-111111111111}")]
    [InlineData("11111111111111111111111111111111")]
    public void TenantValidation_GuidShapedTenant_StillDetectsAMisroute(string configuredTenant)
    {
        // Braced / dashless GUIDs are still GUID-shaped, so the strict check must stay on for them —
        // relaxing the domain forms must not accidentally relax every non-canonical spelling.
        Assert.Throws<TenantMismatchException>(() => AuthService.ValidateTokenTenant(
            BuildJwtWithTid("22222222-2222-2222-2222-222222222222"),
            configuredTenant));
    }

    [Trait("Category", "TenantValidation")]
    [Theory]
    [InlineData("11111111111111111111111111111111")]          // "N" — dashless
    [InlineData("{11111111-1111-1111-1111-111111111111}")]    // "B" — braced
    [InlineData("(11111111-1111-1111-1111-111111111111)")]    // "P" — parenthesised
    [InlineData("11111111-1111-1111-1111-111111111111")]      // "D" — canonical
    [InlineData("11111111-1111-1111-1111-111111111111 ")]     // stray trailing space (Guid.Parse trims)
    [InlineData("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE")]      // upper-case hex (already tolerated; pinned)
    public void TenantValidation_SameTenantInAnyGuidSpelling_IsNotAMismatch(string configuredTenant)
    {
        // A `tid` claim is always the canonical dashed form. Every spelling above parses to the SAME
        // GUID, so comparing strings reported the tenant as a cross-tenant misroute against itself —
        // the identical "fails after a successful sign-in" failure, one layer narrower than the
        // domain-form case. Value comparison is what makes these equivalent.
        var canonicalTid = Guid.Parse(configuredTenant).ToString("D");

        AuthService.ValidateTokenTenant(BuildJwtWithTid(canonicalTid), configuredTenant);
    }

    [Trait("Category", "TenantValidation")]
    [Fact]
    public void TenantValidation_GuidTenant_AgainstANonGuidTid_StaysStrict()
    {
        // AAD always issues a GUID tid, so this shape is not real — but a tenant we know precisely
        // versus a claim we cannot interpret must not be waved through on the value-comparison path.
        Assert.Throws<TenantMismatchException>(() => AuthService.ValidateTokenTenant(
            BuildJwtWithTid("not-a-guid"),
            "11111111-1111-1111-1111-111111111111"));
    }

    private static string BuildJwtWithTid(string tid)
    {
        var header = Base64UrlEncode(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        var payloadObj = new Dictionary<string, object?>
        {
            ["aud"] = "https://org.crm.dynamics.com",
            ["tid"] = tid,
            ["iss"] = $"https://sts.windows.net/{tid}/"
        };
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payloadObj)));
        return $"{header}.{payload}.signature";
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
