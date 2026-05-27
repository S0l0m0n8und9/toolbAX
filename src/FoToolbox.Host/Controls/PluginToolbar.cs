using System.Windows;
using System.Windows.Controls;

namespace FoToolbox.Host.Controls;

/// <summary>
/// Standardized plugin toolbar: a 36px-tall items container with consistent styling.
/// Plugins host this as the top region of their UserControl.
/// </summary>
public sealed class PluginToolbar : ItemsControl
{
    static PluginToolbar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(PluginToolbar),
            new FrameworkPropertyMetadata(typeof(PluginToolbar)));
    }
}
