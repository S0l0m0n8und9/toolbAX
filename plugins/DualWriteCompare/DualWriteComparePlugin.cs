using FoToolbox.SDK.Plugins;
using FoToolbox.SDK.Wpf;
using Microsoft.Extensions.Logging;

namespace DualWriteComparePlugin;

public sealed class DualWriteComparePlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.dualwritecompare";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Dual-write Compare",
        Version = Version.ToString(),
        MinSdk = "0.3.0",
        Icon = "DualWrite",
        Capabilities = new[] { "DualWrite.Read" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("DualWriteCompare initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public IPluginView CreateTool()
    {
        if (_ctx is null)
        {
            throw new InvalidOperationException("Not initialized");
        }

        return new WpfPluginView(new DualWriteCompareView(new DualWriteCompareViewModel(_ctx)));
    }
}
