using System;
using Avalonia;

namespace ToolBax.App;

internal static class Program
{
    // Avalonia configuration; also used by the headless test harness builder.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
