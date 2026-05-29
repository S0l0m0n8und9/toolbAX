using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Typed client over the Dynamics 365 Dual-write Management gateway
/// (<c>/api/DualWriteManagement/1.0/</c>). The supplied <see cref="HttpClient"/> must have its
/// <see cref="HttpClient.BaseAddress"/> set to the gateway root (scheme + host) and is
/// responsible for attaching the bearer token — the client itself is auth-agnostic, so the
/// host can wire whatever token strategy it likes (pasted bearer now, delegated MSAL later).
/// </summary>
public sealed class DualWriteGatewayClient : IDualWriteGateway
{
    public const string ApiBasePath = "api/DualWriteManagement/1.0/";

    private readonly HttpClient _http;

    public DualWriteGatewayClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    /// <summary>Resolves the F&amp;O environment identifier to its dual-write linkage (cid/cname).</summary>
    public async Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(foIdentifier))
        {
            throw new ArgumentException("An F&O environment identifier is required.", nameof(foIdentifier));
        }

        var uri = $"{ApiBasePath}Environments?targetType=AX&identifier={Uri.EscapeDataString(foIdentifier)}";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseEnvironment(json, foIdentifier);
    }

    /// <summary>Lists all dual-write maps for the linkage, each with its template versions.</summary>
    public async Task<IReadOnlyList<DualWriteMap>> GetMapsAsync(string cid, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            throw new ArgumentException("A connection id (cid) is required.", nameof(cid));
        }

        var uri = $"{ApiBasePath}Entities?targetType=AX&cid={Uri.EscapeDataString(cid)}";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseMaps(json);
    }

    /// <summary>
    /// Submits a lifecycle action (start/stop/pause/resume/initial-sync) for the given maps.
    /// Returns the request id to poll via <see cref="GetStatusAsync"/>.
    /// </summary>
    public async Task<DualWriteActionResponse> StartActionAsync(
        DualWriteActionType action,
        IReadOnlyList<DualWriteMap> maps,
        string cid,
        CancellationToken cancellationToken = default)
    {
        var body = MapActionPayloadBuilder.Build(action, maps, cid);
        var uri = $"{ApiBasePath}Start";
        var json = await SendAsync(HttpMethod.Post, uri, body, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseActionResponse(json);
    }

    /// <summary>
    /// Activates the given template version for a map (the "apply map version" action).
    /// Mirrors <c>DWMapEngine.applyMapVersion</c>:
    /// <c>POST SolutionAware/{cid}/SwitchActive/{templateId}?pid={projectId}</c> with the raw
    /// template id as the body.
    /// </summary>
    public async Task<DualWriteActionResponse> SwitchActiveTemplateAsync(
        string cid,
        string projectId,
        string templateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            throw new ArgumentException("A connection id (cid) is required.", nameof(cid));
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("A project id (pid) is required.", nameof(projectId));
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("A template id is required.", nameof(templateId));
        }

        var uri = $"{ApiBasePath}SolutionAware/{Uri.EscapeDataString(cid)}/SwitchActive/{Uri.EscapeDataString(templateId)}?pid={Uri.EscapeDataString(projectId)}";
        var body = await SendAsync(HttpMethod.Post, uri, templateId, cancellationToken).ConfigureAwait(false);
        var trimmed = body?.TrimStart() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new DualWriteActionResponse(string.Empty, null);
        }

        // The gateway may answer with a JSON object ({requestId,...}) or a bare id string.
        return trimmed[0] is '{' or '['
            ? DualWriteResponseParser.ParseActionResponse(body!)
            : new DualWriteActionResponse(trimmed.Trim('"'), null);
    }

    /// <summary>
    /// Lists the field mappings for a project.
    /// <c>GET {pid}/FieldMappings</c> (per <c>DWCommonEngine.getFieldMappingForMaps</c>).
    /// </summary>
    public async Task<IReadOnlyList<DualWriteFieldMapping>> GetFieldMappingsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("A project id (pid) is required.", nameof(projectId));
        }

        var uri = $"{ApiBasePath}{Uri.EscapeDataString(projectId)}/FieldMappings";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseFieldMappings(json);
    }

    /// <summary>
    /// Refreshes table/entity metadata for a project field mapping.
    /// <c>POST api/Project/{fieldMappingName}/Refresh</c> with body <c>{"tokens":[""]}</c>
    /// (host-root path, per <c>DWMapEngine.refreshTable</c>).
    /// </summary>
    public async Task RefreshTablesAsync(string fieldMappingName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fieldMappingName))
        {
            throw new ArgumentException("A field mapping name is required.", nameof(fieldMappingName));
        }

        var uri = $"api/Project/{Uri.EscapeDataString(fieldMappingName)}/Refresh";
        await SendAsync(HttpMethod.Post, uri, "{\"tokens\":[\"\"]}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Polls the status of a previously submitted action request.</summary>
    public async Task<DualWriteRequestStatus> GetStatusAsync(string requestId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new ArgumentException("A request id is required.", nameof(requestId));
        }

        var uri = $"{ApiBasePath}Status/{Uri.EscapeDataString(requestId)}";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseStatus(json);
    }

    private async Task<string> SendAsync(HttpMethod method, string relativeUri, string? jsonBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var content = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DualWriteGatewayException(
                $"Dual-write gateway request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(content)}",
                response.StatusCode);
        }

        return content;
    }

    private static string Trim(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        const int max = 500;
        var collapsed = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return collapsed.Length <= max ? collapsed : collapsed.Substring(0, max) + "…";
    }
}

/// <summary>Raised when the gateway returns a non-success status code.</summary>
public sealed class DualWriteGatewayException : Exception
{
    public DualWriteGatewayException(string message, System.Net.HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
}
