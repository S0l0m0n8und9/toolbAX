using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Net;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// A <see cref="DelegatingHandler"/> that stamps an F&amp;O bearer token (for whichever environment is
/// active at request time) onto outgoing requests. Lets FoToolbox.Core's <c>CatalogService</c> — which
/// takes a plain <see cref="HttpClient"/> — reuse the same <see cref="IAuthService"/> auth as the rest
/// of the app. Tagged metadata requests require their captured context and origin to match before and
/// after authentication. Untagged callers retain existing-header behavior; a token is only added for the
/// active environment's origin, so foreign paging links cannot acquire its bearer.
/// </summary>
public sealed class AuthenticatedHttpHandler : DelegatingHandler
{
    private readonly IAuthService _auth;
    private readonly Func<EnvProfile?> _activeEnv;

    public AuthenticatedHttpHandler(IAuthService auth, Func<EnvProfile?> activeEnv)
    {
        _auth = auth;
        _activeEnv = activeEnv;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var env = _activeEnv();
        if (request.Options.TryGetValue(CatalogRequestContext.MetadataCachePartition, out var partition))
        {
            // Tagged metadata is owned by a captured request context. Neither anonymous dispatch nor a
            // caller-supplied bearer can bypass that ownership or select credentials from another profile.
            var identity = EnvironmentIdentity.TryCreate(env);
            if (identity is null || !string.Equals(partition, identity.ToMetadataCachePartition(), StringComparison.Ordinal)
                || !RequestOriginGuard.IsSameOrigin(env!.Url, request.RequestUri))
            {
                throw new InvalidOperationException("The metadata request no longer matches the active environment.");
            }

            var token = await _auth.AcquireFoTokenAsync(env!, ct).ConfigureAwait(false);
            if (!identity.IsCurrent(_activeEnv()))
            {
                throw new InvalidOperationException("The active environment changed before the metadata request was sent.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await base.SendAsync(request, ct).ConfigureAwait(false);
        }

        if (env is not null
            && request.Headers.Authorization is null
            && RequestOriginGuard.IsSameOrigin(env.Url, request.RequestUri))
        {
            var token = await _auth.AcquireFoTokenAsync(env, ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }
}
