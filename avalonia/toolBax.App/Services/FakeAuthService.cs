using System;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / test <see cref="IAuthService"/>: returns a placeholder token without contacting
/// Entra. A custom delegate can simulate a failure (throw) for negative tests.
/// </summary>
public sealed class FakeAuthService : IAuthService
{
    private readonly Func<EnvProfile, string> _token;
    private readonly Func<EnvProfile, string> _dataverseToken;

    public FakeAuthService(Func<EnvProfile, string>? token = null, Func<EnvProfile, string>? dataverseToken = null)
    {
        _token = token ?? (_ => "fake-fo-token");
        _dataverseToken = dataverseToken ?? (_ => "fake-dataverse-token");
    }

    public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) => Resolve(_token, env);

    public Task<string> AcquireDataverseTokenAsync(EnvProfile env, CancellationToken ct = default) => Resolve(_dataverseToken, env);

    // Surface a throwing delegate as a faulted task (TAP contract), not a synchronous throw.
    private static Task<string> Resolve(Func<EnvProfile, string> token, EnvProfile env)
    {
        try
        {
            return Task.FromResult(token(env));
        }
        catch (Exception ex)
        {
            return Task.FromException<string>(ex);
        }
    }
}
