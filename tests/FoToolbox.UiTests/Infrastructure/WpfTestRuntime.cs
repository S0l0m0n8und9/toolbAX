using System.Windows;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Registers the WPF pack:// URI scheme exactly once per process so Host theme
/// ResourceDictionaries can be loaded by absolute pack URI. The Application is created
/// once on an STA test thread (required by WPF); after construction, the pack:// scheme
/// is registered process-wide so later mounts on other STA dispatcher threads can load
/// Host theme dictionaries without needing their own Application instance.
/// </summary>
internal static class WpfTestRuntime
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void EnsurePackSchemeRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            if (Application.Current is null)
            {
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }
            _registered = true;
        }
    }
}
