using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

internal static class DualWriteTokenResponseValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> OptionalScopes = new(StringComparer.Ordinal)
    {
        "openid", "profile", "offline_access"
    };
    private static readonly HashSet<string> CriticalFormFields = new(StringComparer.Ordinal)
    {
        "client_id", "grant_type", "scope", "resource", "redirect_uri", "code", "refresh_token"
    };

    internal sealed record ValidatedResponse(
        string AccessToken,
        string? RefreshToken,
        DateTimeOffset ExpiresUtc,
        string? Scope,
        string? IdToken);

    internal static async Task<(DualWriteToken? Token, string Reason)> ValidateCaptureAsync(
        DualWriteTokenExchangeObservation observation,
        string? constraint,
        IDualWriteTenantResolver resolver,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (observation is null || observation.RequestUri is null ||
            observation.Method != HttpMethod.Post ||
            !DualWriteEndpointPolicy.IsTokenEndpoint(observation.RequestUri) ||
            observation.ResponseStatusCode is < 200 or > 299 ||
            !IsFormContentType(observation.RequestContentType) ||
            !TryParseForm(observation.RequestForm, out var form) ||
            !TryGetOne(form, "client_id", out var clientId) ||
            !string.Equals(clientId, DualWriteAuthConstants.ClientId, StringComparison.Ordinal) ||
            !TryGetOne(form, "grant_type", out var grantType) ||
            !(string.Equals(grantType, "authorization_code", StringComparison.Ordinal) ||
              string.Equals(grantType, "refresh_token", StringComparison.Ordinal)) ||
            !TryGetOne(form, grantType == "authorization_code" ? "code" : "refresh_token", out var credential) ||
            string.IsNullOrWhiteSpace(credential) ||
            !TryGetOne(form, "scope", out var requestedScope) ||
            !TryValidateScopes(requestedScope, out var canonicalScope) ||
            form.TryGetValue("resource", out var resources) &&
                (resources.Count != 1 ||
                 !string.Equals(resources[0], DualWriteAuthConstants.ResourceBaseUrl, StringComparison.Ordinal)) ||
            !TryParseResponse(observation.ResponseBody, now, out var response) ||
            response.Scope is not null &&
                (!TryValidateScopes(response.Scope, out var responseScope) ||
                 !ResponseResourceScopesFitRequest(responseScope, canonicalScope)))
        {
            return (null, "Token exchange was not accepted.");
        }

        if (!DualWriteEndpointPolicy.TryGetTokenTenant(observation.RequestUri, out var endpointTenant))
        {
            return (null, "Token exchange tenant was not accepted.");
        }

        var parsedConstraint = TenantConstraint.Parse(constraint);
        if (parsedConstraint.Kind == TenantConstraintKind.Invalid)
        {
            return (null, "Tenant constraint was not accepted.");
        }

        Guid actualTenant;
        var endpointIsGuid = Guid.TryParse(endpointTenant, out var endpointGuid) && endpointGuid != Guid.Empty;
        if (parsedConstraint.Kind == TenantConstraintKind.ExplicitGuid)
        {
            if (!endpointIsGuid || endpointGuid != parsedConstraint.TenantId)
            {
                return (null, "Token exchange tenant did not match the configured tenant.");
            }
            actualTenant = endpointGuid;
        }
        else if (parsedConstraint.Kind == TenantConstraintKind.Domain)
        {
            if (!string.Equals(endpointTenant, parsedConstraint.Domain, StringComparison.OrdinalIgnoreCase))
            {
                return (null, "Token exchange tenant did not match the configured domain.");
            }
            var resolved = await resolver.ResolveAsync(parsedConstraint.Domain!, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                return (null, "Tenant domain could not be resolved safely.");
            }
            actualTenant = resolved.Value;
        }
        else if (endpointIsGuid)
        {
            actualTenant = endpointGuid;
        }
        else if (string.Equals(endpointTenant, "common", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(endpointTenant, "organizations", StringComparison.OrdinalIgnoreCase))
        {
            actualTenant = Guid.Empty;
        }
        else
        {
            return (null, "Token exchange tenant was not an organizational authority.");
        }

        if (response.IdToken is not null)
        {
            if (!TryValidateIdToken(response.IdToken, DualWriteAuthConstants.ClientId, now, out var idTenant))
            {
                return (null, "Token response identity metadata was not accepted.");
            }
            if (actualTenant != Guid.Empty && idTenant != actualTenant)
            {
                return (null, "Token response tenant did not match the trusted exchange context.");
            }
            actualTenant = idTenant;
        }
        else if (actualTenant == Guid.Empty || parsedConstraint.Kind == TenantConstraintKind.Domain)
        {
            return (null, "Token response did not include required tenant metadata.");
        }

        var binding = new DualWriteDelegatedBinding(
            actualTenant,
            DualWriteAuthConstants.ClientId,
            DualWriteAuthConstants.ResourceBaseUrl,
            canonicalScope);
        return (new DualWriteToken(response.AccessToken, response.RefreshToken, response.ExpiresUtc)
        {
            Binding = binding
        }, string.Empty);
    }

    internal static bool TryValidateRefresh(
        string body,
        int statusCode,
        DualWriteDelegatedBinding binding,
        string previousRefreshToken,
        DateTimeOffset now,
        out DualWriteToken? token)
    {
        token = null;
        if (statusCode is < 200 or > 299 || !binding.IsTrusted ||
            !TryParseResponse(body, now, out var response))
        {
            return false;
        }

        if (response.Scope is not null &&
            (!TryValidateScopes(response.Scope, out var responseScope) ||
             !ResponseResourceScopesFitRequest(responseScope, binding.Scope)))
        {
            return false;
        }

        if (response.IdToken is not null &&
            (!TryValidateIdToken(response.IdToken, binding.ClientId, now, out var idTenant) ||
             idTenant != binding.TenantId))
        {
            return false;
        }

        token = new DualWriteToken(
            response.AccessToken,
            response.RefreshToken ?? previousRefreshToken,
            response.ExpiresUtc)
        {
            Binding = binding
        };
        return true;
    }

    internal static bool TryValidateScopes(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resourceScopes = new HashSet<string>(StringComparer.Ordinal);
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            if (!all.Add(token))
            {
                continue;
            }
            if (token.StartsWith(DualWriteAuthConstants.ResourceBaseUrl + "/", StringComparison.Ordinal) &&
                token.Length > DualWriteAuthConstants.ResourceBaseUrl.Length + 1)
            {
                resourceScopes.Add(token);
            }
            else if (!OptionalScopes.Contains(token))
            {
                return false;
            }
        }

        if (resourceScopes.Count == 0)
        {
            return false;
        }

        canonical = string.Join(' ', all.OrderBy(scope => scope, StringComparer.Ordinal));
        return true;
    }

    internal static bool TryParseV2Issuer(string? issuer, out Guid tenant)
    {
        tenant = Guid.Empty;
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.IdnHost, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped)
            .Split('/', StringSplitOptions.None);
        return segments.Length == 2 &&
               string.Equals(segments[1], "v2.0", StringComparison.Ordinal) &&
               Guid.TryParse(Uri.UnescapeDataString(segments[0]), out tenant) &&
               tenant != Guid.Empty;
    }

    private static bool TryParseResponse(
        string? body,
        DateTimeOffset now,
        out ValidatedResponse response)
    {
        response = null!;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "access_token", out var accessToken) ||
                !TryString(root, "token_type", out var tokenType) ||
                !string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                !TryPositiveSeconds(root, "expires_in", out var expiresIn) ||
                !TryOptionalString(root, "refresh_token", out var refreshToken) ||
                !TryOptionalString(root, "scope", out var scope) ||
                !TryOptionalString(root, "id_token", out var idToken))
            {
                return false;
            }

            response = new ValidatedResponse(
                accessToken,
                refreshToken,
                now.AddSeconds(expiresIn),
                scope,
                idToken);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryValidateIdToken(
        string token,
        string expectedClient,
        DateTimeOffset now,
        out Guid tenant)
    {
        tenant = Guid.Empty;
        var parts = token.Split('.');
        if (parts.Length != 3 || !TryDecodeBase64Url(parts[1], out var payload))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryString(root, "tid", out var tenantText) ||
                !Guid.TryParse(tenantText, out tenant) || tenant == Guid.Empty ||
                !TryString(root, "iss", out var issuer) ||
                !TryParseV2Issuer(issuer, out var issuerTenant) || issuerTenant != tenant ||
                !AudienceMatches(root, expectedClient) ||
                !AuthorizedPartyMatches(root, expectedClient) ||
                !TryUnixTime(root, "exp", out var expiry) ||
                !TryUnixTime(root, "iat", out var issued) ||
                expiry <= now - ClockSkew || expiry > now.AddHours(24) ||
                issued > now + ClockSkew || issued < now.AddHours(-24) || expiry < issued)
            {
                return false;
            }

            if (root.TryGetProperty("nbf", out var notBeforeElement))
            {
                if (!TryUnixTime(notBeforeElement, out var notBefore) ||
                    notBefore > now + ClockSkew || notBefore > expiry)
                {
                    return false;
                }
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseForm(string? form, out Dictionary<string, List<string>> fields)
    {
        fields = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(form))
        {
            return false;
        }
        foreach (var pair in form.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 1)
            {
                return false;
            }
            var key = DecodeForm(pair[..separator]);
            var value = DecodeForm(pair[(separator + 1)..]);
            if (key is null || value is null)
            {
                return false;
            }
            if (!fields.TryGetValue(key, out var values))
            {
                values = [];
                fields[key] = values;
            }
            values.Add(value);
            if (CriticalFormFields.Contains(key) && values.Count > 1)
            {
                return false;
            }
        }
        return true;
    }

    private static string? DecodeForm(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return null;
        }
    }

    private static bool TryGetOne(Dictionary<string, List<string>> fields, string key, out string value)
    {
        value = string.Empty;
        return fields.TryGetValue(key, out var values) && values.Count == 1 &&
               !string.IsNullOrWhiteSpace(value = values[0]);
    }

    private static bool IsFormContentType(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        string.Equals(contentType.Split(';', 2)[0].Trim(), "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private static bool ResponseResourceScopesFitRequest(string response, string request)
    {
        var requested = request.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        return response.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(scope => scope.StartsWith(DualWriteAuthConstants.ResourceBaseUrl + "/", StringComparison.Ordinal))
            .All(requested.Contains);
    }

    private static bool TryString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(value = element.GetString()!);
    }

    private static bool TryOptionalString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }
        value = element.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryPositiveSeconds(JsonElement root, string name, out int seconds)
    {
        seconds = 0;
        if (!root.TryGetProperty(name, out var element))
        {
            return false;
        }
        return ((element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out seconds)) ||
                (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out seconds))) &&
               seconds > 0;
    }

    private static bool AudienceMatches(JsonElement root, string client)
    {
        if (!root.TryGetProperty("aud", out var audience)) return false;
        if (audience.ValueKind == JsonValueKind.String) return string.Equals(audience.GetString(), client, StringComparison.Ordinal);
        if (audience.ValueKind != JsonValueKind.Array || audience.GetArrayLength() != 1) return false;
        var only = audience[0];
        return only.ValueKind == JsonValueKind.String && string.Equals(only.GetString(), client, StringComparison.Ordinal);
    }

    private static bool AuthorizedPartyMatches(JsonElement root, string client) =>
        !root.TryGetProperty("azp", out var party) ||
        party.ValueKind == JsonValueKind.String && string.Equals(party.GetString(), client, StringComparison.Ordinal);

    private static bool TryUnixTime(JsonElement root, string name, out DateTimeOffset value)
    {
        value = default;
        return root.TryGetProperty(name, out var element) && TryUnixTime(element, out value);
    }

    private static bool TryUnixTime(JsonElement element, out DateTimeOffset value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt64(out var seconds)) return false;
        try { value = DateTimeOffset.FromUnixTimeSeconds(seconds); return true; }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool TryDecodeBase64Url(string encoded, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var padded = encoded.Replace('-', '+').Replace('_', '/');
            padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", 0 => "", _ => throw new FormatException() };
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private enum TenantConstraintKind { AnyOrganization, ExplicitGuid, Domain, Invalid }
    private sealed record TenantConstraint(TenantConstraintKind Kind, Guid TenantId, string? Domain)
    {
        internal static TenantConstraint Parse(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed) ||
                string.Equals(trimmed, "common", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "organizations", StringComparison.OrdinalIgnoreCase))
                return new(TenantConstraintKind.AnyOrganization, Guid.Empty, null);
            if (Guid.TryParse(trimmed, out var tenant) && tenant != Guid.Empty)
                return new(TenantConstraintKind.ExplicitGuid, tenant, null);
            if (Uri.CheckHostName(trimmed) == UriHostNameType.Dns && trimmed.Contains('.') &&
                !trimmed.Contains('/') && !trimmed.Contains('\\') && !trimmed.Contains(':'))
                return new(TenantConstraintKind.Domain, Guid.Empty, trimmed.ToLowerInvariant());
            return new(TenantConstraintKind.Invalid, Guid.Empty, null);
        }
    }
}
