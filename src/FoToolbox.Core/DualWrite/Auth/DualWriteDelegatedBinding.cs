using System;
using System.Net.Http;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Immutable provenance required to renew a captured delegated Dual-write token.</summary>
public sealed record DualWriteDelegatedBinding(
    Guid TenantId,
    string ClientId,
    string ResourceBaseUrl,
    string Scope)
{
    public bool IsTrusted =>
        TenantId != Guid.Empty &&
        string.Equals(ClientId, DualWriteAuthConstants.ClientId, StringComparison.Ordinal) &&
        string.Equals(ResourceBaseUrl, DualWriteAuthConstants.ResourceBaseUrl, StringComparison.Ordinal) &&
        DualWriteTokenResponseValidator.TryValidateScopes(Scope, out _);
}

/// <summary>Committed token endpoint request/response evidence supplied by the browser adapter.</summary>
public sealed record DualWriteTokenExchangeObservation(
    Uri RequestUri,
    HttpMethod Method,
    string? RequestContentType,
    string RequestForm,
    int ResponseStatusCode,
    string ResponseBody);

/// <summary>Committed trusted-gateway response and the bearer used for its request.</summary>
public sealed record DualWriteGatewayResponseObservation(
    Uri RequestUri,
    string? Authorization,
    int ResponseStatusCode);

/// <summary>Resolves a validated tenant domain to the concrete tenant GUID advertised by Entra.</summary>
public interface IDualWriteTenantResolver
{
    Task<Guid?> ResolveAsync(string domain, CancellationToken cancellationToken);
}
