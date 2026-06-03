using System;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>
/// Produces an IntegratorApp access token from a ROPC <see cref="DataIntegratorCredential"/>, caching
/// it in memory until it nears expiry (ROPC re-sends the password on every acquisition, so we cache).
/// </summary>
public sealed class DataIntegratorTokenService
{
    private const string ScopeDefault = "https://IntegratorApp.com/.default";
    private const string AuthorityBase = "https://login.microsoftonline.com";

    private readonly IDataIntegratorTokenAcquirer _acquirer;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile DualWriteToken? _cached;

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public DataIntegratorTokenService(IDataIntegratorTokenAcquirer acquirer) =>
        _acquirer = acquirer ?? throw new ArgumentNullException(nameof(acquirer));

    public async Task<string> GetTokenAsync(DataIntegratorCredential credential, string tenantId, CancellationToken ct = default)
    {
        if (credential is null || !credential.IsComplete)
        {
            throw new DualWriteAuthException("No Data Integrator credential is configured. Set it in Profiles → Data Integrator.");
        }

        if (_cached is not null && !_cached.IsExpired(Clock()))
        {
            return _cached.AccessToken;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached is not null && !_cached.IsExpired(Clock()))
            {
                return _cached.AccessToken;
            }

            var authority = $"{AuthorityBase}/{tenantId}";
            _cached = await _acquirer.AcquireAsync(authority, credential.ClientId, ScopeDefault, credential.Username, credential.Password, ct).ConfigureAwait(false);
            return _cached.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
