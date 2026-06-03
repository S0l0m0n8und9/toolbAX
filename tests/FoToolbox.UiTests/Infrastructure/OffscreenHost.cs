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

    private readonly HwndSource _source;

    private OffscreenHost(HwndSource source) => _source = source;

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
            Width = 1280,
            Height = 1024,
            WindowStyle = 0, // WS_VISIBLE not set => never displayed
        };
        var source = new HwndSource(parameters) { RootVisual = root };

        root.Measure(new Size(1280, 1024));
        root.Arrange(new Rect(0, 0, 1280, 1024));
        root.UpdateLayout();

        var host = new OffscreenHost(source);
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

    public void Dispose() => _source.Dispose();
}
