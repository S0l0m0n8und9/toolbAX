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
            var (profileStore, secretStore, authService) = BuildServices();
            window.DataContext = new ShellViewModel(
                profileStore: profileStore,
                secretStore: secretStore,
                clipboard: new WindowClipboardService(window),
                authService: authService);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Profile + secret + auth services share ONE ProfileService (and, on Windows, one SecretVault) so
    // service-principal reads/writes stay consistent across them. The DPAPI vault + MSAL auth are
    // Windows-only; elsewhere (and on a DB failure) we degrade to in-memory fakes rather than crash.
    private static (IProfileStore Profiles, ISecretStore Secrets, IAuthService Auth) BuildServices()
    {
        try
        {
            var store = new ProfileStore(ProfilePaths.ResolveProfileDbPath());
            var profiles = new ProfileService(store);
            var profileStore = CoreProfileStore.CreateAsync(profiles).GetAwaiter().GetResult();

            if (OperatingSystem.IsWindows())
            {
                // Reuse ProfileStore's connection string (escaped, foreign keys on) for the vault.
                var vault = new SecretVaultService(store.ConnectionString);
                return (profileStore, new CoreSecretStore(profiles, vault), new CoreAuthService(profiles, vault));
            }

            return (profileStore, new FakeSecretStore(), new FakeAuthService());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile store unavailable; starting with empty in-memory stores. {ex}");
            return (new FakeProfileStore(Array.Empty<EnvProfile>()), new FakeSecretStore(), new FakeAuthService());
        }
    }
}
