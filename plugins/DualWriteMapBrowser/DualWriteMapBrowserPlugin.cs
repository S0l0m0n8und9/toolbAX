using FoToolbox.SDK.Plugins;
using FoToolbox.SDK.Wpf;
using Microsoft.Extensions.Logging;

namespace DualWriteMapBrowserPlugin;

public sealed class DualWriteMapBrowserPlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.dualwritemapbrowser";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Dual-write Map Browser",
        Version = Version.ToString(),
        MinSdk = "0.2.0",
        Capabilities = new[] { "Dataverse.Read" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("DualWriteMapBrowser initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public IPluginView CreateTool()
    {
        if (_ctx is null)
        {
            throw new InvalidOperationException("Not initialized");
        }

        return new WpfPluginView(new DualWriteMapBrowserView(new DualWriteMapBrowserViewModel(_ctx)));
    }
}
