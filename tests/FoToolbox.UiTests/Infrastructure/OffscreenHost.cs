using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Mounts a WPF element in an invisible HwndSource so Loaded fires, styles apply, and
/// data bindings evaluate — without showing a window. Host theme dictionaries are merged
/// at the mount root so StaticResource lookups resolve without an Application.Resources.
/// </summary>
internal sealed class OffscreenHost : IDisposable
{
    private static readonly string[] ThemeDictionaries =
    {
        "pack://application:,,,/FoToolbox.Host;component/Themes/Fluent.Theme.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Spacing.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Icons.xaml",
        "pack://application:,,,/FoToolbox.Host;component/Themes/Fluent.Controls.xaml",
    };

    private const int OffscreenWidth = 1280;
    private const int OffscreenHeight = 1024;

    private readonly HwndSource _source;
    private readonly Border _root;

    private OffscreenHost(HwndSource source, Border root) { _source = source; _root = root; }

    public static OffscreenHost Mount(FrameworkElement element)
    {
        WpfTestRuntime.EnsurePackSchemeRegistered();

        var root = new Border();
        foreach (var uri in ThemeDictionaries)
        {
            root.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
        }
        root.Child = element;

        var parameters = new HwndSourceParameters("FoToolbox.UiTests.Offscreen")
        {
            Width = OffscreenWidth,
            Height = OffscreenHeight,
            WindowStyle = 0, // WS_VISIBLE not set => never displayed
        };
        var source = new HwndSource(parameters) { RootVisual = root };

        root.Measure(new Size(OffscreenWidth, OffscreenHeight));
        root.Arrange(new Rect(0, 0, OffscreenWidth, OffscreenHeight));
        root.UpdateLayout();

        var host = new OffscreenHost(source, root);
        host.PumpToIdle();
        return host;
    }

    public void PumpToIdle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        _source.RootVisual = null;
        _root.Child = null;
        _source.Dispose();
        PumpToIdle();
    }
}
