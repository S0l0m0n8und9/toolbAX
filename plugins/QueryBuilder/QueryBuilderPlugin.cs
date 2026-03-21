using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Windows.Controls;

namespace QueryBuilderPlugin;

public sealed class QueryBuilderPlugin : IFoToolPlugin, IFoToolPluginNavigation
{
    private IPluginContext? _ctx;
    private QueryBuilderViewModel? _viewModel;

    public string Id => "fo.querybuilder";
    public Version Version => new(0, 1, 0, 0);
    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Query Builder",
        Version = Version.ToString(),
        MinSdk = "0.2.0",
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
        _viewModel = new QueryBuilderViewModel(_ctx);
        return new QueryBuilderView(_viewModel);
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
