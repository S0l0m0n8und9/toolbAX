using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using FoToolbox.Core.DualWrite;
using FoToolbox.Core.Net;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

public sealed record WriteScope(EnvironmentIdentity? Identity, string Name, string Url)
{
    public static WriteScope Capture(EnvProfile? env) => new(EnvironmentIdentity.TryCreate(env), env?.Name ?? "Unbound environment", env?.Url ?? "");
    public string Display => $"{Name} ({Url})";
}

public sealed record WriteObservation(bool? DispatchStarted, int? StatusCode = null, bool BodyComplete = true,
    bool GatewayAcknowledged = false, int? ElapsedMs = null)
{
    public static WriteObservation From(ODataResponse response) => new(response.DispatchStarted,
        response.DispatchStarted == false || response.StatusCode == 0 ? null : response.StatusCode,
        response.BodyComplete,
        ElapsedMs: response.DispatchStarted == false || response.StatusCode <= 0 ? null : response.ElapsedMs);
    public static WriteObservation From(DualWriteMutationEvidence? evidence) => new(evidence?.DispatchStarted, evidence?.StatusCode, evidence?.BodyComplete ?? true);
    public bool Unconfirmed => DispatchStarted != false && (StatusCode is null or >= 500 or 202 || !BodyComplete);
    public string Summary
    {
        get
        {
            var summary = DispatchStarted == false ? "Not sent." : StatusCode is null ? GatewayAcknowledged
                ? "Gateway acknowledged submission — terminal outcome unconfirmed; HTTP status not provided."
                : "Outcome unknown — the write may have been applied." :
                StatusCode is >= 200 and < 300 ? $"HTTP {StatusCode} acknowledged" + (BodyComplete && StatusCode != 202 ? "." : " — outcome unconfirmed; response incomplete or asynchronous.") :
                $"HTTP {StatusCode} observed" + (StatusCode >= 500 || !BodyComplete ? " — outcome unconfirmed; the write may have been applied." : "; inspect the response before a new attempt.");
            return ElapsedMs is { } elapsed ? $"{summary} Observed response time: {elapsed} ms." : summary;
        }
    }
}

public sealed record PostWriteReceipt(WriteScope Scope, string Method, string Target, DateTimeOffset StartedAt,
    WriteObservation Observation, string? Locator = null)
{
    public Guid AttemptId { get; init; } = Guid.NewGuid();
    public string Summary => $"{Scope.Display} · {Method} {Target} · {StartedAt:O}\n{Observation.Summary}";
    public string? ReadbackTarget(out string reason)
    {
        reason = "";
        var candidate = Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? Locator : Target;
        if (Scope.Identity is null || string.IsNullOrWhiteSpace(candidate))
        { reason = $"No safe server locator. Use Query Builder in {Scope.Display} to inspect {Target}; attempt started {StartedAt:O}. Do not guess a record ID or resend automatically."; return null; }
        // The app's PATCH/DELETE transport appends relative paths to the complete normalized F&O root,
        // including a proxy path prefix. Server POST locators retain normal URI-reference semantics.
        if (!Method.Equals("POST", StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            candidate = Scope.Identity.FoEndpoint.TrimEnd('/') + "/" + candidate.TrimStart('/');
        if (!Uri.TryCreate(Scope.Identity.FoEndpoint.TrimEnd('/') + "/", UriKind.Absolute, out var root) ||
            !Uri.TryCreate(root, candidate, out var uri) || !RequestOriginGuard.IsSameOrigin(Scope.Identity.FoEndpoint, uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
        { reason = "Readback refused: the captured locator must have the same origin and no user information or fragment. Inspect the captured target manually in Query Builder."; return null; }
        return uri.AbsoluteUri;
    }
}

public sealed record CapturedWriteTarget(string Id, string Name, string ProjectId);
public sealed record LifecycleWriteReceipt(WriteScope Scope, string Cid, string Action, DateTimeOffset StartedAt,
    ImmutableArray<CapturedWriteTarget> Targets, WriteObservation Observation, string? RequestId = null,
    string? TerminalState = null, bool? TerminalSuccess = null, string? Diagnostic = null)
{
    public Guid AttemptId { get; init; } = Guid.NewGuid();
    public bool Unconfirmed => Observation.DispatchStarted != false && TerminalSuccess is null;
    public string Summary => $"{Scope.Display} · {Action} · {StartedAt:O}\n" +
        string.Join("; ", Targets.Select(t => $"{t.Name} · {t.Id} · {t.ProjectId}")) + "\n" + (TerminalSuccess is null ? Observation.Summary : $"Terminal gateway status: {TerminalState} ({(TerminalSuccess == true ? "completed" : "failed")}).") +
        (string.IsNullOrWhiteSpace(RequestId) ? "" : $" Request {RequestId}.") +
        (string.IsNullOrWhiteSpace(Diagnostic) ? "" : $"\nGateway detail: {Diagnostic}");
}
public sealed record DebugProjectAttempt(string ProjectId, bool DesiredValue, string Stage = "Not attempted",
    WriteObservation? Observation = null, string? Diagnostic = null)
{
    public string Summary => $"{ProjectId}: {(Stage == "Pending" ? "Pending — outcome not yet observed." : Observation?.Summary ?? Stage)}" +
        (string.IsNullOrWhiteSpace(Diagnostic) ? "" : $" {Diagnostic}");
}
public sealed record DebugWriteReceipt(WriteScope Scope, string Cid, DateTimeOffset StartedAt,
    ImmutableArray<DebugProjectAttempt> Projects, string? EntitySet = null)
{
    public Guid AttemptId { get; init; } = Guid.NewGuid();
    public bool Unconfirmed => Projects.Any(p => p.Observation?.Unconfirmed == true);
    public string Summary => $"{Scope.Display} · debug {(Projects[0].DesiredValue ? "enable" : "disable")} · {StartedAt:O}\n" + string.Join("\n", Projects.Select(p => p.Summary));
}

internal static class WriteUiEvidence
{
    private const int MaximumLength = 320;

    internal static string FromException(Exception exception) =>
        $"{exception.GetType().Name}: {Bound(exception.Message)}";

    internal static string FromGateway(DualWriteGatewayException exception) => Bound(exception.Message);

    internal static string FromReadResponse(ODataResponse response) => response.StatusCode > 0
        ? $"Read failed: HTTP {response.StatusCode} {Bound(response.ReasonPhrase)}."
        : $"Read failed: transport did not return an HTTP response ({Bound(response.ReasonPhrase)}).";

    private static string Bound(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "No additional detail.";
        var builder = new StringBuilder(value.Length);
        var previousSpace = false;
        foreach (var character in value)
        {
            if (char.IsControl(character) || char.IsWhiteSpace(character))
            {
                if (!previousSpace && builder.Length > 0) builder.Append(' ');
                previousSpace = true;
            }
            else
            {
                builder.Append(character);
                previousSpace = false;
            }
            if (builder.Length == MaximumLength) break;
        }
        var text = builder.ToString().Trim();
        return value.Length > MaximumLength ? text + "…" : text;
    }
}
