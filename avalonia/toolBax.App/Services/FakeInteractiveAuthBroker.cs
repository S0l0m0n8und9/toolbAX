using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode <see cref="IInteractiveAuthBroker"/>: returns a delegated sign-in result without a
/// browser. TODO: replace with the WebView2-backed Windows broker.
/// </summary>
public sealed class FakeInteractiveAuthBroker : IInteractiveAuthBroker
{
    public Task<AuthResult?> SignInAsync(string clientId, string tenant, CancellationToken ct = default) =>
        Task.FromResult<AuthResult?>(new AuthResult("svc.dualwrite@contoso.com"));
}
