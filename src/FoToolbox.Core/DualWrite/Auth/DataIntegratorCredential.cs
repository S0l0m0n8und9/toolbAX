namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Decrypted, in-memory ROPC credential for the Dual-write (IntegratorApp) gateway. Persisted via the
/// host's DPAPI vault — never logged.
/// </summary>
public sealed record DataIntegratorCredential(string ClientId, string Username, string Password)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);
}
