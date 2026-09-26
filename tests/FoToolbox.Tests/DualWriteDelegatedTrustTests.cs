using System.Net;
using System.Text;
using System.Text.Json;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;

namespace FoToolbox.Tests;

[Trait("Category", "DualWrite")]
public sealed class DualWriteDelegatedCaptureTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Gateway = "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version";

    private sealed class FixedResolver(Guid? tenant) : IDualWriteTenantResolver
    {
        public int Calls { get; private set; }
        public Task<Guid?> ResolveAsync(string domain, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(tenant);
        }
    }

    private sealed class SequencedResolver(Guid tenant) : IDualWriteTenantResolver
    {
        private int _calls;
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Guid?> ResolveAsync(string domain, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstEntered.TrySetResult();
                await FirstRelease.Task; // deliberately ignores caller cancellation
            }
            return tenant;
        }
    }

    [Theory]
    [InlineData(false, "authorization_code")]
    [InlineData(true, "authorization_code")]
    [InlineData(false, "refresh_token")]
    [InlineData(true, "refresh_token")]
    public async Task Trusted_opaque_token_completes_only_after_exact_gateway_bearer_correlation(
        bool gatewayFirst,
        string grantType)
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);
        var exchange = Exchange(
            TenantA.ToString("D"),
            "opaque-token",
            Response("opaque-token"),
            grantType: grantType);
        var gateway = GatewayResponse("opaque-token");

        if (gatewayFirst)
        {
            Assert.False(capture.ObserveGatewayResponse(gateway));
            Assert.True(await capture.ObserveTokenExchangeAsync(exchange, CancellationToken.None));
        }
        else
        {
            Assert.True(await capture.ObserveTokenExchangeAsync(exchange, CancellationToken.None));
            Assert.False(capture.IsComplete);
            Assert.True(capture.ObserveGatewayResponse(gateway));
        }

        Assert.True(capture.IsComplete);
        Assert.Equal("opaque-token", capture.Result!.Token.AccessToken);
        Assert.Equal(TenantA, capture.Result.Token.Binding!.TenantId);
        Assert.Equal(DualWriteAuthConstants.ClientId, capture.Result.Token.Binding.ClientId);
        Assert.Equal(DualWriteAuthConstants.ResourceBaseUrl, capture.Result.Token.Binding.ResourceBaseUrl);
        Assert.Equal("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/", capture.Result.GatewayBaseUrl);
    }

    [Fact]
    public async Task Foreign_client_and_mixed_resource_do_not_poison_a_later_valid_exchange()
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);
        Assert.False(await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "foreign-client", Response("foreign-client"), clientId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            CancellationToken.None));
        Assert.False(await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "mixed", Response("mixed"),
                scope: DualWriteAuthConstants.Scope + " https://graph.microsoft.com/.default"),
            CancellationToken.None));
        Assert.False(await capture.ObserveTokenExchangeAsync(
            Exchange(
                TenantA.ToString("D"),
                "wrong-tenant",
                Response("wrong-tenant", idToken: IdToken(TenantB, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None));

        Assert.True(await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "valid", Response("valid")),
            CancellationToken.None));
        Assert.True(capture.ObserveGatewayResponse(GatewayResponse("valid")));

        Assert.Equal("valid", capture.Result!.Token.AccessToken);
    }

    [Fact]
    public async Task Gateway_bearer_must_exactly_match_the_trusted_exchange()
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);
        Assert.True(await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "expected", Response("expected")),
            CancellationToken.None));

        Assert.False(capture.ObserveGatewayResponse(GatewayResponse("expected", status: 401)));
        Assert.False(capture.ObserveGatewayResponse(GatewayResponse("different")));
        Assert.False(capture.IsComplete);
        Assert.True(capture.ObserveGatewayResponse(GatewayResponse("expected")));
        Assert.True(capture.IsComplete);
    }

    public static TheoryData<DualWriteTokenExchangeObservation> InvalidExchanges => new()
    {
        Exchange(TenantA.ToString("D"), "failed", Response("failed"), status: 400),
        Exchange(TenantA.ToString("D"), "wrong-method", Response("wrong-method"), method: HttpMethod.Get),
        Exchange(TenantA.ToString("D"), "wrong-content", Response("wrong-content"), contentType: "application/json"),
        Exchange(TenantA.ToString("D"), "missing-expiry", "{\"access_token\":\"missing-expiry\",\"token_type\":\"Bearer\"}"),
        Exchange(TenantA.ToString("D"), "wrong-type", "{\"access_token\":\"wrong-type\",\"token_type\":\"DPoP\",\"expires_in\":3600}"),
        Exchange(TenantA.ToString("D"), "bad-token", Response("bad token\r\ncanary")),
        Exchange(TenantA.ToString("D"), "zero-expiry", "{\"access_token\":\"zero-expiry\",\"token_type\":\"Bearer\",\"expires_in\":0}"),
        Exchange(TenantA.ToString("D"), "duplicate", Response("duplicate"), extraForm: "&client_id=" + DualWriteAuthConstants.ClientId),
        Exchange(TenantA.ToString("D"), "foreign-resource", Response("foreign-resource"), extraForm: "&resource=https%3A%2F%2Fgraph.microsoft.com"),
        Exchange(TenantA.ToString("D"), "bad-grant", Response("bad-grant"), grantType: "password"),
        Exchange(TenantA.ToString("D"), "scope-conflict", Response("scope-conflict", scope: "https://graph.microsoft.com/.default"))
    };

    [Theory]
    [MemberData(nameof(InvalidExchanges))]
    public async Task Failed_or_malformed_exchange_is_not_captured(DualWriteTokenExchangeObservation observation)
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);

        Assert.False(await capture.ObserveTokenExchangeAsync(observation, CancellationToken.None));
        Assert.Null(capture.Token);
        Assert.False(capture.IsComplete);
        Assert.DoesNotContain("opaque", capture.IncompleteReason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("common")]
    [InlineData("organizations")]
    public async Task Organizational_aliases_pin_actual_target_tenant_from_same_response_id_token(string constraint)
    {
        var idToken = IdToken(TenantA, DualWriteAuthConstants.ClientId, Now, homeTenant: TenantB);
        var capture = new DualWriteSignInCapture(constraint, clock: () => Now);

        Assert.True(await capture.ObserveTokenExchangeAsync(
            Exchange(
                string.IsNullOrEmpty(constraint) ? "common" : constraint,
                "opaque",
                Response("opaque", idToken: idToken, clientInfoHomeTenant: TenantB)),
            CancellationToken.None));
        Assert.True(capture.ObserveGatewayResponse(GatewayResponse("opaque")));

        Assert.Equal(TenantA, capture.Result!.Token.Binding!.TenantId);
    }

    [Fact]
    public async Task Domain_constraint_resolves_on_fixed_authority_and_must_match_target_id_token()
    {
        var resolver = new FixedResolver(TenantA);
        var capture = new DualWriteSignInCapture("contoso.example", resolver, () => Now);

        Assert.True(await capture.ObserveTokenExchangeAsync(
            Exchange("contoso.example", "opaque", Response("opaque", idToken: IdToken(TenantA, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None));
        Assert.Equal(1, resolver.Calls);

        var mismatch = new DualWriteSignInCapture("contoso.example", resolver, () => Now);
        Assert.False(await mismatch.ObserveTokenExchangeAsync(
            Exchange("contoso.example", "other", Response("other", idToken: IdToken(TenantB, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111", true)]
    [InlineData("22222222-2222-2222-2222-222222222222", false)]
    public async Task Domain_constraint_accepts_matching_concrete_guid_endpoint_without_id_token(
        string endpointTenant,
        bool accepted)
    {
        var capture = new DualWriteSignInCapture("contoso.example", new FixedResolver(TenantA), () => Now);

        Assert.Equal(accepted, await capture.ObserveTokenExchangeAsync(
            Exchange(endpointTenant, "opaque", Response("opaque")),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("guid", "common")]
    [InlineData("guid", "organizations")]
    [InlineData("guid", "contoso.example")]
    [InlineData("domain", "common")]
    [InlineData("domain", "organizations")]
    [InlineData("domain", "11111111-1111-1111-1111-111111111111")]
    public async Task Configured_guid_or_domain_accepts_matching_actual_tenant_across_trusted_authorities(
        string configured,
        string endpointTenant)
    {
        var resolver = new FixedResolver(TenantA);
        var constraint = configured == "guid" ? TenantA.ToString("D") : "contoso.example";
        var capture = new DualWriteSignInCapture(constraint, resolver, () => Now);

        Assert.True(await capture.ObserveTokenExchangeAsync(
            Exchange(endpointTenant, "opaque", Response("opaque", idToken: IdToken(TenantA, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None));
        Assert.True(capture.ObserveGatewayResponse(GatewayResponse("opaque")));
        Assert.Equal(TenantA, capture.Result!.Token.Binding!.TenantId);
    }

    [Theory]
    [InlineData("guid", "common")]
    [InlineData("domain", "organizations")]
    [InlineData("domain", "22222222-2222-2222-2222-222222222222")]
    public async Task Configured_tenant_rejects_a_different_actual_target(string configured, string endpointTenant)
    {
        var constraint = configured == "guid" ? TenantA.ToString("D") : "contoso.example";
        var capture = new DualWriteSignInCapture(constraint, new FixedResolver(TenantA), () => Now);

        Assert.False(await capture.ObserveTokenExchangeAsync(
            Exchange(endpointTenant, "opaque", Response("opaque", idToken: IdToken(TenantB, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None));
        Assert.False(capture.IsComplete);
    }

    [Theory]
    [InlineData("user_impersonation", true)]
    [InlineData("https://IntegratorApp.com/user_impersonation", true)]
    [InlineData("https%3A%2F%2FIntegratorApp.com%2Fuser_impersonation", true)]
    [InlineData("https://graph.microsoft.com/User.Read", false)]
    [InlineData("user_impersonation https://graph.microsoft.com/User.Read", false)]
    public async Task Default_scope_accepts_actual_resource_permissions_but_rejects_foreign_qualified_scopes(
        string responseScope,
        bool accepted)
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);

        Assert.Equal(accepted, await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "opaque", Response("opaque", scope: responseScope)),
            CancellationToken.None));
    }

    [Theory]
    [InlineData("user_impersonation", true)]
    [InlineData("other_permission", false)]
    public async Task Explicit_named_scope_constrains_returned_permission(string responseScope, bool accepted)
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);
        var requested = DualWriteAuthConstants.ResourceBaseUrl + "/user_impersonation openid";

        Assert.Equal(accepted, await capture.ObserveTokenExchangeAsync(
            Exchange(TenantA.ToString("D"), "opaque", Response("opaque", scope: responseScope), scope: requested),
            CancellationToken.None));
    }

    [Fact]
    public async Task Delayed_validation_cannot_overwrite_a_completed_capture()
    {
        var resolver = new SequencedResolver(TenantA);
        var capture = new DualWriteSignInCapture("contoso.example", resolver, () => Now);
        var delayed = capture.ObserveTokenExchangeAsync(
            Exchange("contoso.example", "late", Response("late", idToken: IdToken(TenantA, DualWriteAuthConstants.ClientId, Now))),
            CancellationToken.None);
        await resolver.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(capture.ObserveGatewayResponse(GatewayResponse("winner")));
            Assert.True(await capture.ObserveTokenExchangeAsync(
                Exchange("contoso.example", "winner", Response("winner", idToken: IdToken(TenantA, DualWriteAuthConstants.ClientId, Now))),
                CancellationToken.None));
            Assert.Equal("winner", capture.Result!.Token.AccessToken);

            resolver.FirstRelease.TrySetResult();
            Assert.False(await delayed);
            Assert.Equal("winner", capture.Result.Token.AccessToken);
            Assert.Equal(string.Empty, capture.IncompleteReason);
        }
        finally
        {
            resolver.FirstRelease.TrySetResult();
            await delayed;
        }
    }

    [Fact]
    public async Task Cancelled_delayed_validation_never_commits_pending_state_or_reason()
    {
        var resolver = new SequencedResolver(TenantA);
        var capture = new DualWriteSignInCapture("contoso.example", resolver, () => Now);
        var initialReason = capture.IncompleteReason;
        using var cancellation = new CancellationTokenSource();
        var delayed = capture.ObserveTokenExchangeAsync(
            Exchange("contoso.example", "late", Response("late", idToken: IdToken(TenantA, DualWriteAuthConstants.ClientId, Now))),
            cancellation.Token);
        await resolver.FirstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        resolver.FirstRelease.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delayed);
        Assert.Equal(0, capture.PendingTokenCount);
        Assert.Equal(initialReason, capture.IncompleteReason);
        Assert.Null(capture.Result);
    }

    [Theory]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-client")]
    [InlineData("wrong-issuer")]
    [InlineData("expired")]
    [InlineData("malformed")]
    [InlineData("missing")]
    public async Task Alias_capture_rejects_invalid_or_missing_id_metadata(string mode)
    {
        var token = mode switch
        {
            "wrong-tenant" => IdToken(TenantB, DualWriteAuthConstants.ClientId, Now),
            "wrong-client" => IdToken(TenantA, "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", Now),
            "wrong-issuer" => IdToken(TenantA, DualWriteAuthConstants.ClientId, Now, issuer: $"https://evil.example/{TenantA:D}/v2.0"),
            "expired" => IdToken(TenantA, DualWriteAuthConstants.ClientId, Now.AddHours(-2)),
            "malformed" => "not-a-jwt",
            _ => null
        };
        var constraint = mode == "wrong-tenant" ? TenantA.ToString("D") : "common";
        var endpointTenant = mode == "wrong-tenant" ? TenantA.ToString("D") : "common";
        var capture = new DualWriteSignInCapture(constraint, clock: () => Now);

        Assert.False(await capture.ObserveTokenExchangeAsync(
            Exchange(endpointTenant, "opaque", Response("opaque", idToken: token)),
            CancellationToken.None));
        Assert.False(capture.IsComplete);
    }

    [Fact]
    public async Task Pending_correlation_state_is_bounded_and_manual_close_has_no_fallback()
    {
        var capture = new DualWriteSignInCapture(TenantA.ToString("D"), clock: () => Now);
        for (var index = 0; index < 12; index++)
        {
            Assert.True(await capture.ObserveTokenExchangeAsync(
                Exchange(TenantA.ToString("D"), $"token-{index}", Response($"token-{index}")),
                CancellationToken.None));
            Assert.False(capture.ObserveGatewayResponse(GatewayResponse($"gateway-{index}")));
        }

        Assert.InRange(capture.PendingTokenCount, 1, 4);
        Assert.InRange(capture.PendingGatewayCount, 1, 4);
        Assert.False(capture.ObserveUrl(Gateway));
        Assert.False(capture.ObserveTokenResponseBody(Response("legacy")));
        Assert.Null(capture.BestEffortResult);
    }

    internal static DualWriteTokenExchangeObservation Exchange(
        string tenant,
        string accessToken,
        string responseBody,
        string? clientId = null,
        string? scope = null,
        string grantType = "authorization_code",
        string extraForm = "",
        int status = 200,
        HttpMethod? method = null,
        string contentType = "application/x-www-form-urlencoded")
    {
        var credential = grantType == "refresh_token" ? "refresh_token=refresh" : "code=code";
        var form = $"client_id={Uri.EscapeDataString(clientId ?? DualWriteAuthConstants.ClientId)}" +
                   $"&grant_type={Uri.EscapeDataString(grantType)}" +
                   $"&scope={Uri.EscapeDataString(scope ?? DualWriteAuthConstants.Scope)}&{credential}{extraForm}";
        return new DualWriteTokenExchangeObservation(
            new Uri($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"),
            method ?? HttpMethod.Post,
            contentType,
            form,
            status,
            responseBody);
    }

    internal static DualWriteGatewayResponseObservation GatewayResponse(string token, int status = 200) =>
        new(new Uri(Gateway), "Bearer " + token, status);

    internal static string Response(
        string accessToken,
        string? idToken = null,
        string? scope = null,
        Guid? clientInfoHomeTenant = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["access_token"] = accessToken,
            ["refresh_token"] = "refresh",
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["scope"] = scope,
            ["id_token"] = idToken,
            ["client_info"] = clientInfoHomeTenant is null
                ? null
                : Base64Url(JsonSerializer.Serialize(new { utid = clientInfoHomeTenant.Value.ToString("D") }))
        }.Where(pair => pair.Value is not null).ToDictionary());

    internal static string IdToken(
        Guid tenant,
        string audience,
        DateTimeOffset now,
        Guid? homeTenant = null,
        string? issuer = null)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["tid"] = tenant.ToString("D"),
            ["iss"] = issuer ?? $"https://login.microsoftonline.com/{tenant:D}/v2.0",
            ["aud"] = audience,
            ["azp"] = audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-1).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(1).ToUnixTimeSeconds(),
            ["utid"] = homeTenant?.ToString("D")
        }.Where(pair => pair.Value is not null).ToDictionary());
        return Base64Url("{\"alg\":\"none\"}") + "." + Base64Url(payload) + ".unsigned";
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

[Trait("Category", "DualWrite")]
public sealed class DualWriteTenantResolverTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            return send(request, cancellationToken);
        }
    }

    [Fact]
    public async Task Domain_resolution_uses_fixed_host_and_exact_issuer()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"issuer\":\"https://login.microsoftonline.com/{Tenant:D}/v2.0\"}}")
        }));
        var resolver = new DualWriteTenantResolver(new HttpClient(handler));

        var result = await resolver.ResolveAsync("contoso.example", CancellationToken.None);

        Assert.Equal(Tenant, result);
        Assert.Equal("login.microsoftonline.com", handler.LastUri!.Host);
        Assert.Equal("/contoso.example/v2.0/.well-known/openid-configuration", handler.LastUri.AbsolutePath);
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("evil/example")]
    [InlineData("-evil.example")]
    [InlineData("evil..example")]
    public async Task Invalid_domain_fails_before_network(string domain)
    {
        var handler = new Handler((_, _) => throw new InvalidOperationException());
        var resolver = new DualWriteTenantResolver(new HttpClient(handler));

        Assert.Null(await resolver.ResolveAsync(domain, CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Mismatching_or_redirected_metadata_fails_closed()
    {
        var mismatch = new DualWriteTenantResolver(new HttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"issuer\":\"https://evil.example/tenant/v2.0\"}")
            }))));
        var redirect = new DualWriteTenantResolver(new HttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
            {
                Headers = { Location = new Uri("https://evil.example/") }
            }))));

        Assert.Null(await mismatch.ResolveAsync("contoso.example", CancellationToken.None));
        Assert.Null(await redirect.ResolveAsync("contoso.example", CancellationToken.None));
        using var transport = DualWriteTenantResolver.CreateTransport();
        Assert.False(transport.AllowAutoRedirect);
    }

    [Fact]
    public async Task Oversized_metadata_is_rejected_and_caller_cancellation_propagates()
    {
        var oversized = new DualWriteTenantResolver(new HttpClient(new Handler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', 64 * 1024 + 1))
            }))));
        Assert.Null(await oversized.ResolveAsync("contoso.example", CancellationToken.None));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var resolver = new DualWriteTenantResolver(new HttpClient(new Handler((_, ct) =>
            Task.FromCanceled<HttpResponseMessage>(ct))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            resolver.ResolveAsync("contoso.example", cancelled.Token));
    }
}

