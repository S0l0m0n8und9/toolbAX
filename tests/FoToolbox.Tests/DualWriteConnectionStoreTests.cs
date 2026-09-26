using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FoToolbox.Tests;

[Trait("Category", "DualWrite")]
public sealed class DualWriteConnectionStoreTests
{
    private static readonly DateTimeOffset Expiry =
        new(2026, 9, 26, 12, 34, 56, TimeSpan.Zero);

    private static readonly DualWriteDelegatedBinding Binding = new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"),
        DualWriteAuthConstants.ClientId,
        DualWriteAuthConstants.ResourceBaseUrl,
        DualWriteAuthConstants.Scope);

    [Fact]
    public async Task Bound_session_round_trips_through_a_new_store_instance()
    {
        var path = TempPath();
        try
        {
            var protector = new TestProtector();
            var settings = BoundSettings();
            await new DualWriteConnectionStore(path, protector).SaveAsync(settings, CancellationToken.None);

            var persistedJson = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain(settings.BearerToken!, persistedJson);
            Assert.DoesNotContain(settings.RefreshToken!, persistedJson);
            Assert.DoesNotContain(Binding.TenantId.ToString(), persistedJson, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Binding.ClientId, persistedJson);
            Assert.DoesNotContain(Binding.ResourceBaseUrl, persistedJson);
            Assert.DoesNotContain(Binding.Scope, persistedJson);

            var loaded = await new DualWriteConnectionStore(path, protector)
                .GetAsync(settings.Key, CancellationToken.None);

            Assert.Equal(settings.Key, loaded.Key);
            Assert.Equal(settings.GatewayBaseUrl, loaded.GatewayBaseUrl);
            Assert.Equal(settings.FoIdentifier, loaded.FoIdentifier);
            Assert.Equal(settings.BearerToken, loaded.BearerToken);
            Assert.Equal(settings.RefreshToken, loaded.RefreshToken);
            Assert.Equal(Expiry, loaded.AccessTokenExpiryUtc);
            Assert.Equal(Binding.TenantId, loaded.DelegatedBinding?.TenantId);
            Assert.Equal(Binding.ClientId, loaded.DelegatedBinding?.ClientId);
            Assert.Equal(Binding.ResourceBaseUrl, loaded.DelegatedBinding?.ResourceBaseUrl);
            Assert.Equal(Binding.Scope, loaded.DelegatedBinding?.Scope);
            Assert.True(loaded.DelegatedBinding?.IsTrusted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Reloaded_bound_session_can_create_and_renew_with_fake_transports()
    {
        var path = TempPath();
        try
        {
            var protector = new TestProtector();
            var settings = BoundSettings();
            await new DualWriteConnectionStore(path, protector).SaveAsync(settings, CancellationToken.None);
            var loaded = await new DualWriteConnectionStore(path, protector)
                .GetAsync(settings.Key, CancellationToken.None);

            var created = new DualWriteGatewayFactory().CreateRefreshing(loaded, _ => Task.CompletedTask);
            Assert.IsType<DualWriteGatewayClient>(created);
            (created as IDisposable)?.Dispose();

            var refreshTransport = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    token_type = "Bearer",
                    access_token = "renewed-access",
                    refresh_token = "rotated-refresh",
                    expires_in = 3600,
                    scope = "user_impersonation offline_access"
                }))
            }));
            var gatewayTransport = new RecordingHandler(_ =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            var now = Expiry.AddMinutes(1);
            var refresher = new DualWriteRefreshTokenProvider(new HttpClient(refreshTransport))
            {
                Clock = () => now
            };
            var current = new DualWriteToken(
                loaded.BearerToken!, loaded.RefreshToken, loaded.AccessTokenExpiryUtc!.Value)
            {
                Binding = loaded.DelegatedBinding
            };
            DualWriteToken? persisted = null;
            var handler = new RefreshingBearerTokenHandler(
                current,
                refresher,
                new Uri(loaded.GatewayBaseUrl),
                token =>
                {
                    persisted = token;
                    return new DualWriteConnectionStore(path, protector).SaveAsync(loaded with
                    {
                        BearerToken = token.AccessToken,
                        RefreshToken = token.RefreshToken,
                        AccessTokenExpiryUtc = token.ExpiresUtc,
                        DelegatedBinding = token.Binding
                    }, CancellationToken.None);
                },
                () => now)
            {
                InnerHandler = gatewayTransport
            };
            using var invoker = new HttpMessageInvoker(handler);

            using var response = await invoker.SendAsync(new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(loaded.GatewayBaseUrl + "api/DualWriteManagement/1.0/Version")),
                CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, refreshTransport.Calls);
            Assert.Equal(1, gatewayTransport.Calls);
            Assert.Equal("renewed-access", gatewayTransport.Authorization?.Parameter);
            Assert.Equal("Bearer", gatewayTransport.Authorization?.Scheme, ignoreCase: true);
            Assert.Equal(Binding, persisted?.Binding);
            Assert.Equal("rotated-refresh", persisted?.RefreshToken);
            var reloadedAfterRefresh = await new DualWriteConnectionStore(path, protector)
                .GetAsync(settings.Key, CancellationToken.None);
            Assert.Equal("renewed-access", reloadedAfterRefresh.BearerToken);
            Assert.Equal("rotated-refresh", reloadedAfterRefresh.RefreshToken);
            Assert.Equal(Binding, reloadedAfterRefresh.DelegatedBinding);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Legacy_record_without_binding_stays_unbound_static_compatible_and_refresh_refused()
    {
        var path = TempPath();
        try
        {
            var protector = new TestProtector();
            await WriteRecordAsync(path, protector, protectedBinding: null);

            var loaded = await new DualWriteConnectionStore(path, protector)
                .GetAsync("bound-session", CancellationToken.None);

            Assert.Null(loaded.DelegatedBinding);
            Assert.Equal("access-token", loaded.BearerToken);
            Assert.Equal("refresh-token", loaded.RefreshToken);
            var exception = Assert.Throws<InvalidOperationException>(() =>
                new DualWriteGatewayFactory().CreateRefreshing(loaded, _ => Task.CompletedTask));
            Assert.Contains("sign in", exception.Message, StringComparison.OrdinalIgnoreCase);

            var staticGateway = new DualWriteGatewayFactory().Create(loaded);
            Assert.IsType<DualWriteGatewayClient>(staticGateway);
            (staticGateway as IDisposable)?.Dispose();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Malformed_or_untrusted_stored_binding_fails_before_credentials_are_unprotected(bool untrusted)
    {
        var path = TempPath();
        try
        {
            var protector = new TestProtector();
            var bindingJson = untrusted
                ? JsonSerializer.Serialize(Binding with { ClientId = "foreign-client" })
                : "{not-json";
            var protectedBinding = protector.Protect(bindingJson);
            await WriteRecordAsync(path, protector, protectedBinding);
            protector.UnprotectInputs.Clear();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new DualWriteConnectionStore(path, protector)
                    .GetAsync("bound-session", CancellationToken.None));

            Assert.Contains("sign in again", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(new[] { protectedBinding }, protector.UnprotectInputs);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Save_rejects_untrusted_binding_before_protection_cache_or_file_mutation()
    {
        var path = TempPath();
        var protector = new TestProtector();
        var settings = BoundSettings() with
        {
            DelegatedBinding = Binding with { Scope = "https://graph.microsoft.com/.default" }
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DualWriteConnectionStore(path, protector).SaveAsync(settings, CancellationToken.None));

        Assert.Contains("sign in again", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, protector.ProtectCalls);
        Assert.False(File.Exists(path));
    }

    private static DualWriteConnectionSettings BoundSettings() => new(
        "bound-session",
        "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/",
        "https://contoso.operations.dynamics.com",
        "access-token")
    {
        RefreshToken = "refresh-token",
        AccessTokenExpiryUtc = Expiry,
        DelegatedBinding = Binding
    };

    private static string TempPath() =>
        Path.Combine(Path.GetTempPath(), $"toolbax-dw-store-{Guid.NewGuid():N}.json");

    private static Task WriteRecordAsync(
        string path,
        TestProtector protector,
        string? protectedBinding)
    {
        var json = JsonSerializer.Serialize(new
        {
            connections = new[]
            {
                new
                {
                    key = "bound-session",
                    gatewayBaseUrl = "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/",
                    foIdentifier = "https://contoso.operations.dynamics.com",
                    protectedToken = protector.Protect("access-token"),
                    protectedRefreshToken = protector.Protect("refresh-token"),
                    protectedDelegatedBinding = protectedBinding,
                    accessTokenExpiryUtc = Expiry.ToString("o"),
                    updatedUtc = Expiry.ToString("o")
                }
            }
        });
        return File.WriteAllTextAsync(path, json);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization;
            return send(request);
        }
    }

    private sealed class TestProtector : ITokenProtector
    {
        private const string Prefix = "protected:";

        public int ProtectCalls { get; private set; }
        public List<string> UnprotectInputs { get; } = [];

        public string Protect(string plaintext)
        {
            ProtectCalls++;
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        }

        public string? Unprotect(string protectedValue)
        {
            UnprotectInputs.Add(protectedValue);
            if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
                return null;

            return Encoding.UTF8.GetString(Convert.FromBase64String(protectedValue[Prefix.Length..]));
        }
    }
}
