using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Profiles;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Persists the per-environment Data Integrator ROPC credential: the secret payload (clientId,
/// username, password) goes in the DPAPI vault; a settings row maps the env to its secret ref.
/// </summary>
public sealed class DataIntegratorCredentialStore
{
    private readonly ProfileStore _profiles;
    private readonly SecretVaultService _vault;

    public DataIntegratorCredentialStore(ProfileStore profiles, SecretVaultService vault)
    {
        _profiles = profiles;
        _vault = vault;
    }

    private static string Key(string envId) => $"DataIntegrator:{envId}";

    public async Task SaveAsync(string envId, DataIntegratorCredential credential, CancellationToken ct = default)
    {
        var secretRef = await _vault.StoreSecretAsync("DataIntegrator", new Payload
        {
            ClientId = credential.ClientId,
            Username = credential.Username,
            Password = credential.Password,
        }, ct);
        await _profiles.SetSettingAsync(Key(envId), secretRef, ct);
    }

    public async Task<DataIntegratorCredential?> GetAsync(string envId, CancellationToken ct = default)
    {
        var secretRef = await _profiles.GetSettingAsync(Key(envId), ct);
        if (string.IsNullOrWhiteSpace(secretRef)) return null;
        var payload = await _vault.ReadSecretAsync<Payload>(secretRef, ct);
        if (payload is null || string.IsNullOrWhiteSpace(payload.Username)) return null;
        return new DataIntegratorCredential(
            payload.ClientId ?? string.Empty,
            payload.Username ?? string.Empty,
            payload.Password ?? string.Empty);
    }

    private sealed class Payload
    {
        public string? ClientId { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
