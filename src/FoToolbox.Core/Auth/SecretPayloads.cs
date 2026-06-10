namespace FoToolbox.Core.Auth;

/// <summary>Vault payload for a stored client secret (WPF host shape: <c>{"Value":"…"}</c>).</summary>
public sealed class ClientSecretPayload
{
    public string? Value { get; set; }
}

/// <summary>Vault payload for a stored bearer token.</summary>
public sealed class BearerTokenPayload
{
    public string? AccessToken { get; set; }
    public string? ExpiresUtc { get; set; }
}
