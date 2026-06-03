using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using FoToolbox.SDK.Plugins;
using FoToolbox.UiTests.Infrastructure;

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
        // Host views are added in Task 6.
    }

    private static ViewCase Plugin(string name, Func<IFoToolPlugin> create) =>
        new(name, async () =>
        {
            var plugin = create();
            await plugin.InitializeAsync(new FakePluginContext());
            return plugin.CreateTool();
        });
}
