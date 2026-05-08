using FoToolbox.Core.Models;
using FoToolbox.Host.Plugins;
using FoToolbox.Core.OData;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using HelloPlugin;
using ODataPostBuilderPlugin;
using QueryBuilderPlugin;
using TableEntityBrowserPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private sealed class StubODataWriteClient : IODataWriteClient
    {
        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default) =>
            Task.FromResult(new ODataWriteResponse(200, null, new Dictionary<string, string>()));
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
        public List<(Microsoft.Extensions.Logging.LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        IDisposable Microsoft.Extensions.Logging.ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        bool Microsoft.Extensions.Logging.ILogger.IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        void Microsoft.Extensions.Logging.ILogger.Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
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

    private static readonly (string Name, Type PluginType, string Id)[] BundledPlugins =
    {
        ("HelloPlugin", typeof(HelloFoToolPlugin), "fo.hello"),
        ("QueryBuilder", typeof(QueryBuilderPlugin.QueryBuilderPlugin), "fo.querybuilder"),
        ("TableEntityBrowser", typeof(TableEntityBrowserPlugin.TableEntityBrowserPlugin), "fo.tableentitybrowser"),
        ("ODataPostBuilder", typeof(ODataPostBuilderPlugin.ODataPostBuilderPlugin), "fo.odatapostbuilder"),
        ("DualWriteMapBrowser", typeof(DualWriteMapBrowserPlugin.DualWriteMapBrowserPlugin), "fo.dualwritemapbrowser")
    };

    private static PluginManager CreateManager(string pluginRoot, CapturingLogger logger) =>
        new(pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(), logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));

    private static string StageCanonical(string pluginRoot, (string Name, Type PluginType, string Id) plugin)
    {
        var assemblyPath = plugin.PluginType.Assembly.Location;
        var pluginDir = Path.Combine(pluginRoot, plugin.Name);
        Directory.CreateDirectory(pluginDir);
        var stagedPath = Path.Combine(pluginDir, plugin.Name + ".dll");
        File.Copy(assemblyPath, stagedPath, overwrite: true);
        return stagedPath;
    }

    private static string StageFlat(string pluginRoot, (string Name, Type PluginType, string Id) plugin)
    {
        Directory.CreateDirectory(pluginRoot);
        var assemblyPath = plugin.PluginType.Assembly.Location;
        var stagedPath = Path.Combine(pluginRoot, plugin.Name + ".dll");
        File.Copy(assemblyPath, stagedPath, overwrite: true);
        return stagedPath;
    }

    private static string LoadedAssemblyPath(LoadedPlugin plugin) =>
        plugin.Instance.GetType().Assembly.Location;

    [Fact]
    public void Discover_Loads_HelloPlugin()
    {
        RunSta(async () =>
        {
            var helloAssembly = typeof(HelloFoToolPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("helloplugin").FullName;
            File.Copy(helloAssembly, Path.Combine(pluginDir, Path.GetFileName(helloAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(), logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));
            var plugins = await manager.DiscoverAsync();

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
        RunSta(async () =>
        {
            var pluginAssembly = typeof(QueryBuilderPlugin.QueryBuilderPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("querybuilder").FullName;
            File.Copy(pluginAssembly, Path.Combine(pluginDir, Path.GetFileName(pluginAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(), logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));
            var plugins = await manager.DiscoverAsync();

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
    public void Discover_Loads_All_Bundled_Plugins_From_Canonical_Subfolders()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-canonical").FullName;
            foreach (var plugin in BundledPlugins)
            {
                StageCanonical(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(BundledPlugins.Select(p => p.Id).OrderBy(id => id), plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Loads_All_Bundled_Plugins_From_Legacy_Flat_Layout()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-flat").FullName;
            foreach (var plugin in BundledPlugins)
            {
                StageFlat(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(BundledPlugins.Select(p => p.Id).OrderBy(id => id), plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Deduplicates_Flat_And_Canonical_Plugins_And_Prefers_Canonical_Copy()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-duplicate").FullName;
            var canonicalHello = StageCanonical(pluginRoot, BundledPlugins.Single(p => p.Name == "HelloPlugin"));
            foreach (var plugin in BundledPlugins)
            {
                StageFlat(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(BundledPlugins.Select(p => p.Id).OrderBy(id => id), plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            var hello = Assert.Single(plugins, p => p.Manifest.Id == "fo.hello");
            Assert.Equal(Path.GetFullPath(canonicalHello), Path.GetFullPath(LoadedAssemblyPath(hello)));
            Assert.Contains(logger.Entries, e => e.Message.Contains("Skipping duplicate plugin candidate", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_Invalid_Plugin_Dll_Does_Not_Block_Valid_Plugins()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-invalid").FullName;
            StageFlat(pluginRoot, BundledPlugins.Single(p => p.Name == "HelloPlugin"));
            File.WriteAllText(Path.Combine(pluginRoot, "InvalidPlugin.dll"), "not a managed assembly");

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            var plugin = Assert.Single(plugins);
            Assert.Equal("fo.hello", plugin.Manifest.Id);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error && e.Message.Contains("InvalidPlugin.dll", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_Empty_Plugin_Folder_Returns_Clean_Empty_State()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-empty").FullName;
            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);

            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning);
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
        RunSta(async () =>
        {
            var helloAssembly = typeof(HelloFoToolPlugin).Assembly.Location;
            var pluginDir = Directory.CreateTempSubdirectory("helloplugin").FullName;
            File.Copy(helloAssembly, Path.Combine(pluginDir, Path.GetFileName(helloAssembly)), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(pluginDir, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(), logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()));
            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.NotNull(logger.LastException);
        });
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action().GetAwaiter().GetResult();
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
