using System;
using System.Threading.Tasks;
using System.Windows.Controls;
using FoToolbox.SDK.Plugins;

namespace UnsignedTestPlugin;

public sealed class UnsignedTestPlugin : IFoToolPlugin
{
    public string Id => "test.unsigned";

    public Version Version => new(0, 1, 0, 0);

    public FoPluginManifest Manifest => new()
    {
        Id = Id,
        Name = "Unsigned Test Plugin",
        Version = Version.ToString(),
        MinSdk = "0.2.0",
        Capabilities = new[] { "OData.Read" }
    };

    public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;

    public UserControl CreateTool() => new UserControl();
}
