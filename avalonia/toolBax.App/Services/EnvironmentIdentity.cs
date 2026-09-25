using System;
using FoToolbox.Core.Auth;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Immutable identity of the connection context behind an environment profile. Cosmetic profile fields
/// are deliberately excluded; every field that can change where or how live data is accessed is included.
/// </summary>
public sealed record EnvironmentIdentity(
    string ProfileId,
    string FoEndpoint,
    string DataverseEndpoint,
    string Tenant,
    string FoClientId,
    FoAuthMode FoAuthMode,
    string DataverseClientId,
    FoAuthMode DataverseAuthMode,
    string DefaultCompany)
{
    public static EnvironmentIdentity Create(EnvProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new EnvironmentIdentity(
            profile.Id,
            NormalizeUrl(ResourceUrlNormalizer.NormalizeFoBaseUrl(profile.Url)),
            NormalizeUrl(ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(profile.DataverseUrl ?? string.Empty)),
            NormalizeIdentifier(profile.Tenant),
            NormalizeIdentifier(profile.ClientId),
            profile.AuthMode,
            NormalizeIdentifier(profile.DataverseClientId),
            profile.DataverseAuthMode,
            profile.Legal);
    }

    public static EnvironmentIdentity? TryCreate(EnvProfile? profile) =>
        profile is null ? null : Create(profile);

    public bool IsCurrent(EnvProfile? current) => Equals(TryCreate(current));

    private static string NormalizeIdentifier(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    private static string NormalizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        var authority = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        var suffix = uri.PathAndQuery + uri.Fragment;
        return suffix == "/" ? authority : authority + suffix;
    }
}
