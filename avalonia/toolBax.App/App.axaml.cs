using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create the window first so the clipboard service can bind to its TopLevel, then hand it
            // to the shell. Profiles are real (the shared FoToolbox profile.db); the remaining seams
            // stay design-mode fakes pending live wiring. Loading the profile store synchronously here
            // is safe — it runs once at startup before the dispatcher loop begins.
            var window = new MainWindow();
            window.DataContext = new ShellViewModel(
                profileStore: LoadProfileStore(),
                clipboard: new WindowClipboardService(window));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IProfileStore LoadProfileStore()
    {
        try
        {
            return CoreProfileStore.CreateDefaultAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // The profile DB is unavailable (locked by another instance, corrupt, or unreadable).
            // Launch in degraded mode with an empty in-memory store rather than crashing before the
            // window opens — the user can still navigate and re-create profiles.
            Trace.TraceError($"Profile store unavailable; starting with an empty in-memory store. {ex}");
            return new FakeProfileStore(Array.Empty<EnvProfile>());
        }
    }
}
