namespace ToolBax.Core.Services;

/// <summary>
/// Stores per-environment secrets (e.g. a service-principal client secret), protected at rest
/// (DPAPI/CurrentUser on Windows). Plaintext is set and cleared but <b>never read back</b> into the
/// UI — only presence is queryable, so a stored secret can't be surfaced or logged.
/// </summary>
public interface ISecretStore
{
    bool HasSecret(string key);

    void SetSecret(string key, string plaintext);

    void ClearSecret(string key);
}
