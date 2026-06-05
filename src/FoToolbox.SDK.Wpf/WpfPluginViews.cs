using System;
using System.Windows;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.SDK.Wpf;

/// <summary>Host-side helpers for adapting an <see cref="IPluginView"/> to a WPF visual.</summary>
public static class WpfPluginViews
{
    /// <summary>
    /// Unwraps the <see cref="FrameworkElement"/> from a WPF plugin view. Lets the host obtain the
    /// visual without referencing <see cref="WpfPluginView"/>'s internals directly. Throws if the view
    /// is not a <see cref="WpfPluginView"/> (e.g. a plugin built for a different UI host).
    /// </summary>
    public static FrameworkElement Resolve(IPluginView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return (view as WpfPluginView)?.Content
            ?? throw new InvalidOperationException(
                $"Plugin view '{view.GetType().FullName}' is not a WPF view (expected {nameof(WpfPluginView)}).");
    }
}
