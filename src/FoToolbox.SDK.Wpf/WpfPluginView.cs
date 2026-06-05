using System;
using System.Windows;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.SDK.Wpf;

/// <summary>
/// WPF implementation of <see cref="IPluginView"/>: wraps the <see cref="FrameworkElement"/> (typically
/// a <c>UserControl</c>) a plugin builds. A plugin's <c>CreateTool</c> returns
/// <c>new WpfPluginView(control)</c>; the WPF host unwraps it via <see cref="WpfPluginViews.Resolve"/>.
/// </summary>
public sealed class WpfPluginView : IPluginView
{
    public WpfPluginView(FrameworkElement content)
        => Content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>The visual to host (e.g. the plugin's <c>UserControl</c>).</summary>
    public FrameworkElement Content { get; }
}
