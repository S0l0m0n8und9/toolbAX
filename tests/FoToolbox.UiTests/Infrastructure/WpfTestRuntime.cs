using System.Windows;

namespace FoToolbox.UiTests.Infrastructure;

/// <summary>
/// Registers the WPF pack:// URI scheme exactly once per process so Host theme
/// ResourceDictionaries can be loaded by absolute pack URI. A single Application is
/// created purely for the registration side-effect and is never accessed again, so its
/// thread affinity is irrelevant.
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
