using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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

    internal static string NormalizeIdentifier(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Stable, versioned cache scope; only connection identity enters it, never credentials.</summary>
    public string ToMetadataCachePartition()
    {
        using var stream = new MemoryStream();
        // BinaryWriter prefixes are explicit little-endian byte counts, not culture-sensitive text or
        // delimiter joins. Names and values are both framed so opaque IDs cannot merge adjacent fields.
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            void Field(string name, string value)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                var valueBytes = Encoding.UTF8.GetBytes(value);
                writer.Write(nameBytes.Length);
                writer.Write(nameBytes);
                writer.Write(valueBytes.Length);
                writer.Write(valueBytes);
            }

            Field("version", "envmeta-v1");
            Field("profileId", ProfileId);
            Field("foEndpoint", FoEndpoint);
            Field("dataverseEndpoint", DataverseEndpoint);
            Field("tenant", Tenant);
            Field("foClientId", FoClientId);
            Field("foAuthMode", ((int)FoAuthMode).ToString(CultureInfo.InvariantCulture));
            Field("dataverseClientId", DataverseClientId);
            Field("dataverseAuthMode", ((int)DataverseAuthMode).ToString(CultureInfo.InvariantCulture));
            Field("defaultCompany", DefaultCompany);
        }

        return "envmeta-v1:" + Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

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
