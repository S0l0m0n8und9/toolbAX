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
    private string? _cachedKey;

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public DataIntegratorTokenService(IDataIntegratorTokenAcquirer acquirer) =>
        _acquirer = acquirer ?? throw new ArgumentNullException(nameof(acquirer));

    public async Task<string> GetTokenAsync(DataIntegratorCredential credential, string tenantId, CancellationToken ct = default)
    {
        if (credential is null || !credential.IsComplete)
        {
            throw new DualWriteAuthException("No Data Integrator credential is configured. Set it in Profiles → Data Integrator.");
        }

        var key = $"{credential.ClientId}|{credential.Username}|{tenantId}";

        var cached = _cached;
        if (cached is not null && _cachedKey == key && !cached.IsExpired(Clock()))
        {
            return cached.AccessToken;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cached = _cached;
            if (cached is not null && _cachedKey == key && !cached.IsExpired(Clock()))
            {
                return cached.AccessToken;
            }

            var authority = $"{AuthorityBase}/{tenantId}";
            var token = await _acquirer.AcquireAsync(authority, credential.ClientId, ScopeDefault, credential.Username, credential.Password, ct).ConfigureAwait(false);
            _cachedKey = key;
            _cached = token;
            return token.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }
}
