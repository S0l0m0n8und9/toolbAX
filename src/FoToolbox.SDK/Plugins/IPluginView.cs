namespace FoToolbox.SDK.Plugins;

/// <summary>
/// UI-framework-agnostic handle to the visual a plugin produces from
/// <see cref="IFoToolPlugin.CreateTool"/>. The host adapts it to its concrete UI type — the WPF host
/// uses <c>FoToolbox.SDK.Wpf.WpfPluginView</c> / <c>WpfPluginViews.Resolve</c>. Keeping this marker in
/// the core SDK lets the plugin contract stay free of any WPF dependency so an alternate UI host can
/// load the same plugins.
/// </summary>
public interface IPluginView
{
}
