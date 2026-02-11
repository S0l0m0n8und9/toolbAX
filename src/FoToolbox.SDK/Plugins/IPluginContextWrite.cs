using FoToolbox.Core.OData;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional plugin context extension for write-capable operations.
/// Plugins should cast <see cref="IPluginContext"/> to this interface when they require OData writes.
/// </summary>
public interface IPluginContextWrite
{
    IODataWriteClient ODataWrite { get; }
}

