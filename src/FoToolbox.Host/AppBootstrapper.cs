using FoToolbox.Core.Auth;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using FoToolbox.Host.OData;
using FoToolbox.Host.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace FoToolbox.Host;

/// <summary>
/// Centralises profile resolution, service creation, and plugin discovery
/// so that <see cref="MainWindow"/> stays a thin UI shell.
/// </summary>
internal sealed class AppBootstrapper : IDisposable
{
    private readonly string _profileDbPath;
    private readonly ILogger _logger;
    private HttpClient? _foHttpClient;
    private HttpClient? _dataverseHttpClient;

    public AppBootstrapper(string profileDbPath, ILogger logger)
    {
        _profileDbPath = profileDbPath;
        _logger = logger;
    }

    /// <summary>
    /// Resolves (or seeds) the default profile from the database.
    /// Returns <c>null</c> when no profile could be determined.
    /// </summary>
    public async Task<ProfileBundle?> ResolveProfileAsync()
    {
        var store = new ProfileStore(_profileDbPath);
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();
        return await svc.GetDefaultBundleAsync();
    }

    /// <summary>
    /// Creates all services and discovers plugins for the given profile.
    /// </summary>
    public async Task<BootstrapResult> ApplyProfileAsync(ProfileBundle bundle)
    {
        _foHttpClient?.Dispose();
        _foHttpClient = CreateAuthenticatedHttpClient(bundle.FoEnvironment, bundle.FoPrincipal);

        _dataverseHttpClient?.Dispose();
        _dataverseHttpClient = IsDataverseConfigured(bundle.DataverseEnvironment)
            ? CreateAuthenticatedHttpClient(
                ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(bundle.DataverseEnvironment.BaseUrl),
                bundle.DataverseEnvironment.TenantId,
                bundle.DataversePrincipal)
            : null;

        var odata = new HttpODataClient(_foHttpClient);
        var odataWrite = new HttpODataWriteClient(_foHttpClient);
        var catalog = CreateCatalogService(_foHttpClient);

        var pluginRoot = ResolvePluginRoot();
        var trust = PluginTrustOptions.FromEnvironment();
        var manager = new PluginManager(
            pluginRoot,
            bundle.FoEnvironment,
            odata,
            odataWrite,
            catalog,
            _logger,
            IsDataverseConfigured(bundle.DataverseEnvironment) ? bundle.DataverseEnvironment : null,
            _dataverseHttpClient,
            trust);

        var plugins = await manager.DiscoverAsync();

        return new BootstrapResult(plugins, manager.NavigationBus);
    }

    private ICatalogService CreateCatalogService(HttpClient httpClient)
    {
        var profileStore = new ProfileStore(_profileDbPath);
        var catalogStorePath = ProfilePaths.ResolveAppDataPath("catalog.db");
        var catalogStore = new CatalogStore(catalogStorePath);
        return new CatalogService(httpClient, profileStore, catalogStore);
    }

    private static HttpClient CreateAuthenticatedHttpClient(FoEnvironment env, ServicePrincipal sp)
    {
        return new HttpClient(new AuthenticatedHandler(env, sp));
    }

    private static HttpClient CreateAuthenticatedHttpClient(string resourceBaseUrl, string tenantId, ServicePrincipal sp)
    {
        return new HttpClient(new AuthenticatedHandler(resourceBaseUrl, tenantId, sp));
    }

    internal static string ResolvePluginRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "plugins");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        // Dev-time fallback: walk up to the solution root and look for built plugin output.
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            var pluginsDir = Path.Combine(dir.FullName, "plugins");
            if (Directory.Exists(pluginsDir))
            {
                // Look for any plugin directory that has been built (not just a specific one).
                foreach (var sub in Directory.GetDirectories(pluginsDir))
                {
                    var binDir = Path.Combine(sub, "bin", "Debug", "net8.0-windows");
                    if (Directory.Exists(binDir))
                    {
                        return binDir;
                    }
                }
            }

            dir = dir.Parent;
        }

        return candidate;
    }

    private static bool IsDataverseConfigured(DataverseEnvironment env)
    {
        return !string.IsNullOrWhiteSpace(env.BaseUrl) &&
               !string.IsNullOrWhiteSpace(env.TenantId);
    }

    public void Dispose()
    {
        _foHttpClient?.Dispose();
        _dataverseHttpClient?.Dispose();
    }
}

internal sealed class BootstrapResult
{
    public IReadOnlyList<LoadedPlugin> Plugins { get; }
    public PluginNavigationBus NavigationBus { get; }

    public BootstrapResult(IReadOnlyList<LoadedPlugin> plugins, PluginNavigationBus navigationBus)
    {
        Plugins = plugins;
        NavigationBus = navigationBus;
    }
}
