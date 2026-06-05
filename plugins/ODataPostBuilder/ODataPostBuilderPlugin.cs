using FoToolbox.SDK.Plugins;
using FoToolbox.SDK.Wpf;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace ODataPostBuilderPlugin;

public sealed class ODataPostBuilderPlugin : IFoToolPlugin, IFoToolPluginNavigation
{
    private IPluginContext? _ctx;
    private ODataPostBuilderViewModel? _viewModel;

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

    public IPluginView CreateTool()
    {
        if (_ctx is null) throw new InvalidOperationException("Not initialized");
        _viewModel = new ODataPostBuilderViewModel(_ctx);
        return new WpfPluginView(new ODataPostBuilderView(_viewModel));
    }

    public void OnNavigateTo(IReadOnlyDictionary<string, string> parameters)
    {
        if (_viewModel is null) return;
        if (parameters.TryGetValue("entity", out var entityName) && !string.IsNullOrWhiteSpace(entityName))
        {
            _viewModel.RequestEntity(entityName);
        }
    }
}
