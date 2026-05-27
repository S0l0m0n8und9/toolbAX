using System.Windows;

namespace FoToolbox.Host.Controls;

/// <summary>
/// Flexible spacer for use inside <see cref="PluginToolbar"/>. Children placed after a spacer
/// dock toward the right edge when used inside a DockPanel; in WrapPanel mode the spacer
/// has no effect (acceptable; right alignment is approximate in wrap mode).
/// </summary>
public sealed class ToolbarSpacer : FrameworkElement
{
    public ToolbarSpacer()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Width = double.NaN;
        MinWidth = 8;
    }
}
