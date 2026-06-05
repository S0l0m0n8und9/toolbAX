using FoToolbox.SDK.Plugins;
using FoToolbox.SDK.Wpf;
using Microsoft.Extensions.Logging;

namespace TableEntityBrowserPlugin;

public sealed class TableEntityBrowserPlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.tableentitybrowser";
    public Version Version => new(0, 1, 0, 0);

    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Table & Entity Browser",
        Version = Version.ToString(),
        MinSdk = "0.2.0",
        Capabilities = new[] { "OData.Read" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("TableEntityBrowser initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public IPluginView CreateTool()
    {
        if (_ctx is null) throw new InvalidOperationException("Not initialized");
        return new WpfPluginView(new TableEntityBrowserView(new TableEntityBrowserViewModel(_ctx)));
    }
}
