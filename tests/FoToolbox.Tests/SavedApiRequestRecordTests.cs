using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class SavedApiRequestRecordTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();
        await store.UpsertEnvironmentAsync(new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF"));

        var id = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow.ToString("o");
        var record = new SavedApiRequestRecord(
            Id: id,
            EnvId: "env",
            Name: "Create customer",
            Method: "POST",
            Url: "https://contoso.operations.dynamics.com/data/CustomersV3",
            OpenCollectionJson: "{\"info\":{\"name\":\"Create customer\",\"type\":\"http\",\"seq\":1},\"http\":{\"method\":\"POST\",\"url\":\"https://contoso.operations.dynamics.com/data/CustomersV3\"}}",
            CreatedUtc: now,
            UpdatedUtc: now);

        await store.SaveApiRequestAsync(record);

        var loaded = (await store.GetSavedApiRequestsAsync("env")).Single();
        Assert.Equal(id, loaded.Id);
        Assert.Equal("env", loaded.EnvId);
        Assert.Equal("Create customer", loaded.Name);
        Assert.Equal("POST", loaded.Method);
        Assert.Contains("/data/CustomersV3", loaded.Url);
        Assert.Contains("\"http\"", loaded.OpenCollectionJson);
    }

    [Fact]
    public async Task Save_WithSameId_Updates()
    {
        var db = Path.GetTempFileName();
        var store = new ProfileStore(db);
        await store.EnsureCreatedAsync();
        await store.UpsertEnvironmentAsync(new FoEnvironment("env", "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF"));

        var id = Guid.NewGuid().ToString("N");
        var t0 = DateTime.UtcNow.AddMinutes(-2).ToString("o");
        var t1 = DateTime.UtcNow.ToString("o");

        await store.SaveApiRequestAsync(new SavedApiRequestRecord(
            Id: id,
            EnvId: "env",
            Name: "Req",
            Method: "POST",
            Url: "https://contoso.operations.dynamics.com/data/Foo",
            OpenCollectionJson: "{}",
            CreatedUtc: t0,
            UpdatedUtc: t0));

        await store.SaveApiRequestAsync(new SavedApiRequestRecord(
            Id: id,
            EnvId: "env",
            Name: "Req2",
            Method: "PATCH",
            Url: "https://contoso.operations.dynamics.com/data/Foo(1)",
            OpenCollectionJson: "{\"x\":1}",
            CreatedUtc: t0, // should not change
            UpdatedUtc: t1));

        var loaded = (await store.GetSavedApiRequestsAsync("env")).Single();
        Assert.Equal("Req2", loaded.Name);
        Assert.Equal("PATCH", loaded.Method);
        Assert.EndsWith("/data/Foo(1)", loaded.Url);
        Assert.Equal("{\"x\":1}", loaded.OpenCollectionJson);
        Assert.Equal(t0, loaded.CreatedUtc);
        Assert.Equal(t1, loaded.UpdatedUtc);
    }
}

