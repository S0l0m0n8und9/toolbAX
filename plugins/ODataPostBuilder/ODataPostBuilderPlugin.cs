using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;

namespace ODataPostBuilderPlugin;

public sealed class ODataPostBuilderPlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.odatapostbuilder";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "OData API Builder",
        Version = Version.ToString(),
        MinSdk = "0.3.0",
        Capabilities = new[] { "OData.Read", "OData.Write" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("OData API Builder initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public UserControl CreateTool()
    {
        if (_ctx is null) throw new InvalidOperationException("Not initialized");
        return new ODataPostBuilderView(new ODataPostBuilderViewModel(_ctx));
    }
}
