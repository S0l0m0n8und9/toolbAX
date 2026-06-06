using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.App.Views;

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
            var profileStore = CoreProfileStore.CreateDefaultAsync().GetAwaiter().GetResult();
            window.DataContext = new ShellViewModel(
                profileStore: profileStore,
                clipboard: new WindowClipboardService(window));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
