using Avalonia;
using Avalonia.Headless;
using ToolBax.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace ToolBax.App.Tests;

/// <summary>
/// Headless Avalonia app for [AvaloniaFact]/[AvaloniaTheory]. Reuses the real <see cref="App"/> so
/// tests get the same FluentTheme + design-token resources the running app uses (no display server).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<ToolBax.App.App>()
            .WithInterFont()   // embedded font so headless text measurement works with no system fonts
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
