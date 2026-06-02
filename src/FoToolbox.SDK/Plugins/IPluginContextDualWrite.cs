using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional context extension for dual-write plugins: acquires a delegated token for the Data
/// Integrator (IntegratorApp) gateway from the active profile's credential. Cast
/// <see cref="IPluginContext"/> to this.
/// </summary>
public interface IPluginContextDualWrite
{
    Task<string> AcquireDataIntegratorTokenAsync(CancellationToken cancellationToken = default);
}
