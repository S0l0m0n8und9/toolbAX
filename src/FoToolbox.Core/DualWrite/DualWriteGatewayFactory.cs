using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite;

/// <summary>Builds an <see cref="IDualWriteGateway"/> bound to a connection's gateway URL + bearer token.</summary>
public interface IDualWriteGatewayFactory
{
    IDualWriteGateway Create(DualWriteConnectionSettings settings);
}

/// <summary>
/// Default factory: builds an <see cref="HttpClient"/> whose base address is the gateway root
/// and whose handler injects the pasted bearer token, then wraps it in a
/// <see cref="DualWriteGatewayClient"/>.
/// </summary>
public sealed class DualWriteGatewayFactory : IDualWriteGatewayFactory
{
    public IDualWriteGateway Create(DualWriteConnectionSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (string.IsNullOrWhiteSpace(settings.GatewayBaseUrl))
        {
            throw new InvalidOperationException("Gateway base URL is not configured.");
        }

        var baseUrl = settings.GatewayBaseUrl.TrimEnd('/') + "/";
        var http = new HttpClient(new BearerTokenHandler(settings.BearerToken))
        {
            BaseAddress = new Uri(baseUrl)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FoToolbox-DualWrite/0.1");
        return new DualWriteGatewayClient(http);
    }
}

internal sealed class BearerTokenHandler : DelegatingHandler
{
    private readonly string? _token;

    public BearerTokenHandler(string? token) : base(new HttpClientHandler())
    {
        _token = token;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
