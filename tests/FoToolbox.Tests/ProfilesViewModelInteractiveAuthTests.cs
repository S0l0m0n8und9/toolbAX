using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Host.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public sealed class ProfilesViewModelInteractiveAuthTests
{
    private sealed class FakeInteractiveTokenProvider : IInteractiveTokenProvider
    {
        public int Calls { get; private set; }
        public InteractiveTokenRequest? LastRequest { get; private set; }
        public string Token { get; set; } = "header.payload.signature";

        public Task<InteractiveTokenResult> AcquireTokenAsync(InteractiveTokenRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(new InteractiveTokenResult(Token, DateTimeOffset.UtcNow.AddHours(1)));
        }
    }

    private static ProfileItem BuildProfile(string envId, AuthMode foAuthMode, string clientId, string baseUrl, string tenantId)
    {
        var env = new FoEnvironment(envId, "Test Env", baseUrl, tenantId, null);
        var ceEnv = new DataverseEnvironment(envId, string.Empty, string.Empty);
        var foSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, clientId, foAuthMode, null, null, AuthTarget.Fo);
        var ceSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Dataverse);
        return new ProfileItem(new EnvironmentEditor(env), new DataverseEnvironmentEditor(ceEnv), new ServicePrincipalEditor(foSp), new ServicePrincipalEditor(ceSp));
    }

    // ── Task 8: Interactive AuthMode ────────────────────────────────────────

    [Fact]
    public void NewProfile_AddCommand_DefaultsToInteractiveAuthMode()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive-default");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            Assert.Equal(AuthMode.Interactive, selected.FoPrincipal.AuthMode);
            Assert.Equal(AuthMode.Interactive, selected.DataversePrincipal.AuthMode);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Save_InteractiveMode_ClearsCertThumbprintAndSecretRef()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive-save");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.Environment.Name = "Env";
            selected.Environment.BaseUrl = "https://contoso.operations.dynamics.com";
            selected.Environment.TenantId = "99999999-9999-9999-9999-999999999999";
            selected.FoPrincipal.ClientId = "11111111-2222-3333-4444-555555555555";
            selected.FoPrincipal.AuthMode = AuthMode.Interactive;
            // Simulate leftover cert/secret that should be cleared on save
            selected.FoPrincipal.CertThumbprint = "AABBCCDD";
            selected.FoPrincipal.SecretRef = "old-secret-ref";

            var saveMethod = typeof(ProfilesViewModel).GetMethod("SaveAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.NotNull(saveMethod);
            var task = Assert.IsAssignableFrom<Task<bool>>(saveMethod!.Invoke(vm, new object[] { false }));
            var result = await task;

            Assert.True(result);
            // After saving an Interactive-mode principal, cert and secret must be null.
            Assert.Null(selected.FoPrincipal.CertThumbprint);
            Assert.Null(selected.FoPrincipal.SecretRef);
            Assert.Equal(AuthMode.Interactive, selected.FoPrincipal.AuthMode);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task StoredCredentialStatus_InteractiveMode_ShowsInteractiveMessage()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive-status");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            await vm.RefreshAsync();
            vm.AddProfileCommand.Execute(null);

            var selected = Assert.IsType<ProfileItem>(vm.Selected);
            selected.FoPrincipal.AuthMode = AuthMode.Interactive;

            Assert.Contains("browser", vm.FoStoredCredentialStatus, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task BeginInteractiveReauth_InteractiveMode_TriggersSignInFlow()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive-reauth");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            var fake = new FakeInteractiveTokenProvider();
            vm.InteractiveTokenProvider = fake;

            await vm.RefreshAsync();
            vm.Selected = BuildProfile(
                Guid.NewGuid().ToString("N"),
                AuthMode.Interactive,
                "11111111-2222-3333-4444-555555555555",
                "https://contoso.operations.dynamics.com",
                "99999999-9999-9999-9999-999999999999");

            await vm.BeginInteractiveReauthAsync("Finance and Operations");

            // For Interactive mode, BeginInteractiveReauth should trigger the sign-in flow.
            Assert.Equal(1, fake.Calls);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    // ── existing tests ───────────────────────────────────────────────────────

    [Fact]
    public async Task InteractiveSignIn_BearerTokenMode_AcquiresViaProviderAndStoresToken()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            var fake = new FakeInteractiveTokenProvider();
            vm.InteractiveTokenProvider = fake;

            await vm.RefreshAsync();
            vm.Selected = BuildProfile(
                Guid.NewGuid().ToString("N"),
                AuthMode.BearerToken,
                "11111111-2222-3333-4444-555555555555",
                "https://contoso.operations.dynamics.com",
                "99999999-9999-9999-9999-999999999999");

            await vm.AcquireFoTokenInteractiveAsync();

            Assert.Equal(1, fake.Calls);
            Assert.NotNull(fake.LastRequest);
            Assert.Equal("11111111-2222-3333-4444-555555555555", fake.LastRequest!.ClientId);
            Assert.Equal("99999999-9999-9999-9999-999999999999", fake.LastRequest.TenantId);
            Assert.Equal(
                ResourceUrlNormalizer.NormalizeFoBaseUrl("https://contoso.operations.dynamics.com"),
                fake.LastRequest.ResourceBaseUrl);
            Assert.False(string.IsNullOrWhiteSpace(vm.Selected!.FoPrincipal.SecretRef));
            Assert.Contains("acquired", vm.Status, StringComparison.OrdinalIgnoreCase);
            // Saving the first profile makes it active, so the acquire reaches the
            // "refresh other plugins?" branch — which must NOT block on a modal in a headless
            // context (regression: this test hung CI when it invoked MessageBox.Show).
            Assert.Contains("not refreshed", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task InteractiveSignIn_NonBearerTokenMode_DoesNotCallProvider()
    {
        var dir = Directory.CreateTempSubdirectory("profiles-interactive");
        var dbPath = Path.Combine(dir.FullName, "profiles.db");
        try
        {
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            var fake = new FakeInteractiveTokenProvider();
            vm.InteractiveTokenProvider = fake;

            await vm.RefreshAsync();
            vm.Selected = BuildProfile(
                Guid.NewGuid().ToString("N"),
                AuthMode.ClientSecret,
                "11111111-2222-3333-4444-555555555555",
                "https://contoso.operations.dynamics.com",
                "99999999-9999-9999-9999-999999999999");

            await vm.AcquireFoTokenInteractiveAsync();

            Assert.Equal(0, fake.Calls);
            Assert.Contains("BearerToken", vm.Status, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir.FullName, recursive: true); } catch { }
        }
    }
}
