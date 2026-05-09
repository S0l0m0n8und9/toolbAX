using HelloPlugin;
using ODataPostBuilderPlugin;
using QueryBuilderPlugin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TableEntityBrowserPlugin;

namespace FoToolbox.Tests.Fixtures;

internal static class LegacyFlatBundledPluginFixture
{
    internal sealed record BundledPluginDescriptor(string Name, Type PluginType, string ManifestId);

    internal static readonly IReadOnlyList<BundledPluginDescriptor> BundledPlugins = new[]
    {
        new BundledPluginDescriptor("HelloPlugin", typeof(HelloFoToolPlugin), "fo.hello"),
        new BundledPluginDescriptor("QueryBuilder", typeof(QueryBuilderPlugin.QueryBuilderPlugin), "fo.querybuilder"),
        new BundledPluginDescriptor("TableEntityBrowser", typeof(TableEntityBrowserPlugin.TableEntityBrowserPlugin), "fo.tableentitybrowser"),
        new BundledPluginDescriptor("ODataPostBuilder", typeof(ODataPostBuilderPlugin.ODataPostBuilderPlugin), "fo.odatapostbuilder"),
        new BundledPluginDescriptor("DualWriteMapBrowser", typeof(DualWriteMapBrowserPlugin.DualWriteMapBrowserPlugin), "fo.dualwritemapbrowser")
    };

    internal static readonly IReadOnlyList<string> ExpectedBundledPluginIds =
        BundledPlugins.Select(p => p.ManifestId).OrderBy(id => id, StringComparer.Ordinal).ToArray();

    internal static int ExpectedBundledPluginCount => BundledPlugins.Count;

    internal static string CreateLegacyFlatLayoutFixture()
    {
        var pluginRoot = Directory.CreateTempSubdirectory("legacy-flat-fixture").FullName;
        foreach (var plugin in BundledPlugins)
        {
            var stagedPath = Path.Combine(pluginRoot, plugin.Name + ".dll");
            File.Copy(plugin.PluginType.Assembly.Location, stagedPath, overwrite: true);
        }

        return pluginRoot;
    }
}
