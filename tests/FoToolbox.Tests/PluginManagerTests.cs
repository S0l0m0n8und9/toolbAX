using FoToolbox.Core.Models;
using FoToolbox.Host.Plugins;
using FoToolbox.Tests.Fixtures;
using FoToolbox.Core.OData;
using FoToolbox.Core.Catalog;
using FoToolbox.SDK.Plugins;
using HelloPlugin;
using UnsignedTestPlugin;
using ODataPostBuilderPlugin;
using QueryBuilderPlugin;
using TableEntityBrowserPlugin;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Concurrent;
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

    private sealed class FakeConsentPrompt : IPluginConsentPrompt
    {
        private readonly PluginConsentDecision _decision;
        public int Calls { get; private set; }
        public FakeConsentPrompt(PluginConsentDecision decision) => _decision = decision;
        public PluginConsentDecision RequestConsent(PluginConsentRequest request)
        {
            Calls++;
            return _decision;
        }
    }

    private static string UnsignedPluginAssemblyPath() =>
        typeof(UnsignedTestPlugin.UnsignedTestPlugin).Assembly.Location;

    private static string StageUnsigned(string pluginRoot, string fileName)
    {
        Directory.CreateDirectory(pluginRoot);
        var dest = Path.Combine(pluginRoot, fileName);
        File.Copy(UnsignedPluginAssemblyPath(), dest, overwrite: true);
        return dest;
    }

    private static FoEnvironment CreateEnv() =>
        new("dev", "Dev", "https://contoso.operations.dynamics.com", "00000000-0000-0000-0000-000000000000", "USMF");

    private static PluginManager CreateManager(string pluginRoot, CapturingLogger logger) =>
        new(pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(), logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));

    private static string StageCanonical(string pluginRoot, LegacyFlatBundledPluginFixture.BundledPluginDescriptor plugin)
    {
        var assemblyPath = plugin.PluginType.Assembly.Location;
        var pluginDir = Path.Combine(pluginRoot, plugin.Name);
        Directory.CreateDirectory(pluginDir);
        var stagedPath = Path.Combine(pluginDir, plugin.Name + ".dll");
        File.Copy(assemblyPath, stagedPath, overwrite: true);
        return stagedPath;
    }

    private static string StageFlat(string pluginRoot, LegacyFlatBundledPluginFixture.BundledPluginDescriptor plugin)
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
            foreach (var plugin in LegacyFlatBundledPluginFixture.BundledPlugins)
            {
                StageCanonical(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Loads_All_Bundled_Plugins_From_Legacy_Flat_Layout()
    {
        RunSta(async () =>
        {
            var pluginRoot = LegacyFlatBundledPluginFixture.CreateLegacyFlatLayoutFixture();

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginCount, plugins.Count);
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Upgrade_Then_Restart_Preserves_Bundled_Plugin_Count_And_Identities()
    {
        RunSta(async () =>
        {
            var pluginRoot = LegacyFlatBundledPluginFixture.CreateLegacyFlatLayoutFixture();

            var firstLogger = new CapturingLogger();
            var firstManager = CreateManager(pluginRoot, firstLogger);
            var firstDiscovery = await firstManager.DiscoverAsync();
            var firstIds = firstDiscovery.Select(p => p.Manifest.Id).OrderBy(id => id).ToArray();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginCount, firstDiscovery.Count);
            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, firstIds);
            Assert.DoesNotContain(firstLogger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);

            var restartLogger = new CapturingLogger();
            var restartManager = CreateManager(pluginRoot, restartLogger);
            var restartDiscovery = await restartManager.DiscoverAsync();
            var restartIds = restartDiscovery.Select(p => p.Manifest.Id).OrderBy(id => id).ToArray();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginCount, restartDiscovery.Count);
            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, restartIds);
            Assert.Equal(firstIds, restartIds);
            Assert.DoesNotContain(restartLogger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Migrates_Legacy_Flat_Bundled_Plugins_To_Canonical_Subfolders()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-flat-migration").FullName;
            foreach (var plugin in LegacyFlatBundledPluginFixture.BundledPlugins)
            {
                StageFlat(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, plugins.Select(p => p.Manifest.Id).OrderBy(id => id));

            foreach (var plugin in LegacyFlatBundledPluginFixture.BundledPlugins)
            {
                var canonicalPath = Path.Combine(pluginRoot, plugin.Name, plugin.Name + ".dll");
                var flatPath = Path.Combine(pluginRoot, plugin.Name + ".dll");
                Assert.True(File.Exists(canonicalPath), $"Expected canonical plugin path {canonicalPath} to exist.");
                Assert.False(File.Exists(flatPath), $"Expected legacy flat plugin path {flatPath} to be removed.");
            }

            Assert.Contains(
                logger.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
                     e.Message.Contains("Migrated legacy flat plugin", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_Logs_Skipped_Migration_With_Plugin_And_Path_When_Canonical_Exists()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-flat-skip-migration").FullName;
            var hello = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "HelloPlugin");
            var canonicalHelloPath = StageCanonical(pluginRoot, hello);
            var flatHelloPath = StageFlat(pluginRoot, hello);

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            _ = await manager.DiscoverAsync();

            Assert.True(File.Exists(canonicalHelloPath));
            Assert.True(File.Exists(flatHelloPath));
            Assert.Contains(
                logger.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information &&
                     e.Message.Contains("Skipping legacy flat plugin migration", StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains("HelloPlugin", StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains(Path.Combine(pluginRoot, "HelloPlugin", "HelloPlugin.dll"), StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains(Path.Combine(pluginRoot, "HelloPlugin.dll"), StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_Logs_Failed_Migration_With_Plugin_And_Path_And_Falls_Back_To_Flat_Load()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-flat-failed-migration").FullName;
            var hello = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "HelloPlugin");
            var flatHelloPath = StageFlat(pluginRoot, hello);
            var canonicalPath = Path.Combine(pluginRoot, "HelloPlugin", "HelloPlugin.dll");

            // Block canonical directory creation so migration fails and fallback loading remains exercised.
            File.WriteAllText(Path.Combine(pluginRoot, "HelloPlugin"), "blocking file");

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            var loadedHello = Assert.Single(plugins, p => p.Manifest.Id == "fo.hello");
            Assert.Equal(Path.GetFullPath(flatHelloPath), Path.GetFullPath(LoadedAssemblyPath(loadedHello)));
            Assert.True(File.Exists(flatHelloPath));
            var migrationWarning = Assert.Single(
                logger.Entries,
                e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning &&
                     e.Message.Contains("Failed migrating legacy flat plugin", StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains("HelloPlugin", StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains(flatHelloPath, StringComparison.OrdinalIgnoreCase) &&
                     e.Message.Contains(canonicalPath, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(migrationWarning.Exception);
        });
    }

    [Fact]
    public void Discover_Loads_Mixed_Layout_Canonical_And_FlatOnly()
    {
        // Canonical for HelloPlugin+QueryBuilder; flat-only for TableEntityBrowser+ODataPostBuilder.
        // Migration moves the flat-only bundled ones to canonical before discovery runs.
        // All four must load and no "Skipping duplicate" log must appear because there is no overlap.
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-mixed-layout").FullName;

            var hello = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "HelloPlugin");
            var query = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "QueryBuilder");
            var tableEntity = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "TableEntityBrowser");
            var odata = LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "ODataPostBuilder");

            StageCanonical(pluginRoot, hello);
            StageCanonical(pluginRoot, query);
            StageFlat(pluginRoot, tableEntity);
            StageFlat(pluginRoot, odata);

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            var loadedIds = plugins.Select(p => p.Manifest.Id).OrderBy(id => id).ToArray();
            Assert.Equal(
                new[] { "fo.hello", "fo.odatapostbuilder", "fo.querybuilder", "fo.tableentitybrowser" },
                loadedIds);
            Assert.DoesNotContain(
                logger.Entries,
                e => e.Message.Contains("Skipping duplicate plugin candidate", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void Discover_Deduplicates_Flat_And_Canonical_Plugins_And_Prefers_Canonical_Copy()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-duplicate").FullName;
            var canonicalHello = StageCanonical(pluginRoot, LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "HelloPlugin"));
            foreach (var plugin in LegacyFlatBundledPluginFixture.BundledPlugins)
            {
                StageFlat(pluginRoot, plugin);
            }

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, plugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            var hello = Assert.Single(plugins, p => p.Manifest.Id == "fo.hello");
            Assert.Equal(Path.GetFullPath(canonicalHello), Path.GetFullPath(LoadedAssemblyPath(hello)));
            Assert.Contains(logger.Entries, e => e.Message.Contains("Skipping duplicate plugin candidate", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_SameVersionReinstall_Loads_All_Bundled_Plugins_Without_Errors()
    {
        // Scenario: WiX places canonical plugin subfolders during initial install. A same-version
        // reinstall re-places the same files while the app is closed (no file locks). On next
        // app start, DiscoverAsync must load all plugins cleanly from the pre-existing canonical
        // layout, without requiring any migration or manual intervention.
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-same-version-reinstall").FullName;

            // Pre-stage canonical layout as WiX would leave it after install or same-version reinstall.
            foreach (var plugin in LegacyFlatBundledPluginFixture.BundledPlugins)
            {
                StageCanonical(pluginRoot, plugin);
            }

            // First app start post-install.
            var firstLogger = new CapturingLogger();
            var firstManager = CreateManager(pluginRoot, firstLogger);
            var firstPlugins = await firstManager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginCount, firstPlugins.Count);
            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, firstPlugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.DoesNotContain(firstLogger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
            Assert.Contains(firstLogger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information && e.Message.Contains(pluginRoot, StringComparison.OrdinalIgnoreCase));

            // Simulate app restart after same-version reinstall: canonical layout is unchanged
            // (reinstall replaces files with identical content while app is closed, so no migration
            // or re-staging is needed). A fresh PluginManager discovers the same layout.
            var restartLogger = new CapturingLogger();
            var restartManager = CreateManager(pluginRoot, restartLogger);
            var restartPlugins = await restartManager.DiscoverAsync();

            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginCount, restartPlugins.Count);
            Assert.Equal(LegacyFlatBundledPluginFixture.ExpectedBundledPluginIds, restartPlugins.Select(p => p.Manifest.Id).OrderBy(id => id));
            Assert.DoesNotContain(restartLogger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
            Assert.Contains(restartLogger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information && e.Message.Contains(pluginRoot, StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Discover_Invalid_Plugin_Dll_Does_Not_Block_Valid_Plugins()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-invalid").FullName;
            StageFlat(pluginRoot, LegacyFlatBundledPluginFixture.BundledPlugins.Single(p => p.Name == "HelloPlugin"));
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
            var pluginDir = Directory.CreateTempSubdirectory("unsigned-blocked").FullName;
            StageUnsigned(pluginDir, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginDir, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()));
            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_AlwaysTrust_Loads_And_Persists()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-always").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");
            var storePath = Path.Combine(Directory.CreateTempSubdirectory("ts-always").FullName, "trusted.json");
            var store = new FoToolbox.Core.Profiles.PluginTrustStore(storePath);
            var prompt = new FakeConsentPrompt(PluginConsentDecision.AlwaysTrust);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                trustStore: store, consentPrompt: prompt);

            var plugins = await manager.DiscoverAsync();

            Assert.Single(plugins, p => p.Manifest.Id == "test.unsigned");
            Assert.Equal(1, prompt.Calls);
            Assert.True(File.Exists(storePath));
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_LoadOnce_Loads_Without_Persisting()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-once").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");
            var storePath = Path.Combine(Directory.CreateTempSubdirectory("ts-once").FullName, "trusted.json");
            var store = new FoToolbox.Core.Profiles.PluginTrustStore(storePath);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                trustStore: store, consentPrompt: new FakeConsentPrompt(PluginConsentDecision.LoadOnce));

            var plugins = await manager.DiscoverAsync();

            Assert.Single(plugins, p => p.Manifest.Id == "test.unsigned");
            Assert.False(File.Exists(storePath));
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_Deny_Skips_Plugin()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-consent-deny").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                consentPrompt: new FakeConsentPrompt(PluginConsentDecision.Deny));

            var plugins = await manager.DiscoverAsync();

            Assert.DoesNotContain(plugins, p => p.Manifest.Id == "test.unsigned");
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_Denied_When_No_ConsentPrompt()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-headless-deny").FullName;
            StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()));

            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning);
        });
    }

    [Fact]
    public void Bundled_Plugin_With_Wrong_Token_Is_Rejected()
    {
        RunSta(async () =>
        {
            // Stage the unsigned (no-token) assembly under a bundled assembly file name so the
            // strong-name pin check runs and fails (token absent != pinned token).
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-pin-mismatch").FullName;
            var bundledDir = Path.Combine(pluginRoot, "HelloPlugin");
            Directory.CreateDirectory(bundledDir);
            File.Copy(UnsignedPluginAssemblyPath(), Path.Combine(bundledDir, "HelloPlugin.dll"), overwrite: true);

            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(true, Array.Empty<string>()));

            var plugins = await manager.DiscoverAsync();

            Assert.Empty(plugins);
            Assert.Contains(logger.Entries, e => e.Level == Microsoft.Extensions.Logging.LogLevel.Error &&
                e.Message.Contains("strong-name", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void Unsigned_ThirdParty_PreTrusted_Loads_Without_Prompting()
    {
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-pretrusted").FullName;
            var stagedPath = StageUnsigned(pluginRoot, "UnsignedTestPlugin.dll");
            var storePath = Path.Combine(Directory.CreateTempSubdirectory("ts-pretrusted").FullName, "trusted.json");

            // Pre-seed the trust store with this exact assembly name + SHA-256, as a prior
            // "Always trust" decision would have.
            var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(stagedPath)));
            var store = new FoToolbox.Core.Profiles.PluginTrustStore(storePath);
            store.Add("UnsignedTestPlugin", sha);

            var prompt = new FakeConsentPrompt(PluginConsentDecision.Deny); // would block the load if ever called
            var logger = new CapturingLogger();
            var manager = new PluginManager(
                pluginRoot, CreateEnv(), new StubODataClient(), new StubODataWriteClient(), new StubCatalogService(),
                logger, trustOptions: new PluginTrustOptions(false, Array.Empty<string>()),
                trustStore: store, consentPrompt: prompt);

            var plugins = await manager.DiscoverAsync();

            Assert.Single(plugins, p => p.Manifest.Id == "test.unsigned");
            Assert.Equal(0, prompt.Calls);
        });
    }

    [Fact]
    public void Discover_Loads_New_DualWrite_Plugins_As_Renderable_Tabs()
    {
        // The Dual-write Operations and Compare plugins are the newest bundled features. This guards
        // that they are discovered, pass bundled-plugin trust, and produce a non-null tool control —
        // i.e. the host can render a tab for each (MainWindowViewModel.LoadPlugins binds ToolControl).
        RunSta(async () =>
        {
            var pluginRoot = Directory.CreateTempSubdirectory("plugins-dualwrite-new").FullName;
            StageCanonicalByType(pluginRoot, "DualWriteOperations", typeof(DualWriteOperationsPlugin.DualWriteOperationsPlugin));
            StageCanonicalByType(pluginRoot, "DualWriteCompare", typeof(DualWriteComparePlugin.DualWriteComparePlugin));

            var logger = new CapturingLogger();
            var manager = CreateManager(pluginRoot, logger);
            var plugins = await manager.DiscoverAsync();

            if (plugins.Count == 0 && logger.LastException != null)
            {
                throw logger.LastException;
            }

            var operations = Assert.Single(plugins, p => p.Manifest.Id == "fo.dualwriteoperations");
            Assert.Equal("Dual-write Operations", operations.Manifest.Name);
            Assert.NotNull(operations.ToolControl);

            var compare = Assert.Single(plugins, p => p.Manifest.Id == "fo.dualwritecompare");
            Assert.Equal("Dual-write Compare", compare.Manifest.Name);
            Assert.NotNull(compare.ToolControl);

            Assert.DoesNotContain(logger.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
        });
    }

    [Fact]
    public void RunSta_Keeps_Async_Continuations_On_Sta_Thread()
    {
        // Regression for #38: DiscoverAsync awaits IFoToolPlugin.InitializeAsync and then calls
        // CreateTool(), which constructs WPF controls and requires an STA thread. If the STA test
        // thread has no SynchronizationContext, the post-await continuation resumes on a thread-pool
        // (MTA) thread and CreateTool throws "The calling thread must be STA" intermittently —
        // producing the flaky "expected 5 bundled plugins, observed 4" failure under full-suite load.
        // RunSta must therefore keep awaited continuations on the same STA thread.
        RunSta(async () =>
        {
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
            await Task.Yield();
            Assert.Equal(ApartmentState.STA, Thread.CurrentThread.GetApartmentState());
        });
    }

    private static void StageCanonicalByType(string pluginRoot, string name, Type pluginType)
    {
        var pluginDir = Path.Combine(pluginRoot, name);
        Directory.CreateDirectory(pluginDir);
        File.Copy(pluginType.Assembly.Location, Path.Combine(pluginDir, name + ".dll"), overwrite: true);
    }

    private static void RunSta(Func<Task> action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var previousContext = SynchronizationContext.Current;
            var syncContext = new SingleThreadSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(syncContext);
            try
            {
                // Pump awaited continuations back onto this STA thread (rather than letting them
                // resume on a thread-pool MTA thread) so WPF construction in CreateTool() stays on
                // STA. See RunSta_Keeps_Async_Continuations_On_Sta_Thread / issue #38.
                var task = action();
                task.ContinueWith(_ => syncContext.Complete(), TaskScheduler.Default);
                syncContext.RunOnCurrentThread();
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previousContext);
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

    /// <summary>
    /// Minimal single-threaded <see cref="SynchronizationContext"/> that queues posted callbacks
    /// and runs them on the thread that calls <see cref="RunOnCurrentThread"/>. Used by
    /// <see cref="RunSta"/> so async continuations stay on the dedicated STA thread.
    /// </summary>
    private sealed class SingleThreadSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("Synchronous Send is not supported on the STA pump.");

        public void RunOnCurrentThread()
        {
            foreach (var work in _queue.GetConsumingEnumerable())
            {
                work.Callback(work.State);
            }
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
