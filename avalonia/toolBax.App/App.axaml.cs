using System;
using System.Diagnostics;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FoToolbox.Core.Catalog;
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
            var (profileStore, secretStore, authService, odataFactory, metadataFactory, mapReaderFactory) = BuildServices();

            // The OData client + metadata service resolve a token / $metadata for whichever environment
            // is active *at call time*, so they read the shell's ActiveEnvironment through a closure. The
            // shell is assigned below before any of those can fire (the user must navigate + act). `shell`
            // stays genuinely nullable and the closure null-conditional, so even an unexpected eager
            // evaluation yields a graceful "no active environment" result rather than a NullReferenceException.
            ShellViewModel? shell = null;
            Func<EnvProfile?> activeEnv = () => shell?.ActiveEnvironment;
            var odataClient = odataFactory(activeEnv);
            var metadataService = metadataFactory(activeEnv);
            var mapReader = mapReaderFactory(activeEnv);
            shell = new ShellViewModel(
                profileStore: profileStore,
                secretStore: secretStore,
                // Real loopback-MSAL interactive sign-in (system browser, no WebView2); cross-platform,
                // caches the delegated token for a later silent dual-write gateway acquisition.
                authBroker: new CoreInteractiveAuthBroker(),
                clipboard: new WindowClipboardService(window),
                authService: authService,
                odataClient: odataClient,
                metadataService: metadataService,
                mapReader: mapReader,
                fileSave: new StorageFileSaveService(window));
            window.DataContext = shell;
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Profile + secret + auth services share ONE ProfileService (and, on Windows, one SecretVault) so
    // service-principal reads/writes stay consistent across them. The DPAPI vault + MSAL auth are
    // Windows-only; elsewhere (and on a DB failure) we degrade to in-memory fakes rather than crash.
    // The OData + metadata factories take the shell's active-environment accessor (only known after the
    // shell is built) and pair real authed services with the real auth path, or fake sample services
    // with the degraded one — so an offline/non-Windows run still demos without firing real HTTP.
    private static (IProfileStore Profiles, ISecretStore Secrets, IAuthService Auth,
        Func<Func<EnvProfile?>, IODataClient> ODataFactory,
        Func<Func<EnvProfile?>, IMetadataService> MetadataFactory,
        Func<Func<EnvProfile?>, IDualWriteMapReader> MapReaderFactory) BuildServices()
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
                    activeEnv => new CoreODataClient(auth, activeEnv),
                    activeEnv => CreateMetadataService(store, auth, activeEnv),
                    activeEnv => new CoreDualWriteMapReader(new CoreDataverseClient(auth, activeEnv)));
            }

            return (profileStore, new FakeSecretStore(), new FakeAuthService(),
                _ => new FakeODataClient(), _ => new FakeMetadataService(), _ => new FakeDualWriteMapReader());
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile store unavailable; starting with empty in-memory stores. {ex}");
            return (new FakeProfileStore(Array.Empty<EnvProfile>()), new FakeSecretStore(),
                new FakeAuthService(), _ => new FakeODataClient(), _ => new FakeMetadataService(),
                _ => new FakeDualWriteMapReader());
        }
    }

    // FoToolbox.Core's CatalogService fetches + caches OData $metadata over a plain HttpClient; wrap one
    // with AuthenticatedHttpHandler so it carries the active environment's bearer token, and cache to a
    // SQLite catalog.db under %LocalAppData% (cross-platform, no DPAPI).
    private static IMetadataService CreateMetadataService(ProfileStore store, IAuthService auth, Func<EnvProfile?> activeEnv)
    {
        var http = new HttpClient(new AuthenticatedHttpHandler(auth, activeEnv) { InnerHandler = new HttpClientHandler() });
        var catalogStore = new CatalogStore(ProfilePaths.ResolveAppDataPath("catalog.db"));
        var catalog = new CatalogService(http, store, catalogStore);
        return new CoreMetadataService(catalog, activeEnv);
    }
}
