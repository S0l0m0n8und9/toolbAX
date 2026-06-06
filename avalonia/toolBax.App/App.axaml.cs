using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FoToolbox.Core.Profiles;
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
            // to the shell. Profiles + secrets are real (the shared FoToolbox profile.db); the remaining
            // seams stay design-mode fakes pending live wiring. Building the stores synchronously here
            // is safe — it runs once at startup before the dispatcher loop begins.
            var window = new MainWindow();
            var (profileStore, secretStore) = BuildProfileServices();
            window.DataContext = new ShellViewModel(
                profileStore: profileStore,
                secretStore: secretStore,
                clipboard: new WindowClipboardService(window));
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Profile + secret stores share ONE ProfileService so service-principal reads/writes stay
    // consistent across them. On a DB failure (locked/corrupt/unreadable) we degrade to in-memory
    // fakes rather than crash before the window opens.
    private static (IProfileStore Profiles, ISecretStore Secrets) BuildProfileServices()
    {
        try
        {
            var dbPath = ProfilePaths.ResolveProfileDbPath();
            var profiles = new ProfileService(new ProfileStore(dbPath));
            var profileStore = CoreProfileStore.CreateAsync(profiles).GetAwaiter().GetResult();

            // The DPAPI secret vault is Windows-only; elsewhere fall back to the in-memory fake.
            ISecretStore secretStore = OperatingSystem.IsWindows()
                ? new CoreSecretStore(profiles, new SecretVaultService($"Data Source={dbPath}"))
                : new FakeSecretStore();

            return (profileStore, secretStore);
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile store unavailable; starting with empty in-memory stores. {ex}");
            return (new FakeProfileStore(Array.Empty<EnvProfile>()), new FakeSecretStore());
        }
    }
}
