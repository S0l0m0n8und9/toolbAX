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
