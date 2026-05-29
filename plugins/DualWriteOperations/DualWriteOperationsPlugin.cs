using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;

namespace DualWriteOperationsPlugin;

public sealed class DualWriteOperationsPlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.dualwriteoperations";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Dual-write Operations",
        Version = Version.ToString(),
        MinSdk = "0.3.0",
        Icon = "DualWrite",
        Capabilities = new[] { "DualWrite.Operate" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("DualWriteOperations initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public UserControl CreateTool()
    {
        if (_ctx is null)
        {
            throw new InvalidOperationException("Not initialized");
        }

        return new DualWriteOperationsView(new DualWriteOperationsViewModel(_ctx));
    }
}
