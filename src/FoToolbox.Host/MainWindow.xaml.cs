using FoToolbox.Core.Models;
using FoToolbox.Host.OData;
using FoToolbox.Host.Plugins;
using FoToolbox.Host.ViewModels;
using FoToolbox.Host.Views;
using FoToolbox.Host.Diagnostics;
using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Catalog;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;

namespace FoToolbox.Host;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ILogger _logger;
    private readonly string _profileDbPath = ProfilePaths.ResolveProfileDbPath();
    private ProfilesView? _profilesView;
    private bool _loadedOnce;

    public MainWindow()
    {
        InitializeComponent();

        AppDiagnostics.Initialize();
        _logger = AppDiagnostics.Logger;

        _vm = new MainWindowViewModel();
        DataContext = _vm;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        Loaded -= MainWindow_Loaded;

        try
        {
            await LoadPluginsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during startup plugin load.");
            MessageBox.Show($"Startup failed: {ex.Message}", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadPluginsAsync()
    {
        var profile = await ResolveProfileAsync(_profileDbPath);
        if (profile is null)
        {
            MessageBox.Show("No environment profiles found. Seed data could not be created.", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (env, sp) = profile.Value;
        _profilesView ??= new ProfilesView(new ProfilesViewModel(_profileDbPath, _logger, ApplyProfile));
        await ApplyProfileAsync(env, sp);

        // Kick off a background update check (fire-and-forget).
        _ = _vm.CheckUpdatesAsync();
    }

    private void ApplyProfile(FoEnvironment env, ServicePrincipal sp)
    {
        _ = ApplyProfileAsync(env, sp);
    }

    private async Task ApplyProfileAsync(FoEnvironment env, ServicePrincipal sp)
    {
        try
        {
            var odata = CreateODataClient(env, sp);
            var catalog = CreateCatalogService(env, sp);

            var pluginRoot = ResolvePluginRoot();
            var trust = PluginTrustOptions.FromEnvironment();
            var manager = new PluginManager(pluginRoot, env, odata, catalog, _logger, trust);
            var plugins = await manager.DiscoverAsync();
            _vm.LoadPlugins(plugins, _profilesView);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply profile {EnvId}", env.Id);
            MessageBox.Show($"Failed to apply profile: {ex.Message}", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<(FoEnvironment Env, ServicePrincipal Sp)?> ResolveProfileAsync(string dbPath)
    {
        var store = new ProfileStore(dbPath);
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();

        // Seed a placeholder profile if empty.
        var defaultProfile = await svc.GetDefaultAsync();
        if (defaultProfile is null)
        {
            var env = new FoEnvironment("dev", "Dev environment", "https://contoso.operations.dynamics.com", "00000000-0000-0000-0000-000000000000", "USMF");
            await svc.UpsertEnvironmentAsync(env);
            var sp = new ServicePrincipal("sp-dev", env.Id, "00000000-0000-0000-0000-000000000000", AuthMode.ClientSecret, null, null);
            await svc.UpsertServicePrincipalAsync(sp);
            await svc.SetDefaultEnvironmentAsync(env.Id);

            return (env, sp);
        }

        return await svc.GetDefaultAsync();
    }

    private static IODataClient CreateODataClient(FoEnvironment env, ServicePrincipal sp)
    {
        var handler = new AuthenticatedHandler(env, sp);
        var httpClient = new HttpClient(handler);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return new HttpODataClient(httpClient);
    }

    private ICatalogService CreateCatalogService(FoEnvironment env, ServicePrincipal sp)
    {
        var handler = new AuthenticatedHandler(env, sp);
        var httpClient = new HttpClient(handler);
        var profileStore = new ProfileStore(_profileDbPath);
        var catalogStorePath = ProfilePaths.ResolveAppDataPath("catalog.db");
        var catalogStore = new CatalogStore(catalogStorePath);
        return new CatalogService(httpClient, profileStore, catalogStore);
    }

    private static string ResolvePluginRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, "plugins");
        if (Directory.Exists(candidate))
        {
            return candidate;
        }

        // Dev-time fallback: climb upward to find the solution-level plugins output.
        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < 5 && dir != null; i++)
        {
            var pluginsDir = Path.Combine(dir.FullName, "plugins");
            if (Directory.Exists(pluginsDir))
            {
                var binDir = Path.Combine(pluginsDir, "HelloPlugin", "bin", "Debug", "net8.0-windows");
                if (Directory.Exists(binDir))
                {
                    return binDir;
                }
            }

            dir = dir.Parent;
        }

        return candidate;
    }

}
