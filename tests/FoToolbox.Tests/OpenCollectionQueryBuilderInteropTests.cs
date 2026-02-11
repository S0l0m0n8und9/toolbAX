using QueryBuilderPlugin;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace FoToolbox.Tests;

public class OpenCollectionQueryBuilderInteropTests
{
    [Fact]
    public void ImportOpenCollection_Parses_GetUrl_IntoSavedQueryItem()
    {
        var json = """
                   {
                     "opencollection": "1.0.0",
                     "info": { "name": "Test" },
                     "items": [
                       {
                         "info": { "name": "Customers", "type": "http", "seq": 1 },
                         "http": {
                           "method": "GET",
                           "url": "https://contoso.operations.dynamics.com/data/CustomersV3?$select=AccountNumber,Name&$top=5&$count=true&$filter=dataAreaId%20eq%20'USMF'"
                         }
                       }
                     ],
                     "bundled": true
                   }
                   """;

        var store = new SavedQueryStore(Path.GetTempFileName());
        var imported = store.ImportOpenCollection(json, envId: "env", baseUrl: "https://contoso.operations.dynamics.com");
        var one = Assert.Single(imported);

        Assert.Equal("env", one.EnvId);
        Assert.Equal("CustomersV3", one.Entity);
        Assert.False(one.CrossCompany);
        Assert.Equal("USMF", one.Company);
        Assert.Null(one.FilterText); // extracted into Company
        Assert.True(one.Count);
        Assert.Equal(5, one.Top);
        Assert.Contains("AccountNumber", one.Select);
        Assert.Contains("Name", one.Select);
    }

    [Fact]
    public void ExportAllAsOpenCollection_Writes_CollectionDoc_WithGetUrls()
    {
        var store = new SavedQueryStore(Path.GetTempFileName());

        var items = new[]
        {
            new SavedQueryItem
            {
                EnvId = "env",
                Name = "Customers",
                Entity = "CustomersV3",
                CrossCompany = false,
                Company = "USMF",
                Select = new() { "AccountNumber", "Name" },
                Top = 5,
                Count = true
            }
        };

        var doc = store.ExportAllAsOpenCollection("Test", "https://contoso.operations.dynamics.com", items);

        Assert.Contains("\"opencollection\"", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"items\"", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/data/CustomersV3", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$select=AccountNumber,Name", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$top=5", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$count=true", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dataAreaId", doc, StringComparison.OrdinalIgnoreCase);
    }
}

