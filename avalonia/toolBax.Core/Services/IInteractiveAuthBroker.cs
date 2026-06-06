using System.Threading;
using System.Threading.Tasks;

namespace ToolBax.Core.Services;

/// <summary>Result of an interactive sign-in: the account that authenticated.</summary>
public sealed record AuthResult(string Account);

/// <summary>
/// Acquires a delegated token via interactive (browser) sign-in — WebView2 on Windows. Kept behind
/// an interface so the Profiles/Data-Integrator view models stay platform-neutral and headless-
/// testable (a fake returns a result without launching a browser). Returns null if the user cancels.
/// </summary>
public interface IInteractiveAuthBroker
{
    Task<AuthResult?> SignInAsync(string clientId, string tenant, CancellationToken ct = default);
}
