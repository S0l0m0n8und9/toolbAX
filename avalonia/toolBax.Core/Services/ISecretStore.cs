namespace ToolBax.Core.Services;

/// <summary>Which service principal a secret belongs to — F&amp;O or the (separate) Dataverse app reg.</summary>
public enum SecretTarget
{
    Fo,
    Dataverse,
}

/// <summary>
/// Stores per-environment secrets (e.g. a service-principal client secret), protected at rest
/// (DPAPI/CurrentUser on Windows). Plaintext is set and cleared but <b>never read back</b> into the
/// UI — only presence is queryable, so a stored secret can't be surfaced or logged. The
/// <c>target</c> selects the F&amp;O or Dataverse service principal (each has its own secret).
/// </summary>
public interface ISecretStore
{
    bool HasSecret(string key, SecretTarget target = SecretTarget.Fo);

    void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo);

    void ClearSecret(string key, SecretTarget target = SecretTarget.Fo);
}
