using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Identity.Client;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class AuthBrokerTests
{
    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_AuthMode_RoundTrips_Through_ProfileStore()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();
        await svc.UpsertEnvironmentAsync(new FoEnvironment("env1", "Env", "https://contoso.operations.dynamics.com", "tenant", null));
        await svc.UpsertServicePrincipalAsync(new ServicePrincipal("sp1", "env1", "client-id", AuthMode.Interactive, null, null, AuthTarget.Fo));

        var loaded = await svc.GetServicePrincipalAsync("env1", AuthTarget.Fo);

        Assert.Equal(AuthMode.Interactive, loaded!.AuthMode);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public void MsalTokenProvider_Reuses_App_For_Same_Credential_And_Rebuilds_On_Rotation()
    {
        var provider = new FoToolbox.Core.Auth.MsalTokenProvider(
            "https://login.microsoftonline.com",
            (_, _) => Task.FromResult<FoToolbox.Core.Auth.ClientCredential>(new FoToolbox.Core.Auth.ClientSecretCredential("secret-1")));

        var sp = new ServicePrincipal("sp", "env", "client-id", AuthMode.ClientSecret, null, null);
        var authority = "https://login.microsoftonline.com/tenant";
        var cred1 = new FoToolbox.Core.Auth.ClientSecretCredential("secret-1");
        var cred2 = new FoToolbox.Core.Auth.ClientSecretCredential("secret-2");

        var app1 = provider.GetOrCreateApp(sp, authority, cred1);
        var app2 = provider.GetOrCreateApp(sp, authority, cred1);
        var app3 = provider.GetOrCreateApp(sp, authority, cred2);
        // different authority (tenant) → different app entry
        var app4 = provider.GetOrCreateApp(sp, "https://login.microsoftonline.com/other-tenant", cred1);

        Assert.Same(app1, app2);
        Assert.NotSame(app1, app3);
        Assert.NotSame(app1, app4);
    }

    // -----------------------------------------------------------------------
    // Task 5: AuthBroker tests
    // -----------------------------------------------------------------------

    private static string CreateJwt(DateTimeOffset expiresUtc, string? tenantId = null)
    {
        static string B64Url(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64Url("{\"alg\":\"none\"}");
        var tid = tenantId is null ? "" : $",\"tid\":\"{tenantId}\"";
        var payload = B64Url($"{{\"exp\":{expiresUtc.ToUnixTimeSeconds()}{tid}}}");
        return $"{header}.{payload}.sig";
    }

    private static async Task<SecretVaultService> NewVaultAsync()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();
        return new SecretVaultService(store.ConnectionString);
    }

    private sealed class FakeInteractiveProvider : IInteractiveTokenProvider
    {
        public InteractiveTokenRequest? LastRequest;
        public string Token = "";
        public Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new InteractiveTokenResult(Token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_Mode_Routes_To_Interactive_Provider_With_Sp_ClientId()
    {
        var fake = new FakeInteractiveProvider();
        var freshToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "tenant-1");
        fake.Token = freshToken;

        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault, interactiveProvider: fake);
        var sp = new ServicePrincipal("sp1", "env1", "public-client-id", AuthMode.Interactive, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        var token = await broker.AcquireTokenAsync(request);

        Assert.Equal(freshToken, token);
        Assert.NotNull(fake.LastRequest);
        Assert.Equal("public-client-id", fake.LastRequest!.ClientId);
        Assert.Equal("tenant-1", fake.LastRequest.TenantId);
        Assert.Equal("https://contoso.operations.dynamics.com", fake.LastRequest.ResourceBaseUrl);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_Mode_Rejects_Cross_Tenant_Token()
    {
        var fake = new FakeInteractiveProvider();
        // Token contains tid "other-tenant" but request expects "tenant-1"
        fake.Token = CreateJwt(DateTimeOffset.UtcNow.AddHours(1), "other-tenant");

        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault, interactiveProvider: fake);
        var sp = new ServicePrincipal("sp1", "env1", "public-client-id", AuthMode.Interactive, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        await Assert.ThrowsAsync<TenantMismatchException>(() => broker.AcquireTokenAsync(request));
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_Mode_Without_ClientId_Throws_Actionable_Message()
    {
        var fake = new FakeInteractiveProvider();
        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault, interactiveProvider: fake);
        var sp = new ServicePrincipal("sp1", "env1", "", AuthMode.Interactive, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.AcquireTokenAsync(request));
        Assert.Contains("client ID", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Profiles", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task BearerToken_Mode_Resolves_Pending_Token_First()
    {
        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault);
        var sp = new ServicePrincipal("sp1", "env1", "", AuthMode.BearerToken, null, null, AuthTarget.Fo);
        var fresh = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        // Pass as "Bearer <token>" to exercise normalization
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp,
            PendingBearerToken: $"Bearer {fresh}");

        var token = await broker.AcquireTokenAsync(request);

        // Prefix must be stripped
        Assert.Equal(fresh, token);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task BearerToken_Mode_Reads_Vault_Then_EnvVar_And_Rejects_Expired()
    {
        var vault = await NewVaultAsync();
        var expiredToken = CreateJwt(DateTimeOffset.UtcNow.AddHours(-1));
        var secretRef = await vault.StoreSecretAsync("bearer", new BearerTokenPayload { AccessToken = expiredToken });

        var broker = new AuthBroker(vault);
        var sp = new ServicePrincipal("sp1", "env1", "", AuthMode.BearerToken, secretRef, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.AcquireTokenAsync(request));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task BearerToken_Mode_Falls_Back_To_Target_Specific_EnvVar()
    {
        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault);
        var sp = new ServicePrincipal("sp1", "env1", "", AuthMode.BearerToken, null, null, AuthTarget.Dataverse);
        var fresh = CreateJwt(DateTimeOffset.UtcNow.AddHours(1));
        Environment.SetEnvironmentVariable("FOTB_CE_BEARER_TOKEN", fresh);
        try
        {
            var request = new AuthTokenRequest("https://contoso.crm.dynamics.com", "tenant-1", sp);
            var token = await broker.AcquireTokenAsync(request);
            Assert.Equal(fresh, token);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_CE_BEARER_TOKEN", null);
        }
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task ClientSecret_Mode_Without_Any_Credential_Throws_Actionable_Message()
    {
        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault);
        var sp = new ServicePrincipal("sp1", "env1", "client-id", AuthMode.ClientSecret, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        Environment.SetEnvironmentVariable("FOTB_CLIENT_SECRET", null);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.AcquireTokenAsync(request));
            Assert.Contains("FOTB_CLIENT_SECRET", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_CLIENT_SECRET", null);
        }
    }

    // -----------------------------------------------------------------------
    // Fix 1: AuthTokenRequest.ToString() must not leak secrets
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Auth")]
    public void AuthTokenRequest_ToString_Does_Not_Leak_Pending_Secrets()
    {
        var sp = new ServicePrincipal("sp1", "env1", "client-id", AuthMode.ClientSecret, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest(
            ResourceBaseUrl: "https://contoso.operations.dynamics.com",
            TenantId: "tenant-1",
            Principal: sp,
            ServiceName: "MySvc",
            PendingClientSecret: "s3cret-XYZ",
            PendingBearerToken: "tok-ABC");

        var text = request.ToString();

        Assert.DoesNotContain("s3cret-XYZ", text);
        Assert.DoesNotContain("tok-ABC", text);
        Assert.Contains("https://contoso.operations.dynamics.com", text);
    }

    // -----------------------------------------------------------------------
    // Fix 2: Certificate mode → honest actionable error
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Certificate_Mode_Without_Secret_Throws_Certificate_Specific_Message()
    {
        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault);
        var sp = new ServicePrincipal("sp1", "env1", "client-id", AuthMode.Certificate, null, "thumb-ABC", AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp);

        Environment.SetEnvironmentVariable("FOTB_CLIENT_SECRET", null);
        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => broker.AcquireTokenAsync(request));
            Assert.Contains("Certificate", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FOTB_CLIENT_SECRET", null);
        }
    }

    // -----------------------------------------------------------------------
    // Fix 3: ClientSecretCredential.ToString() must not leak the raw secret
    // -----------------------------------------------------------------------

    [Fact]
    [Trait("Category", "Auth")]
    public void ClientSecretCredential_ToString_Does_Not_Contain_Raw_Secret()
    {
        var cred = new FoToolbox.Core.Auth.ClientSecretCredential("s3cret-XYZ");

        var text = cred.ToString();

        Assert.DoesNotContain("s3cret-XYZ", text);
    }

    [Fact]
    [Trait("Category", "Auth")]
    public void ClientCertificateCredential_ToString_Prints_Thumbprint_Only()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=Test",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));
        var cred = new FoToolbox.Core.Auth.ClientCertificateCredential(cert);

        var text = cred.ToString();

        // Must contain the thumbprint (available from the cert)
        Assert.Contains(cert.Thumbprint, text, StringComparison.OrdinalIgnoreCase);
        // Must not spill private key material or the full cert subject/raw bytes
        Assert.DoesNotContain("BEGIN CERTIFICATE", text, StringComparison.OrdinalIgnoreCase);
    }

    // -----------------------------------------------------------------------
    // Fix 4: MSAL errors from interactive arm wrapped as AuthRecoveryException
    // -----------------------------------------------------------------------

    private sealed class ThrowingInteractiveProvider : IInteractiveTokenProvider
    {
        private readonly Exception _toThrow;
        public ThrowingInteractiveProvider(Exception toThrow) => _toThrow = toThrow;
        public Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default)
            => throw _toThrow;
    }

    [Fact]
    [Trait("Category", "Auth")]
    public async Task Interactive_MsalClientException_Wraps_As_AuthRecoveryException()
    {
        var msalEx = new MsalClientException("authentication_canceled", "User canceled authentication.");
        var fake = new ThrowingInteractiveProvider(msalEx);

        var vault = await NewVaultAsync();
        var broker = new AuthBroker(vault, interactiveProvider: fake);
        var sp = new ServicePrincipal("sp1", "env1", "public-client-id", AuthMode.Interactive, null, null, AuthTarget.Fo);
        var request = new AuthTokenRequest("https://contoso.operations.dynamics.com", "tenant-1", sp, "MySvc");

        var ex = await Assert.ThrowsAsync<AuthRecoveryException>(() => broker.AcquireTokenAsync(request));
        Assert.True(ex.RequiresInteractiveReauth);
        Assert.IsType<MsalClientException>(ex.InnerException);
    }
}