[Trait("Category", "DualWrite")]
public sealed class DualWritePinnedRefreshTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DualWriteDelegatedBinding Binding = new(
        Tenant,
        DualWriteAuthConstants.ClientId,
        DualWriteAuthConstants.ResourceBaseUrl,
        DualWriteAuthConstants.Scope);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? LastUri { get; private set; }
        public string? LastBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await send(request);
        }
    }

    [Fact]
    public async Task Refresh_is_pinned_to_actual_tenant_client_and_resource_and_preserves_binding()
    {
        var transport = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DualWriteDelegatedCaptureTests.Response("new-access"))
        }));
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport)) { Clock = () => Now };
        var current = new DualWriteToken("old", "old-refresh", Now.AddMinutes(-1)) { Binding = Binding };

        var refreshed = await provider.RefreshAsync(current, CancellationToken.None);

        Assert.Equal($"https://login.microsoftonline.com/{Tenant:D}/oauth2/v2.0/token", transport.LastUri!.AbsoluteUri);
        Assert.Contains("client_id=" + Uri.EscapeDataString(DualWriteAuthConstants.ClientId), transport.LastBody);
        Assert.Contains("scope=https%3A%2F%2FIntegratorApp.com%2F.default", transport.LastBody);
        Assert.Contains("offline_access", transport.LastBody);
        Assert.Equal("new-access", refreshed.AccessToken);
        Assert.Equal(Binding, refreshed.Binding);
    }

    [Theory]
    [InlineData("user_impersonation")]
    [InlineData("https://IntegratorApp.com/user_impersonation")]
    [InlineData("https%3A%2F%2FIntegratorApp.com%2Fuser_impersonation")]
    public async Task Refresh_accepts_default_scope_expansion_for_the_bound_resource(string responseScope)
    {
        var transport = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DualWriteDelegatedCaptureTests.Response("new-access", scope: responseScope))
        }));
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport)) { Clock = () => Now };
        var current = new DualWriteToken("old", "old-refresh", Now.AddMinutes(-1)) { Binding = Binding };

        var refreshed = await provider.RefreshAsync(current, CancellationToken.None);

        Assert.Equal("new-access", refreshed.AccessToken);
        Assert.Equal(Binding, refreshed.Binding);
    }

    [Fact]
    public async Task Cancelled_late_refresh_response_never_persists_or_reaches_gateway()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new Handler(_ => { entered.TrySetResult(); return release.Task; });
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport)) { Clock = () => Now };
        var current = new DualWriteToken("old", "refresh", Now.AddMinutes(-1)) { Binding = Binding };
        var gateway = new UnauthorizedGateway();
        var persisted = 0;
        var handler = new RefreshingBearerTokenHandler(
            current,
            provider,
            new Uri("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/"),
            _ => { persisted++; return Task.CompletedTask; },
            () => Now)
        {
            InnerHandler = gateway
        };
        using var cancellation = new CancellationTokenSource();
        var send = new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version"),
            cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.SetResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DualWriteDelegatedCaptureTests.Response("late"))
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        Assert.Equal(0, persisted);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task Legacy_refresh_without_binding_fails_before_network()
    {
        var transport = new Handler(_ => throw new InvalidOperationException());
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport));

        var ex = await Assert.ThrowsAsync<DualWriteAuthException>(() =>
            provider.RefreshAsync("legacy-refresh", CancellationToken.None));

        Assert.Contains("sign in", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, transport.Calls);
    }

    [Fact]
    public async Task Invalid_refresh_response_does_not_persist_or_replay()
    {
        var transport = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DualWriteDelegatedCaptureTests.Response(
                "bad", scope: "https://graph.microsoft.com/.default"))
        }));
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport)) { Clock = () => Now };
        var current = new DualWriteToken("old", "refresh", Now.AddHours(1)) { Binding = Binding };
        var gateway = new UnauthorizedGateway();
        var persisted = 0;
        var handler = new RefreshingBearerTokenHandler(
            current,
            provider,
            new Uri("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/"),
            _ => { persisted++; return Task.CompletedTask; },
            () => Now)
        {
            InnerHandler = gateway
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(0, persisted);
    }

    [Fact]
    public async Task Refreshed_bearer_with_whitespace_is_rejected_without_callback_or_replay()
    {
        var transport = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DualWriteDelegatedCaptureTests.Response("bad token\r\nREFRESH-CANARY"))
        }));
        var provider = new DualWriteRefreshTokenProvider(new HttpClient(transport)) { Clock = () => Now };
        var current = new DualWriteToken("old", "refresh", Now.AddHours(1)) { Binding = Binding };
        var gateway = new UnauthorizedGateway();
        var persisted = 0;
        var handler = new RefreshingBearerTokenHandler(
            current,
            provider,
            new Uri("https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/"),
            _ => { persisted++; return Task.CompletedTask; },
            () => Now)
        {
            InnerHandler = gateway
        };

        var response = await new HttpMessageInvoker(handler).SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/api/DualWriteManagement/1.0/Version"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, transport.Calls);
        Assert.Equal(1, gateway.Calls);
        Assert.Equal(0, persisted);
    }

    [Fact]
    public void Refreshing_factory_rejects_legacy_context_before_client_creation()
    {
        var settings = new DualWriteConnectionSettings(
            "key",
            "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/",
            "fo.example",
            "access")
        {
            RefreshToken = "refresh",
            AccessTokenExpiryUtc = Now.AddHours(1)
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DualWriteGatewayFactory().CreateRefreshing(settings, _ => Task.CompletedTask));
        Assert.Contains("sign in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refreshing_factory_accepts_a_bound_delegated_session_without_network_access()
    {
        var settings = new DualWriteConnectionSettings(
            "key",
            "https://projectmanagementservice.weu.gateway.prod.island.powerapps.com/",
            "fo.example",
            "access")
        {
            RefreshToken = "refresh",
            AccessTokenExpiryUtc = Now.AddHours(1),
            DelegatedBinding = Binding
        };

        var gateway = new DualWriteGatewayFactory().CreateRefreshing(settings, _ => Task.CompletedTask);

        Assert.IsType<DualWriteGatewayClient>(gateway);
        (gateway as IDisposable)?.Dispose();
    }

    private sealed class UnauthorizedGateway : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
