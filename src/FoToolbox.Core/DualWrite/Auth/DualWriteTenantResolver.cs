using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Resolves tenant domains through Entra's fixed-host OIDC metadata endpoint.</summary>
public sealed class DualWriteTenantResolver
    : IDualWriteTenantResolver
{
    private const int MaximumBodyBytes = 64 * 1024;
    private static readonly HttpClient DefaultHttpClient = new(CreateTransport());
    private readonly HttpClient _http;
    private readonly TimeSpan _timeout;

    public DualWriteTenantResolver(HttpClient? http = null, TimeSpan? timeout = null)
    {
        _http = http ?? DefaultHttpClient;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public async Task<Guid?> ResolveAsync(string domain, CancellationToken cancellationToken)
    {
        if (!IsValidDomain(domain))
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            var uri = new Uri(
                $"https://login.microsoftonline.com/{Uri.EscapeDataString(domain)}/v2.0/.well-known/openid-configuration");
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content is null ||
                response.Content.Headers.ContentLength > MaximumBodyBytes)
            {
                return null;
            }

            var body = await ReadBoundedAsync(response.Content, timeout.Token).ConfigureAwait(false);
            if (body is null)
            {
                return null;
            }

            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("issuer", out var issuer) &&
                   issuer.ValueKind == JsonValueKind.String &&
                   DualWriteTokenResponseValidator.TryParseV2Issuer(issuer.GetString(), out var tenant)
                ? tenant
                : null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static HttpClientHandler CreateTransport() => new() { AllowAutoRedirect = false };

    private static bool IsValidDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253 ||
            domain.Contains('/') || domain.Contains('\\') || domain.Contains(':') ||
            Uri.CheckHostName(domain) != UriHostNameType.Dns)
        {
            return false;
        }

        var labels = domain.Split('.', StringSplitOptions.None);
        if (labels.Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-')
            {
                return false;
            }

            foreach (var character in label)
            {
                if (!(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static async Task<string?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaximumBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
