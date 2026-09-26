using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
public sealed class DualWriteGatewayClient : IDualWriteGateway, IDisposable
{
    public const string ApiBasePath = "api/DualWriteManagement/1.0/";

    private readonly HttpClient _http;
    // Dispose the HttpClient only when we own it (the factory creates a dedicated one). An injected
    // client (e.g. a test's shared HttpClient) stays the caller's to dispose.
    private readonly bool _ownsHttp;

    public DualWriteGatewayClient(HttpClient http, bool ownsHttpClient = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsHttp = ownsHttpClient;
    }

    /// <summary>Resolves the F&amp;O environment identifier to its dual-write linkage (cid/cname).</summary>
    public async Task<DualWriteEnvironment> GetEnvironmentAsync(string foIdentifier, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(foIdentifier))
        {
            throw new ArgumentException("An F&O environment identifier is required.", nameof(foIdentifier));
        }

        // The gateway's Environments lookup keys on the bare host name, NOT a full URL. Sending the
        // scheme/path (e.g. "https://x/") returns an empty list. Mirror the MS reference tool, which
        // normalizes via UriBuilder(url).Uri.Host (DWLibary ArgsHandler.parseUriHostname).
        var identifier = NormalizeToHost(foIdentifier);

        var uri = $"{ApiBasePath}Environments?targetType=AX&identifier={Uri.EscapeDataString(identifier)}";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteResponseParser.ParseEnvironment(json, identifier);
    }

    private static string NormalizeToHost(string identifier)
    {
        var trimmed = identifier.Trim();
        try
        {
            // UriBuilder parses a bare multi-label host correctly (Host == the host). A few inputs
            // (e.g. "host:8080" with no scheme) parse to an empty Host; fall back to the raw value
            // rather than send the gateway an empty identifier.
            var host = new UriBuilder(trimmed).Uri.Host;
            return string.IsNullOrEmpty(host) ? trimmed : host;
        }
        catch
        {
            return trimmed;
        }
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
        string body;
        try { body = MapActionPayloadBuilder.Build(action, maps, cid); }
        catch (ArgumentException ex) { throw new DualWriteMutationValidationException(ex); }
        var uri = $"{ApiBasePath}Start";
        var result = await SendMutationAsync(HttpMethod.Post, uri, body, cancellationToken).ConfigureAwait(false);
        try { return DualWriteResponseParser.ParseActionResponse(result.Content) with { Acknowledgment = result.Evidence }; }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new DualWriteMutationException(result.Evidence with { FailureKind = ex.GetType().Name }, ex); }
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
            throw new DualWriteMutationValidationException(new ArgumentException("A connection id (cid) is required.", nameof(cid)));
        }

        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new DualWriteMutationValidationException(new ArgumentException("A project id (pid) is required.", nameof(projectId)));
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new DualWriteMutationValidationException(new ArgumentException("A template id is required.", nameof(templateId)));
        }

        var uri = $"{ApiBasePath}SolutionAware/{Uri.EscapeDataString(cid)}/SwitchActive/{Uri.EscapeDataString(templateId)}?pid={Uri.EscapeDataString(projectId)}";
        var result = await SendMutationAsync(HttpMethod.Post, uri, templateId, cancellationToken).ConfigureAwait(false);
        var body = result.Content;
        var trimmed = body?.TrimStart() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return new DualWriteActionResponse(string.Empty, null) { Acknowledgment = result.Evidence };
        }

        // The gateway may answer with a JSON object ({requestId,...}) or a bare id string.
        try
        {
            return (trimmed[0] is '{' or '['
                ? DualWriteResponseParser.ParseActionResponse(body!)
                : new DualWriteActionResponse(trimmed.Trim('"'), null)) with { Acknowledgment = result.Evidence };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        { throw new DualWriteMutationException(result.Evidence with { FailureKind = ex.GetType().Name }, ex); }
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
            throw new DualWriteMutationValidationException(new ArgumentException("A field mapping name is required.", nameof(fieldMappingName)));
        }

        var uri = $"api/Project/{Uri.EscapeDataString(fieldMappingName)}/Refresh";
        await SendAsync(HttpMethod.Post, uri, "{\"tokens\":[\"\"]}", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the connection set by name.
    /// <c>GET api/ConnectionSet/{cname}</c> (host-root, per <c>DWCommonEngine.getConnectionSet</c>).
    /// </summary>
    public async Task<DualWriteConnectionSet> GetConnectionSetAsync(string cname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cname))
        {
            throw new ArgumentException("A connection set name (cname) is required.", nameof(cname));
        }

        var uri = $"api/ConnectionSet/{Uri.EscapeDataString(cname)}";
        var json = await SendAsync(HttpMethod.Get, uri, null, cancellationToken).ConfigureAwait(false);
        return DualWriteConnectionSetParser.Parse(json);
    }

    /// <summary>
    /// Resets the dual-write link for the chosen legal entities.
    /// <c>POST api/ConnectionSet/{cid}/Reset?targetType=AX&amp;forceReset={bool}</c> with a
    /// <see cref="ResetLinkPayloadBuilder"/> body (per <c>ResetLinkEngine.sendResetLinkPayload</c>).
    /// </summary>
    public async Task ResetLinksAsync(
        string cid,
        DualWriteConnectionSet connectionSet,
        IReadOnlyList<string> legalEntities,
        bool forceReset,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(cid))
        {
            throw new DualWriteMutationValidationException(new ArgumentException("A connection id (cid) is required.", nameof(cid)));
        }

        if (connectionSet is null)
        {
            throw new DualWriteMutationValidationException(new ArgumentNullException(nameof(connectionSet)));
        }

        var body = ResetLinkPayloadBuilder.Build(connectionSet, legalEntities ?? Array.Empty<string>());
        var force = forceReset ? "true" : "false";
        var uri = $"api/ConnectionSet/{Uri.EscapeDataString(cid)}/Reset?targetType=AX&forceReset={force}";
        await SendAsync(HttpMethod.Post, uri, body, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies integration keys for a CE entity.
    /// <c>POST api/dataset/{datasetName}/IntegrationKeys</c> with body
    /// <c>{ integrationKeys: { "&lt;ceEntity&gt;": [keyFields] }, datasetName }</c>
    /// (host-root, per <c>DWMapEngine.applyIntegrationKeys</c> + <c>DWIntegrationKeyUpdate</c>).
    /// </summary>
    public async Task ApplyIntegrationKeysAsync(
        string datasetName,
        string ceEntityName,
        IReadOnlyList<string> keyFields,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetName))
        {
            throw new DualWriteMutationValidationException(new ArgumentException("A dataset name is required.", nameof(datasetName)));
        }

        if (string.IsNullOrWhiteSpace(ceEntityName))
        {
            throw new DualWriteMutationValidationException(new ArgumentException("A CE entity name is required.", nameof(ceEntityName)));
        }

        var payload = new Dictionary<string, object?>
        {
            ["integrationKeys"] = new Dictionary<string, IReadOnlyList<string>>
            {
                [ceEntityName] = keyFields ?? Array.Empty<string>()
            },
            ["datasetName"] = datasetName
        };

        var body = JsonSerializer.Serialize(payload);
        var uri = $"api/dataset/{Uri.EscapeDataString(datasetName)}/IntegrationKeys";
        await SendAsync(HttpMethod.Post, uri, body, cancellationToken).ConfigureAwait(false);
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
        if (method != HttpMethod.Get)
            return (await SendMutationAsync(method, relativeUri, jsonBody, cancellationToken).ConfigureAwait(false)).Content;
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

    private async Task<(string Content, DualWriteMutationEvidence Evidence)> SendMutationAsync(
        HttpMethod method, string relativeUri, string? body, CancellationToken cancellationToken)
    {
        var evidence = new DualWriteMutationEvidence(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(method, relativeUri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_http.Timeout != Timeout.InfiniteTimeSpan) deadline.CancelAfter(_http.Timeout);
            deadline.Token.ThrowIfCancellationRequested();
            // Auth handlers live inside this opaque pipeline. Crossing it means only may-have-been-sent.
            evidence = evidence with { DispatchStarted = true };
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            evidence = evidence with { StatusCode = (int)response.StatusCode, Headers = CollectHeaders(response) };
            await response.Content.LoadIntoBufferAsync(_http.MaxResponseContentBufferSize, deadline.Token).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            evidence = evidence with { BodyComplete = true };
            if (!response.IsSuccessStatusCode)
                throw new DualWriteGatewayException(
                    $"Dual-write gateway request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Trim(content)}",
                    response.StatusCode) { Evidence = evidence };
            return (content, evidence);
        }
        catch (DualWriteGatewayException) when (evidence.BodyComplete) { throw; }
        catch (OperationCanceledException ex)
        {
            throw new DualWriteMutationCanceledException(evidence with { FailureKind = ex.GetType().Name }, ex,
                cancellationToken.IsCancellationRequested ? cancellationToken : ex.CancellationToken);
        }
        catch (Exception ex)
        { throw new DualWriteMutationException(evidence with { FailureKind = ex.GetType().Name }, ex); }
    }

    private static IReadOnlyDictionary<string, string> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
        foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(headers);
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

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}

/// <summary>Raised when the gateway returns a non-success status code.</summary>
public sealed class DualWriteGatewayException : Exception, IDualWriteMutationFailure
{
    public DualWriteGatewayException(string message, System.Net.HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
    public DualWriteMutationEvidence? Evidence { get; init; }
}
