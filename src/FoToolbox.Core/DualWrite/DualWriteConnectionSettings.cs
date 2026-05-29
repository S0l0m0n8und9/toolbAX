namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Decrypted, in-memory dual-write connection settings: the gateway base URL, the F&amp;O
/// identifier used to resolve the linkage, and the (already-decrypted) bearer token.
/// </summary>
public sealed record DualWriteConnectionSettings(string Key, string GatewayBaseUrl, string FoIdentifier, string? BearerToken)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(GatewayBaseUrl) &&
        !string.IsNullOrWhiteSpace(FoIdentifier) &&
        !string.IsNullOrWhiteSpace(BearerToken);
}
