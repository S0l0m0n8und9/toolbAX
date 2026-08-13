using System;
using Avalonia;

namespace ToolBax.App;

internal static class Program
{
    // Avalonia configuration; also used by the headless test harness builder.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // #212: the window froze in a deadlock wholly inside the WinUIComposition backend — the UI thread
            // waiting in MediaContext.SyncWaitCompositorBatch on a synchronous paint while the render thread
            // never returned from the native WinUiCompositedWindowRenderTarget.BeginDraw(). This app draws no
            // acrylic, transparency or blur, which is all WinUIComposition offers over the alternatives, so on
            // this codebase its only distinguishing behaviour was the wedge. Ask for the DXGI swap chain
            // instead, keeping the redirection surface as the compatibility floor. Ignored off Windows, where
            // the Win32 backend never loads. TOOLBAX_COMPOSITION overrides this without a rebuild — see
            // CompositionPreference, which also explains why an unrecognised value is ignored rather than fatal.
            .With(new Win32PlatformOptions { CompositionMode = CompositionPreference.Current.Modes })
            .WithInterFont()
            .LogToTrace();

    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
