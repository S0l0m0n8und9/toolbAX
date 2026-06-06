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
            var (profileStore, secretStore, authService, odataFactory) = BuildServices();

            // The OData client mints a token for whichever environment is active *at send time*, so it
            // reads the shell's ActiveEnvironment through a closure. The shell is assigned below before
            // any send can fire (the user must navigate + click). `shell` stays genuinely nullable and
            // the closure null-conditional, so even an unexpected eager evaluation yields a graceful
            // "no active environment" response rather than a NullReferenceException.
            ShellViewModel? shell = null;
            var odataClient = odataFactory(() => shell?.ActiveEnvironment);
            shell = new ShellViewModel(
                profileStore: profileStore,
                secretStore: secretStore,
                clipboard: new WindowClipboardService(window),
                authService: authService,
                odataClient: odataClient);
            window.DataContext = shell;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Profile + secret + auth services share ONE ProfileService (and, on Windows, one SecretVault) so
    // service-principal reads/writes stay consistent across them. The DPAPI vault + MSAL auth are
    // Windows-only; elsewhere (and on a DB failure) we degrade to in-memory fakes rather than crash.
    // The OData factory takes the shell's active-environment accessor (only known after the shell is
    // built) and pairs a real authed client with the real auth path, or the fake sample client with
    // the degraded one — so an offline/non-Windows run still demos without firing real HTTP.
    private static (IProfileStore Profiles, ISecretStore Secrets, IAuthService Auth,
        Func<Func<EnvProfile?>, IODataClient> ODataFactory) BuildServices()
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
                var auth = new CoreAuthService(profiles, vault);
                return (profileStore, new CoreSecretStore(profiles, vault), auth,
                    activeEnv => new CoreODataClient(auth, activeEnv));
            }

            return (profileStore, new FakeSecretStore(), new FakeAuthService(), _ => new FakeODataClient());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile store unavailable; starting with empty in-memory stores. {ex}");
            return (new FakeProfileStore(Array.Empty<EnvProfile>()), new FakeSecretStore(),
                new FakeAuthService(), _ => new FakeODataClient());
        }
    }
}
