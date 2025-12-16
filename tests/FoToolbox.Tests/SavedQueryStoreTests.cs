using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using QueryBuilderPlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class SavedQueryStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips_FilterTree()
    {
        var db = Path.GetTempFileName();
        var profileStore = new ProfileStore(db);
        await profileStore.EnsureCreatedAsync();
        await profileStore.UpsertEnvironmentAsync(new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF"));

        var store = new SavedQueryStore(db);

        var item = new SavedQueryItem
        {
            EnvId = "env",
            Name = "Test query",
            Entity = "Customers",
            CrossCompany = false,
            Company = "USMF",
            Select = new List<string> { "AccountNumber", "Name" },
            OrderBy = "AccountNumber asc",
            Count = true,
            FilterText = "AccountNumber eq 'A0001'",
            Expand = "SalesOrders",
            FilterRoot = new FilterGroupDto
            {
                LogicalOperator = "and",
                Children = new List<FilterDto>
                {
                    new FilterConditionDto { Field = "AccountNumber", Operator = "eq", Value = "A0001" },
                    new FilterGroupDto
                    {
                        LogicalOperator = "or",
                        Children = new List<FilterDto>
                        {
                            new FilterConditionDto { Field = "Name", Operator = "contains", Value = "Contoso" }
                        }
                    }
                }
            }
        };

        await store.SaveAsync(item);

        var loaded = (await store.LoadForEnvAsync("env")).Single();
        Assert.Equal("Test query", loaded.Name);
        Assert.Equal("Customers", loaded.Entity);
        Assert.Equal("USMF", loaded.Company);
        Assert.False(loaded.CrossCompany);
        Assert.Equal("AccountNumber asc", loaded.OrderBy);
        Assert.True(loaded.Count);
        Assert.Equal("SalesOrders", loaded.Expand);

        var root = Assert.IsType<FilterGroupDto>(loaded.FilterRoot);
        Assert.Equal("and", root.LogicalOperator);
        Assert.NotNull(root.Children);
        Assert.Equal(2, root.Children!.Count);

        var c0 = Assert.IsType<FilterConditionDto>(root.Children[0]);
        Assert.Equal("AccountNumber", c0.Field);
        Assert.Equal("eq", c0.Operator);
        Assert.Equal("A0001", c0.Value);

        var g1 = Assert.IsType<FilterGroupDto>(root.Children[1]);
        Assert.Equal("or", g1.LogicalOperator);
        Assert.NotNull(g1.Children);
        Assert.Single(g1.Children!);
    }

    [Fact]
    public async Task Load_LegacyFilterRoot_EmptyObject_DoesNotThrow()
    {
        var db = Path.GetTempFileName();
        var profileStore = new ProfileStore(db);
        await profileStore.EnsureCreatedAsync();
        await profileStore.UpsertEnvironmentAsync(new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF"));

        var record = new SavedQueryRecord(
            Id: Guid.NewGuid().ToString("N"),
            EnvId: "env",
            Name: "Legacy query",
            SpecJson: """
                      {
                        "EnvId": "env",
                        "Name": "Legacy query",
                        "Entity": "Customers",
                        "Select": [],
                        "CrossCompany": true,
                        "FilterRoot": {}
                      }
                      """,
            CrossCompany: true,
            CreatedUtc: DateTime.UtcNow.ToString("o"),
            UpdatedUtc: DateTime.UtcNow.ToString("o"));

        await profileStore.SaveQueryAsync(record);

        var store = new SavedQueryStore(db);
        var loaded = (await store.LoadForEnvAsync("env")).Single();
        Assert.Equal("Legacy query", loaded.Name);
        Assert.NotNull(loaded.FilterRoot);
        Assert.IsType<FilterConditionDto>(loaded.FilterRoot);
    }
}
