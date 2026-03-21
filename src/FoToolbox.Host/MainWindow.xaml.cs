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
    private HttpClient? _activeFoHttpClient;
    private HttpClient? _activeDataverseHttpClient;

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

        var bundle = profile;
        _profilesView ??= new ProfilesView(new ProfilesViewModel(_profileDbPath, _logger, ApplyProfile));
        await ApplyProfileAsync(bundle);

        // Kick off a background update check (fire-and-forget).
        _ = _vm.CheckUpdatesAsync();
    }

    private void ApplyProfile(ProfileBundle bundle)
    {
        _ = ApplyProfileAsync(bundle);
    }

    private async Task ApplyProfileAsync(ProfileBundle bundle)
    {
        try
        {
            _activeFoHttpClient?.Dispose();
            _activeFoHttpClient = CreateAuthenticatedHttpClient(bundle.FoEnvironment, bundle.FoPrincipal);

            _activeDataverseHttpClient?.Dispose();
            if (IsDataverseConfigured(bundle.DataverseEnvironment))
            {
                _activeDataverseHttpClient = CreateAuthenticatedHttpClient(
                    ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(bundle.DataverseEnvironment.BaseUrl),
                    bundle.DataverseEnvironment.TenantId,
                    bundle.DataversePrincipal);
            }
            else
            {
                _activeDataverseHttpClient = null;
            }

            var odata = CreateODataClient(_activeFoHttpClient);
            var odataWrite = CreateODataWriteClient(_activeFoHttpClient);
            var catalog = CreateCatalogService(_activeFoHttpClient);

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
                _activeDataverseHttpClient,
                trust);
            var plugins = await manager.DiscoverAsync();
            _vm.LoadPlugins(plugins, _profilesView);

            // Wire cross-plugin tab activation: when a plugin calls TryNavigateTo the bus fires
            // this event so the host can bring the right tab to focus.
            manager.NavigationBus.PluginActivationRequested += loaded =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var entry = _vm.Plugins.FirstOrDefault(p => p.Loaded == loaded);
                    if (entry is not null)
                    {
                        _vm.Selected = entry;
                    }
                });
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply profile {EnvId}", bundle.FoEnvironment.Id);
            MessageBox.Show($"Failed to apply profile: {ex.Message}", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task<ProfileBundle?> ResolveProfileAsync(string dbPath)
    {
        var store = new ProfileStore(dbPath);
        var svc = new ProfileService(store);
        await svc.EnsureCreatedAsync();

        // Seed a placeholder profile if empty.
        var defaultProfile = await svc.GetDefaultBundleAsync();
        if (defaultProfile is null)
        {
            var env = new FoEnvironment("dev", "Dev environment", "https://contoso.operations.dynamics.com", "00000000-0000-0000-0000-000000000000", "USMF");
            await svc.UpsertEnvironmentAsync(env);
            var foSp = new ServicePrincipal("sp-dev", env.Id, "00000000-0000-0000-0000-000000000000", AuthMode.ClientSecret, null, null, AuthTarget.Fo);
            await svc.UpsertServicePrincipalAsync(foSp);
            var ceEnv = new DataverseEnvironment(env.Id, string.Empty, string.Empty);
            await svc.UpsertDataverseEnvironmentAsync(ceEnv);
            var ceSp = new ServicePrincipal("sp-dev-ce", env.Id, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Dataverse);
            await svc.UpsertServicePrincipalAsync(ceSp);
            await svc.SetDefaultEnvironmentAsync(env.Id);

            return await svc.GetDefaultBundleAsync();
        }

        return await svc.GetDefaultBundleAsync();
    }

    private static HttpClient CreateAuthenticatedHttpClient(FoEnvironment env, ServicePrincipal sp)
    {
        var handler = new AuthenticatedHandler(env, sp);
        return new HttpClient(handler);
    }

    private static HttpClient CreateAuthenticatedHttpClient(string resourceBaseUrl, string tenantId, ServicePrincipal sp)
    {
        var handler = new AuthenticatedHandler(resourceBaseUrl, tenantId, sp);
        return new HttpClient(handler);
    }

    private static IODataClient CreateODataClient(HttpClient httpClient)
    {
        return new HttpODataClient(httpClient);
    }

    private static IODataWriteClient CreateODataWriteClient(HttpClient httpClient)
    {
        return new HttpODataWriteClient(httpClient);
    }

    private ICatalogService CreateCatalogService(HttpClient httpClient)
    {
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

    private static bool IsDataverseConfigured(DataverseEnvironment env)
    {
        return !string.IsNullOrWhiteSpace(env.BaseUrl) &&
               !string.IsNullOrWhiteSpace(env.TenantId);
    }

}
