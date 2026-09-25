using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FoToolbox.Tests;

public sealed class CatalogRetentionTests
{
    private sealed class Fixture
    {
        public string Db { get; } = Path.Combine(Path.GetTempPath(), $"retention-{Guid.NewGuid():N}.db");
        public CatalogStore Store { get; }
        public ProfileStore Profiles { get; } = new(Path.Combine(Path.GetTempPath(), $"retention-profile-{Guid.NewGuid():N}.db"));
        public Fixture() => Store = new CatalogStore(Db);
        public CatalogService Service(HttpMessageHandler handler) => new(new HttpClient(handler), Profiles, new CatalogStore(Db),
            new CatalogServiceOptions(TimeSpan.FromDays(1), TimeSpan.FromDays(1)));
        public async Task Sql(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={Db};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<List<(string Key, string Kind, string Json)>> Rows()
        {
            await using var connection = new SqliteConnection($"Data Source={Db};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT EnvId, Kind, PayloadJson FROM CatalogData ORDER BY EnvId, Kind";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<(string, string, string)>();
            while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            return rows;
        }
    }

    private static FoEnvironment Env(string partition, string id = "exact-profile") =>
        new(id, "Env", "https://contoso.operations.dynamics.com", "tenant", "USMF") { MetadataCachePartition = partition };
    private static string Key(string partition, string id = "exact-profile") =>
        "catalog-meta-v1:" + JsonSerializer.Serialize(new[] { id, "https://contoso.operations.dynamics.com", partition });
    private const string Xml = """
        <edmx:Edmx Version="4.0" xmlns:edmx="http://docs.oasis-open.org/odata/ns/edmx"><edmx:DataServices>
        <Schema Namespace="Default" xmlns="http://docs.oasis-open.org/odata/ns/edm">
        <EntityType Name="Customer"><Key><PropertyRef Name="Id"/></Key><Property Name="Id" Type="Edm.String"/></EntityType>
        <EntityContainer Name="Container"><EntitySet Name="Customers" EntityType="Default.Customer"/></EntityContainer>
        </Schema></edmx:DataServices></edmx:Edmx>
        """;

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task>? BeforeReply { get; set; }
        public int MetadataCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (BeforeReply is not null) await BeforeReply(request, ct);
            var metadata = request.RequestUri!.AbsolutePath.EndsWith("$metadata", StringComparison.Ordinal);
            if (metadata) Interlocked.Increment(ref MetadataCalls);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(metadata ? Xml : "{}") };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"same-etag\"");
            return response;
        }
    }

    [Fact]
    public async Task Successful_reads_bound_completed_metadata_partitions_to_three()
    {
        var fixture = new Fixture();
        var handler = new Handler();
        var service = fixture.Service(handler);
        for (var i = 0; i < 5; i++)
        {
            var index = await service.GetODataEntityIndexAsync(Env($"context-{i}"), CatalogRefreshMode.UseCacheIfFresh);
            Assert.Equal("Customers", Assert.Single(index.Entities).Name);
        }
        var rows = await fixture.Rows();
        Assert.Equal(3, rows.Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, handler.MetadataCalls);
    }

    [Fact]
    public async Task A_B_A_reuses_metadata_within_the_limit_without_changing_freshness()
    {
        var fixture = new Fixture();
        var handler = new Handler();
        var service = fixture.Service(handler);
        await service.GetODataEntityIndexAsync(Env("a"), CatalogRefreshMode.UseCacheIfFresh);
        var original = await fixture.Store.GetAsync(Key("a"), "ODataMetadataXml");
        await service.GetODataEntityIndexAsync(Env("b"), CatalogRefreshMode.UseCacheIfFresh);
        await service.GetODataEntityIndexAsync(Env("a"), CatalogRefreshMode.UseCacheIfFresh);

        Assert.Equal(2, handler.MetadataCalls);
        Assert.Equal(original!.UpdatedUtc, (await fixture.Store.GetAsync(Key("a"), "ODataMetadataXml"))!.UpdatedUtc);
        Assert.Equal(2, (await fixture.Rows()).Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Equal_timestamps_have_a_deterministic_key_tiebreak_and_other_rows_survive()
    {
        var fixture = new Fixture();
        await fixture.Store.EnsureCreatedAsync();
        var time = DateTime.UtcNow.AddMinutes(-1);
        foreach (var partition in new[] { "a", "b", "c", "d", "e" })
            await fixture.Store.SaveAsync(Key(partition), "ODataMetadataXml", "metadata-xml-v1", Xml, "same-etag", time);
        var sentinels = new[]
        {
            (Key("old", "EXACT-PROFILE"), "ODataMetadataXml"),
            (Key("old", "exact-profile-extra"), "ODataMetadataXml"),
            ("catalog-v2:[\"exact-profile\",\"https://contoso.operations.dynamics.com\"]", "ODataMetadataXml"),
            ("exact-profile|https://contoso.operations.dynamics.com", "Tables"),
            ("catalog-meta-v2:[\"exact-profile\",\"url\",\"partition\"]", "ODataMetadataXml"),
            ("catalog-meta-v1:not-json", "ODataMetadataXml"),
            ("catalog-meta-v1:[\"exact-profile\",\"url\"]", "ODataMetadataXml"),
            ("catalog-meta-v1:[\"exact-profile\",\"url\",null]", "ODataMetadataXml"),
            ("catalog-meta-v1:[\"exact-profile\",\"url\",\"p\",\"future\"]", "ODataMetadataXml"),
            (Key("e"), "FutureMetadata"),
            (Key("e"), "ODataEntityDetailsUnknown:Customer"),
            (Key("e"), "Tables"),
        };
        foreach (var (key, kind) in sentinels)
            await fixture.Store.SaveAsync(key, kind, "sentinel", "{\"source\":\"UserImport\"}", null, time);

        // Reading E saves an index with the SAME XML timestamp: retention must not manufacture recency.
        await fixture.Service(new Handler()).GetODataEntityIndexAsync(Env("e"), CatalogRefreshMode.UseCacheIfFresh);
        foreach (var (key, kind) in sentinels)
            Assert.Equal("{\"source\":\"UserImport\"}", (await fixture.Store.GetAsync(key, kind))!.PayloadJson);
        foreach (var p in new[] { "a", "b", "c" })
            Assert.Equal(time, (await fixture.Store.GetAsync(Key(p), "ODataMetadataXml"))!.UpdatedUtc);
        Assert.Null(await fixture.Store.GetAsync(Key("d"), "ODataMetadataXml"));
        Assert.Null(await fixture.Store.GetAsync(Key("e"), "ODataMetadataXml"));
        Assert.Null(await fixture.Store.GetAsync(Key("e"), "ODataEntityIndex"));
    }

    [Fact]
    public async Task Retention_ranks_the_full_fetched_timestamp_before_using_the_key_tiebreak()
    {
        var fixture = new Fixture();
        await fixture.Store.EnsureCreatedAsync();
        var time = DateTime.UtcNow.Date.AddHours(1);
        var names = new[] { "a", "b", "c", "d", "e" };
        for (var i = 0; i < names.Length; i++)
            await fixture.Store.SaveAsync(Key(names[i]), "ODataMetadataXml", "metadata-xml-v1", Xml, "same-etag", time.AddTicks(i));
        await fixture.Service(new Handler()).GetODataEntityIndexAsync(Env("e"), CatalogRefreshMode.UseCacheIfFresh);

        Assert.Null(await fixture.Store.GetAsync(Key("a"), "ODataMetadataXml"));
        Assert.Null(await fixture.Store.GetAsync(Key("b"), "ODataMetadataXml"));
        foreach (var p in new[] { "c", "d", "e" })
            Assert.NotNull(await fixture.Store.GetAsync(Key(p), "ODataMetadataXml"));
    }

    [Fact]
    public async Task Eviction_removes_all_known_representations_and_restart_fetches_again()
    {
        var fixture = new Fixture();
        var handler = new Handler();
        var service = fixture.Service(handler);
        await service.GetODataMetadataAsync(Env("old"), CatalogRefreshMode.UseCacheIfFresh);
        await service.GetODataEntityIndexAsync(Env("old"), CatalogRefreshMode.UseCacheIfFresh);
        await service.GetODataEntityDetailsAsync(Env("old"), "Customers", CatalogRefreshMode.UseCacheIfFresh);
        Assert.Equal(4, (await fixture.Rows()).Count(r => r.Key == Key("old")));
        foreach (var partition in new[] { "b", "c", "d" })
            await service.GetODataEntityIndexAsync(Env(partition), CatalogRefreshMode.UseCacheIfFresh);
        Assert.DoesNotContain(await fixture.Rows(), r => r.Key == Key("old"));

        var restartedHandler = new Handler();
        var restarted = fixture.Service(restartedHandler);
        var details = await restarted.GetODataEntityDetailsAsync(Env("old"), "Customers", CatalogRefreshMode.UseCacheIfFresh);
        Assert.Equal("Customers", details!.Name);
        Assert.Equal(1, restartedHandler.MetadataCalls);
        Assert.Equal(3, (await fixture.Rows()).Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Another_service_prune_invalidates_a_warmed_XML_memo()
    {
        var fixture = new Fixture();
        var aHandler = new Handler();
        var a = fixture.Service(aHandler);
        await a.GetODataEntityIndexAsync(Env("old"), CatalogRefreshMode.UseCacheIfFresh);
        // A distinct Store reached through a canonical-path alias must share retention coordination.
        var alias = Path.Combine(Path.GetDirectoryName(fixture.Db)!, ".", Path.GetFileName(fixture.Db));
        var b = new CatalogService(new HttpClient(new Handler()), fixture.Profiles, new CatalogStore(alias));
        foreach (var partition in new[] { "b", "c", "d" })
            await b.GetODataEntityIndexAsync(Env(partition), CatalogRefreshMode.UseCacheIfFresh);
        Assert.Null(await fixture.Store.GetAsync(Key("old"), "ODataMetadataXml"));

        Assert.NotNull(await a.GetODataEntityDetailsAsync(Env("old"), "Customers", CatalogRefreshMode.UseCacheIfFresh));
        Assert.Equal(2, aHandler.MetadataCalls);
        Assert.NotNull(await fixture.Store.GetAsync(Key("old"), "ODataMetadataXml"));
    }

    [Fact]
    public async Task Leased_fetch_and_canceled_waiter_survive_other_partitions_without_serializing_HTTP()
    {
        var fixture = new Fixture();
        await fixture.Store.EnsureCreatedAsync();
        await fixture.Store.SaveAsync(Key("held"), "ODataMetadataXml", "metadata-xml-v1", Xml, "same-etag", DateTime.UtcNow.AddDays(-2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler { BeforeReply = async (request, ct) =>
        {
            if (request.Options.TryGetValue(CatalogRequestContext.MetadataCachePartition, out var p) && p == "held")
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
        } };
        var service = fixture.Service(handler);
        using var firstCancellation = new CancellationTokenSource();
        using var waiterCancellation = new CancellationTokenSource();
        var first = service.GetODataMetadataAsync(Env("held"), CatalogRefreshMode.ForceRefresh, firstCancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var waiter = service.GetODataEntityIndexAsync(Env("held"), CatalogRefreshMode.ForceRefresh, waiterCancellation.Token);
        Assert.False(waiter.IsCompleted);

        // A separate service/store on the same path completes HTTP while the first is still held.
        var other = fixture.Service(new Handler());
        foreach (var p in new[] { "b", "c", "d", "e" })
            await other.GetODataEntityIndexAsync(Env(p), CatalogRefreshMode.UseCacheIfFresh).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(first.IsCompleted);
        Assert.NotNull(await fixture.Store.GetAsync(Key("held"), "ODataMetadataXml"));
        Assert.Equal(4, (await fixture.Rows()).Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());

        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(5)));
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Null(await fixture.Store.GetAsync(Key("held"), "ODataMetadataXml"));
        Assert.Equal(3, (await fixture.Rows()).Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task Faulted_fetch_releases_its_partition_and_preserves_the_original_error()
    {
        var fixture = new Fixture();
        await fixture.Store.EnsureCreatedAsync();
        await fixture.Store.SaveAsync(Key("old"), "ODataMetadataXml", "metadata-xml-v1", Xml, "same-etag", DateTime.UtcNow.AddDays(-2));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expected = new HttpRequestException("original fetch failure");
        var failing = fixture.Service(new Handler { BeforeReply = async (_, _) => { entered.TrySetResult(); await failure.Task; throw expected; } });
        var pending = failing.GetODataEntityIndexAsync(Env("old"), CatalogRefreshMode.ForceRefresh);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = fixture.Service(new Handler());
        foreach (var p in new[] { "b", "c", "d" })
            await other.GetODataEntityIndexAsync(Env(p), CatalogRefreshMode.UseCacheIfFresh);
        failure.TrySetResult();
        var observed = await Assert.ThrowsAsync<HttpRequestException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Same(expected, observed);
        Assert.Null(await fixture.Store.GetAsync(Key("old"), "ODataMetadataXml"));
    }

    [Fact]
    public async Task SQLite_cleanup_failure_keeps_success_and_rolls_back_the_entire_group_deletion()
    {
        var fixture = new Fixture();
        await fixture.Store.EnsureCreatedAsync();
        var time = DateTime.UtcNow.AddDays(-2);
        foreach (var p in new[] { "a", "b", "c", "d" })
        {
            await fixture.Store.SaveAsync(Key(p), "ODataMetadataXml", "metadata-xml-v1", Xml, "same-etag", time);
            await fixture.Store.SaveAsync(Key(p), "ODataEntityDetails:Customers", "details", "{}", "same-etag", time);
        }
        await fixture.Sql("CREATE TRIGGER BlockMetadataPrune BEFORE DELETE ON CatalogData WHEN OLD.Kind = 'ODataMetadataXml' BEGIN SELECT RAISE(ABORT, 'cleanup blocked'); END;");
        var good = await fixture.Service(new Handler()).GetODataEntityIndexAsync(Env("new"), CatalogRefreshMode.UseCacheIfFresh);
        Assert.Single(good.Entities);
        foreach (var p in new[] { "a", "b", "c", "d" })
        {
            Assert.NotNull(await fixture.Store.GetAsync(Key(p), "ODataMetadataXml"));
            Assert.NotNull(await fixture.Store.GetAsync(Key(p), "ODataEntityDetails:Customers"));
        }
        var original = new HttpRequestException("original failure");
        var failing = fixture.Service(new Handler { BeforeReply = (_, _) => Task.FromException(original) });
        Assert.Same(original, await Assert.ThrowsAsync<HttpRequestException>(() =>
            failing.GetODataEntityIndexAsync(Env("failed"), CatalogRefreshMode.ForceRefresh)));

        // No leaked pins from the failures: after removing the obstruction the next completion prunes.
        await fixture.Sql("DROP TRIGGER BlockMetadataPrune;");
        await fixture.Service(new Handler()).GetODataEntityIndexAsync(Env("next"), CatalogRefreshMode.UseCacheIfFresh);
        Assert.Equal(3, (await fixture.Rows()).Select(r => r.Key).Distinct(StringComparer.Ordinal).Count());
    }
}
