using FoToolbox.Core.Models;
using FoToolbox.Host.OData;
using FoToolbox.Host.Plugins;
using FoToolbox.Host.ViewModels;
using FoToolbox.Core.OData;
using FoToolbox.Core.Profiles;
using FoToolbox.Core.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using FoToolbox.Updater;

namespace FoToolbox.Host;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainWindowViewModel();
        DataContext = _vm;

        LoadPlugins();
    }

    private void LoadPlugins()
    {
        var logger = NullLogger.Instance;
        var profile = ResolveProfileAsync().GetAwaiter().GetResult();
        if (profile is null)
        {
            MessageBox.Show("No environment profiles found. Seed data could not be created.", "FO Toolbox", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (env, sp) = profile.Value;
        var odata = CreateODataClient(env, sp);

        var pluginRoot = ResolvePluginRoot();
        var trust = PluginTrustOptions.FromEnvironment();
        var manager = new PluginManager(pluginRoot, env, odata, logger, trust);
        var plugins = manager.Discover();
        _vm.LoadPlugins(plugins);

        // Kick off a background update check (fire-and-forget).
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var channel = ReadChannelConfig();
            if (channel is null) return;

            var http = new HttpClient();
            var fetcher = new ResilientUpdateFetcher(new HttpUpdateFetcher(http));
            var loader = new UpdateManifestLoader(fetcher);
            var updater = new UpdaterClient(fetcher, Path.Combine(AppContext.BaseDirectory, "updates"));
            var orchestrator = new UpdateOrchestrator(loader, updater, channel);

            var staged = await orchestrator.CheckAndStageAsync();
            if (!string.IsNullOrEmpty(staged))
            {
                // TODO: surface to UI that an update is staged; for now just log to debug console.
                Console.WriteLine($"Update staged at {staged}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Update check failed: {ex.Message}");
        }
    }

    private UpdateChannelConfig? ReadChannelConfig()
    {
        var channel = Environment.GetEnvironmentVariable("FOTOOLBOX_UPDATE_CHANNEL") ?? "stable";
        var manifestUrl = Environment.GetEnvironmentVariable("FOTOOLBOX_UPDATE_MANIFEST");
        if (string.IsNullOrWhiteSpace(manifestUrl))
        {
            return null;
        }

        return new UpdateChannelConfig(channel, new Uri(manifestUrl));
    }

    private static async Task<(FoEnvironment Env, ServicePrincipal Sp)?> ResolveProfileAsync()
    {
        var dbPath = Path.Combine(AppContext.BaseDirectory, "profile.db");
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
