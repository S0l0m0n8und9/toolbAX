using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Host;

/// <summary>
/// Delegating handler that injects a bearer token acquired through the shared <see cref="AuthBroker"/>.
/// All token-acquisition policy (mode routing, credential resolution, caching) lives in the broker —
/// this class only maps failures to <see cref="AuthRecoveryException"/> and notifies the coordinator.
/// </summary>
internal sealed class AuthenticatedHandler : DelegatingHandler
{
    private readonly AuthReauthCoordinator? _reauthCoordinator;
    private readonly AuthBroker _broker;
    private readonly AuthTokenRequest _request;
    private readonly string _serviceName;

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp, SecretVaultService vault, AuthReauthCoordinator? reauthCoordinator = null)
        : this(env, sp, new AuthBroker(vault), reauthCoordinator)
    {
    }

    public AuthenticatedHandler(string resourceBaseUrl, string tenantId, ServicePrincipal sp, SecretVaultService vault, AuthReauthCoordinator? reauthCoordinator = null)
        : this(resourceBaseUrl, tenantId, sp, new AuthBroker(vault), reauthCoordinator)
    {
    }

    public AuthenticatedHandler(FoEnvironment env, ServicePrincipal sp, AuthBroker broker, AuthReauthCoordinator? reauthCoordinator = null)
        : this(ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl), env.TenantId, sp, broker, reauthCoordinator)
    {
    }

    public AuthenticatedHandler(string resourceBaseUrl, string tenantId, ServicePrincipal sp, AuthBroker broker, AuthReauthCoordinator? reauthCoordinator = null)
        : base(new HttpClientHandler())
    {
        _reauthCoordinator = reauthCoordinator;
        _broker = broker;
        _serviceName = sp.Target == AuthTarget.Dataverse ? "Dataverse" : "Finance and Operations";
        _request = new AuthTokenRequest(resourceBaseUrl, tenantId, sp, _serviceName);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string token;
        try
        {
            token = await _broker.AcquireTokenAsync(_request, cancellationToken);
        }
        catch (Exception ex) when (TryCreateRecoveryException(ex, out var recovery))
        {
            _reauthCoordinator?.Notify(recovery!);
            throw recovery!;
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            response.Dispose();
            var recovery = new AuthRecoveryException(
                _serviceName,
                $"{_serviceName} needs you to sign in again. The host will switch to Profiles so you can complete interactive re-authentication for this environment, then save and re-apply the profile.",
                requiresInteractiveReauth: true);
            _reauthCoordinator?.Notify(recovery);
            throw recovery;
        }

        return response;
    }

    private bool TryCreateRecoveryException(Exception exception, out AuthRecoveryException? recovery)
    {
        if (exception is AuthRecoveryException authRecovery)
        {
            recovery = authRecovery;
            return true;
        }

        if (_request.Principal.AuthMode == AuthMode.BearerToken &&
            exception is InvalidOperationException invalidOperation &&
            invalidOperation.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            recovery = new AuthRecoveryException(
                _serviceName,
                $"{_serviceName} bearer token has expired. The host will switch to Profiles so you can acquire a fresh token for this environment, then save and re-apply the profile.",
                requiresInteractiveReauth: true,
                exception);
            return true;
        }

        recovery = null;
        return false;
    }
}
