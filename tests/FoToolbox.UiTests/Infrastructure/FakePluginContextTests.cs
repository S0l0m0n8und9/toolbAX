using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.UiTests.Infrastructure;

public class FakePluginContextTests
{
    [Fact]
    public async Task Context_exposes_all_optional_capabilities_and_seeded_metadata()
    {
        var ctx = new FakePluginContext();

        Assert.IsAssignableFrom<IPluginContextWrite>(ctx);
        Assert.IsAssignableFrom<IPluginContextDataverse>(ctx);
        Assert.IsAssignableFrom<IPluginContextNavigation>(ctx);

        var metadata = await ctx.Catalog.GetODataMetadataAsync(ctx.CurrentEnv, CatalogRefreshMode.UseCacheIfAvailable);
        Assert.Contains(metadata.Entities, e => e.Name == "Customers");
    }
}
