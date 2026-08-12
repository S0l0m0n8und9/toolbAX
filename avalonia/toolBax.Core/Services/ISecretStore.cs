namespace ToolBax.Core.Services;

/// <summary>
/// Which of an environment's credentials a secret belongs to: the F&amp;O service principal, the
/// (separate) Dataverse app registration, or the Data Integrator service account — which has no service
/// principal at all and so is stored differently. Each is a distinct secret for the same environment id,
/// which is why the target (never the shape of the key) decides where a secret is read and written.
/// </summary>
public enum SecretTarget
{
    Fo,
    Dataverse,
    DataIntegrator,
}

/// <summary>
/// Stores per-environment secrets (e.g. a service-principal client secret), protected at rest
/// (DPAPI/CurrentUser on Windows). Plaintext is set and cleared but <b>never read back</b> into the
/// UI — only presence is queryable, so a stored secret can't be surfaced or logged. The <c>key</c> is
/// the environment id and the <c>target</c> selects which of that environment's credentials is meant
/// (see <see cref="SecretTarget"/>).
/// </summary>
public interface ISecretStore
{
    bool HasSecret(string key, SecretTarget target = SecretTarget.Fo);

    void SetSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo);

    void ClearSecret(string key, SecretTarget target = SecretTarget.Fo);
}
