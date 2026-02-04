using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Windows.Controls;

namespace HelloPlugin;

public sealed class HelloFoToolPlugin : IFoToolPlugin
{
    private IPluginContext? _context;

    public string Id => "fo.hello";

    public Version Version => new(0, 1, 0, 0);

    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Hello Plugin",
        Version = Version.ToString(),
        MinSdk = "0.1.0",
        Capabilities = new[] { "OData.Read" }
    };

    public Task InitializeAsync(IPluginContext context)
    {
        _context = context;
        _context.Logger.LogInformation("HelloFoToolPlugin initialized for {Env}", context.CurrentEnv.Name);
        return Task.CompletedTask;
    }

    public UserControl CreateTool()
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Plugin not initialized.");
        }

        return new HelloTool(new HelloToolViewModel(_context));
    }
}

public sealed class HelloToolViewModel
{
    public HelloToolViewModel(IPluginContext ctx)
    {
        Title = "Hello from FOtoolbox";
        EnvironmentName = ctx.CurrentEnv.Name;
        BaseUrl = ctx.CurrentEnv.BaseUrl;
        ODataStatus = ctx.OData is null ? "OData client not wired yet" : "OData client available";
    }

    public string Title { get; }
    public string EnvironmentName { get; }
    public string BaseUrl { get; }
    public string ODataStatus { get; }
}
