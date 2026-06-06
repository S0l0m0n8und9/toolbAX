using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// A <see cref="DelegatingHandler"/> that stamps an F&amp;O bearer token (for whichever environment is
/// active at request time) onto outgoing requests. Lets FoToolbox.Core's <c>CatalogService</c> — which
/// takes a plain <see cref="HttpClient"/> — reuse the same <see cref="IAuthService"/> auth as the rest
/// of the app. A request that already carries an Authorization header is left untouched.
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
        if (env is not null && request.Headers.Authorization is null)
        {
            var token = await _auth.AcquireFoTokenAsync(env, ct).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, ct).ConfigureAwait(false);
    }
}
