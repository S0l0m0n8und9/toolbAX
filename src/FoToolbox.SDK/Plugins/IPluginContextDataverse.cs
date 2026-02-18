using FoToolbox.Core.Models;
using System.Net.Http;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional plugin context extension for Dataverse-capable operations.
/// Plugins should cast <see cref="IPluginContext"/> to this interface when they require Dataverse access.
/// </summary>
public interface IPluginContextDataverse
{
    bool HasDataverseProfile { get; }
    DataverseEnvironment? CurrentDataverseEnv { get; }
    HttpClient? DataverseHttp { get; }
}
