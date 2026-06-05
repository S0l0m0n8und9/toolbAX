using System.Linq;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.TestHelpers;
using Xunit;

namespace FoToolbox.Tests;

/// <summary>
/// Locks the canonical shape of the shared test seed (#39). Both FoToolbox.Tests and
/// FoToolbox.UiTests now build their fakes from <see cref="TestCatalogBuilder"/>, so this guards
/// the one source of truth against accidental drift.
/// </summary>
public class TestCatalogBuilderTests
{
    [Trait("Category", "TestHelpers")]
    [Fact]
    public void SeedMetadata_HasCanonicalCustomersEntity()
    {
        var entity = Assert.Single(TestCatalogBuilder.SeedMetadata().Entities);
        Assert.Equal("Customers", entity.Name);

        var account = Assert.Single(entity.Properties, p => p.Name == "AccountNumber");
        Assert.Equal("Edm.String", account.Type);
        Assert.False(account.Nullable);

        var name = Assert.Single(entity.Properties, p => p.Name == "Name");
        Assert.True(name.Nullable);
    }

    [Trait("Category", "TestHelpers")]
    [Fact]
    public async Task FakeCatalogService_SeedsFromTheSharedBuilder()
    {
        var env = new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF");
        var metadata = await new FakeCatalogService().GetODataMetadataAsync(env, CatalogRefreshMode.UseCacheIfAvailable);

        Assert.Equal("Customers", Assert.Single(metadata.Entities).Name);
    }
}
