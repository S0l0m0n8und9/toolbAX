using System;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;

namespace FoToolbox.TestHelpers;

/// <summary>
/// Single source of truth for the seeded catalog/metadata used by tests. Both the unit-test
/// (<c>FoToolbox.Tests</c>) and binding-harness (<c>FoToolbox.UiTests</c>) fakes build their seed
/// data here so the shapes cannot drift apart (#39). Deterministic: no <see cref="DateTime.UtcNow"/>.
/// </summary>
public static class TestCatalogBuilder
{
    /// <summary>Fixed seed timestamp so snapshots/catalogs are reproducible across runs.</summary>
    public static readonly DateTime SeedTimeUtc = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The canonical seeded entity (a key string + one nullable string) used by tests.</summary>
    public static ODataEntity CustomersEntity() => new(
        "Customers",
        new[]
        {
            new ODataProperty("AccountNumber", "Edm.String", false),
            new ODataProperty("Name", "Edm.String", true),
        },
        Array.Empty<ODataNavigationProperty>());

    /// <summary>OData metadata seeded with <see cref="CustomersEntity"/>.</summary>
    public static ODataMetadata SeedMetadata() =>
        new(new[] { CustomersEntity() }, Array.Empty<ODataEnumType>(), null);

    /// <summary>The seeded (empty) table catalog.</summary>
    public static TableCatalog SeedTables() =>
        new("contoso", "Contoso", SeedTimeUtc, Array.Empty<TableInfo>());

    /// <summary>A catalog snapshot combining <see cref="SeedTables"/> and <see cref="SeedMetadata"/>.</summary>
    public static CatalogSnapshot SeedSnapshot(FoEnvironment env) =>
        new(env.Id, env.BaseUrl, SeedTables(), SeedMetadata(), SeedTimeUtc);
}
