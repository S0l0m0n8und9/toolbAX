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

    public FakeAuthService(Func<EnvProfile, string>? token = null) =>
        _token = token ?? (_ => "fake-fo-token");

    public Task<string> AcquireFoTokenAsync(EnvProfile env, CancellationToken ct = default) =>
        Task.FromResult(_token(env));
}
