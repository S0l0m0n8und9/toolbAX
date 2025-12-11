using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;

namespace QueryBuilderPlugin;

public sealed class QueryBuilderPlugin : IFoToolPlugin
{
    private IPluginContext? _ctx;

    public string Id => "fo.querybuilder";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Query Builder",
        Version = Version.ToString(),
        MinSdk = "0.1.0",
        Capabilities = new[] { "OData.Read" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _ctx = context;
        _ctx.Logger.LogInformation("Query Builder initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public UserControl CreateTool()
    {
        if (_ctx is null) throw new InvalidOperationException("Not initialized");
        return new QueryBuilderView(new QueryBuilderViewModel(_ctx));
    }
}
