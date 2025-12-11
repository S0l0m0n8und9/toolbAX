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
        var manager = new PluginManager(pluginRoot, env, odata, logger);
        var plugins = manager.Discover();
        _vm.LoadPlugins(plugins);
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
