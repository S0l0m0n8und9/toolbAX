using FoToolbox.Core.Models;
using FoToolbox.Host.Plugins;
using FoToolbox.Core.OData;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using HelloPlugin;
using QueryBuilderPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Xunit;

namespace FoToolbox.Tests;

public sealed class PluginManagerTests
{
    private sealed class StubODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default)
            => ODataClientExtensions.EmptyPages(cancellationToken);
    }

    private sealed class StubCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null));

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default)
            => Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()), new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null), DateTime.UtcNow));

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default)
            => Task.FromResult(new TableCatalog("import", "UserImport", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default)
            => Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default)
            => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoEnvironment env, string tableName)
            => $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoEnvironment env, string entityName)
            => $"{env.BaseUrl}/data/{entityName}";
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger
    {
        public Exception? LastException { get; private set; }
        IDisposable Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        bool Microsoft.Extensions.Logging.ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        void Microsoft.Extensions.Logging.ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception != null)
            {
                LastException = exception;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();
            public void Dispose() { }
        }
    }

    private static FoEnvironment CreateEnv() =>
        new("dev", "Dev", "https://contoso.operations.dynamics.com", "00000000-0000-0000-0000-000000000000", "USMF");

    [Fact]
    public void Discover_Loads_HelloPlugin()
    {
        RunSta(() =>
        {
            var helloAssembly = typeof(HelloFoToolPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("helloplugin").FullName;
            File.Copy(helloAssembly, Path.Combine(pluginDir, Path.GetFileName(helloAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubCatalogService(), logger, new PluginTrustOptions(true, Array.Empty<string>()));
            var plugins = manager.Discover();

            if (plugins.Count == 0 && logger.LastException != null)
            {
                throw logger.LastException;
            }

            Assert.NotEmpty(plugins);
            var first = Assert.Single(plugins);
            Assert.Equal("fo.hello", first.Manifest.Id);
            Assert.NotNull(first.ToolControl);
        });
    }

    [Fact]
    public void Discover_Loads_QueryBuilderPlugin()
    {
        RunSta(() =>
        {
            var pluginAssembly = typeof(QueryBuilderPlugin.QueryBuilderPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("querybuilder").FullName;
            File.Copy(pluginAssembly, Path.Combine(pluginDir, Path.GetFileName(pluginAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubCatalogService(), logger, new PluginTrustOptions(true, Array.Empty<string>()));
            var plugins = manager.Discover();

            if (plugins.Count == 0 && logger.LastException != null)
            {
                throw logger.LastException;
            }

            Assert.NotEmpty(plugins);
            var first = Assert.Single(plugins);
            Assert.Equal("fo.querybuilder", first.Manifest.Id);
            Assert.NotNull(first.ToolControl);
        });
    }

    [Fact]
    public void ValidateManifest_Throws_When_MinSdk_TooHigh()
    {
        var manifest = new FoPluginManifest
        {
            Id = "test.bad",
            Name = "Bad",
            Version = "1.0.0",
            MinSdk = "9.9.9",
            Capabilities = Array.Empty<string>()
        };

        Assert.Throws<InvalidOperationException>(() => PluginManager.ValidateManifest(manifest));
    }

    [Fact]
    public void ValidateManifest_Throws_On_Invalid_MinSdk_Format()
    {
        var manifest = new FoPluginManifest
        {
            Id = "test.bad",
            Name = "Bad",
            Version = "1.0.0",
            MinSdk = "not-a-version",
            Capabilities = Array.Empty<string>()
        };

        Assert.Throws<InvalidOperationException>(() => PluginManager.ValidateManifest(manifest));
    }

    [Fact]
    public void Unsigned_Plugin_Blocked_When_Not_Allowed()
    {
        RunSta(() =>
        {
            var helloAssembly = typeof(HelloFoToolPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("helloplugin").FullName;
            File.Copy(helloAssembly, Path.Combine(pluginDir, Path.GetFileName(helloAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubCatalogService(), logger, new PluginTrustOptions(false, Array.Empty<string>()));
            var plugins = manager.Discover();

            Assert.Empty(plugins);
            Assert.NotNull(logger.LastException);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
        {
            throw failure;
        }
    }
}
