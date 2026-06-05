using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using FoToolbox.Host.ViewModels;
using FoToolbox.Host.Views;
using FoToolbox.SDK.Plugins;
using FoToolbox.SDK.Wpf;
using FoToolbox.UiTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoToolbox.UiTests;

internal static class ViewRegistry
{
    public static IReadOnlyDictionary<string, ViewCase> All { get; } =
        Build().ToDictionary(c => c.Name, StringComparer.Ordinal);

    private static IEnumerable<ViewCase> Build()
    {
        yield return Plugin("QueryBuilder", () => new QueryBuilderPlugin.QueryBuilderPlugin());
        yield return Plugin("ODataPostBuilder", () => new ODataPostBuilderPlugin.ODataPostBuilderPlugin());
        yield return Plugin("TableEntityBrowser", () => new TableEntityBrowserPlugin.TableEntityBrowserPlugin());
        yield return Plugin("DualWriteMapBrowser", () => new DualWriteMapBrowserPlugin.DualWriteMapBrowserPlugin());
        yield return Plugin("DualWriteOperations", () => new DualWriteOperationsPlugin.DualWriteOperationsPlugin());
        yield return Plugin("DualWriteCompare", () => new DualWriteComparePlugin.DualWriteComparePlugin());
        yield return Plugin("Hello", () => new HelloPlugin.HelloFoToolPlugin());
        yield return new ViewCase("ProfilesView", () =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "fotoolbox-uitests");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".db");
            // ProfilesView.Loaded auto-runs RefreshCommand against this empty temp store.
            var vm = new ProfilesViewModel(dbPath, NullLogger.Instance, _ => { });
            return Task.FromResult<FrameworkElement>(new ProfilesView(vm));
        });
    }

    private static ViewCase Plugin(string name, Func<IFoToolPlugin> create) =>
        new(name, async () =>
        {
            var plugin = create();
            await plugin.InitializeAsync(new FakePluginContext());
            return WpfPluginViews.Resolve(plugin.CreateTool());
        });
}
