using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
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
            // Persistent per-session log (#168), started before anything else so a failure inside
            // BuildServices below is already being written to file. The published exe had no trace
            // listener at all, so every Trace.* report — the degraded-mode reason, the last-resort net's
            // exception dumps, failed requests — evaporated and a user's error left nothing on disk.
            // Scoped to the real desktop host exactly like the last-resort net further down: headless
            // tests must not write log files. The handle is intentionally not stored — the listener lives
            // for the process lifetime and flushes on every write, so a crash still keeps the tail.
            _ = SessionTraceLog.Start();

            // Create the window first so the clipboard service can bind to its TopLevel, then hand it
            // to the shell. Profiles + secrets are real (the shared FoToolbox profile.db); the remaining
            // seams stay design-mode fakes pending live wiring. Building the stores synchronously here
            // is safe — it runs once at startup before the dispatcher loop begins.
            var window = new MainWindow();
            var (profileStore, secretStore, authService, odataFactory, metadataFactory, mapReaderFactory, virtualTableReaderFactory, degraded) = BuildServices();

            // The shell shouts about degradation on screen (#164), but a user reporting "it showed me
            // rows that don't exist" days later needs the reason on disk too. BuildServices only traces
            // the exception case; this catches every reason, including the non-Windows one.
            if (degraded is not null)
            {
                Trace.TraceWarning($"Starting in degraded mode ({degraded.Reason}) — data shown is offline sample data, not live.");
            }

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
            var virtualTableReader = virtualTableReaderFactory(activeEnv);
            // Dual-write portal sign-in captures the delegated token AND auto-discovers the regional
            // gateway host (no client id / manual gateway URL), mirroring the WPF plugin. The real capture
            // hosts WebView2 via Avalonia's NativeControlHost and is Windows-only; until that adapter is
            // wired the fake yields a seeded result so design-mode/headless flows still exercise the path.
#if WEBVIEW2
            IDualWriteSignIn dualWriteSignIn = OperatingSystem.IsWindows()
                ? new WebView2DualWriteSignIn(window)
                : new FakeDualWriteSignIn();
#else
            IDualWriteSignIn dualWriteSignIn = new FakeDualWriteSignIn();
#endif
            // The gateway tester/connector pair with the real auth (portal sign-in + live gateway); fall
            // back to the canned fake when auth is degraded/non-Windows so design-mode doesn't hit the network.
            var gatewayTester = authService is CoreAuthService
                ? (IDualWriteGatewayTester)new CoreDualWriteGatewayTester(dualWriteSignIn)
                : new FakeDualWriteGatewayTester();
            // The Operations screen connects to the live gateway via the real connector when auth is real;
            // otherwise the seeded fake so design-mode lists sample maps.
            var dwConnector = authService is CoreAuthService
                ? (IDualWriteConnector)new CoreDualWriteConnector(dualWriteSignIn)
                : new FakeDualWriteConnector();
            // Compare connects to two environments' gateways via the same connector, then diffs.
            var compareService = authService is CoreAuthService
                ? (IDualWriteCompareService)new CoreDualWriteCompareService(dwConnector)
                : new FakeDualWriteCompareService();
            // "Test connection" forces a fresh token and probes the real data endpoint ($metadata /
            // WhoAmI); with degraded/non-Windows auth the fake reports a canned pass so design-mode
            // doesn't hit the network.
            var connectionTester = authService is CoreAuthService
                ? (IConnectionTester)new CoreConnectionTester(authService)
                : new FakeConnectionTester();
            shell = new ShellViewModel(
                operationsContentFactory: () => new DualWriteOpsViewModel(dwConnector, activeEnv, new DialogService(), odata: odataClient, metadata: metadataService),
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
                fileSave: new StorageFileSaveService(window),
                gatewayTester: gatewayTester,
                compareService: compareService,
                connectionTester: connectionTester,
                launcher: new WindowUrlLauncher(window),
                virtualTableReader: virtualTableReader,
                degraded: degraded);
            window.DataContext = shell;
            desktop.MainWindow = window;

            // The safety net below is deliberately scoped to the real desktop host: headless tests install it
            // themselves so a test's throwing job can't be swallowed by the app-wide handler.
            ShellViewModel shellForReports = shell;   // non-nullable local: the lambda's nullable flow state resets
            // The handle is intentionally not stored — the net lives for the process lifetime.
            _ = InstallLastResortExceptionHandlers(message =>
                Dispatcher.UIThread.Post(() => shellForReports.ReportBackgroundFailure(message)));
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Installs the last-resort exception net (#163) and returns a handle that removes it again.
    /// CommunityToolkit's AsyncRelayCommand rethrows a faulted command task on the dispatcher (it does not
    /// flow exceptions to the task scheduler), and an unobserved Task exception is rethrown on the finalizer
    /// thread — either one kills the process and takes every tool's unsaved state with it. This is a net for
    /// a path that missed its own try/catch, NOT a substitute for one: it traces the failure, hands
    /// <paramref name="report"/> a one-line message for the shell's status strip, and never terminates for a
    /// recoverable exception. <paramref name="report"/> may be invoked from any thread — marshalling to the
    /// UI thread is the caller's job.
    /// </summary>
    public static IDisposable InstallLastResortExceptionHandlers(Action<string> report)
    {
        void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Keep the app alive FIRST — everything after this line is best-effort reporting.
            e.Handled = true;
            Report(report, e.Exception, "A background action failed");
        }

        void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // Observe FIRST, so the finalizer thread can't rethrow while we're reporting.
            e.SetObserved();
            Report(report, e.Exception.GetBaseException(), "A background task failed");
        }

        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        return new Unsubscriber(() =>
        {
            Dispatcher.UIThread.UnhandledException -= OnDispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        });
    }

    // Traces the full exception, then hands the sink a one-liner. A failing sink must never recurse or
    // throw out of a last-resort handler, so its own failure is traced and swallowed.
    private static void Report(Action<string> report, Exception ex, string headline)
    {
        Trace.TraceError($"{headline}: {ex}");
        try
        {
            report($"{headline}: {ex.Message}");
        }
        catch (Exception reportFailure)
        {
            Trace.TraceError($"Reporting a background failure itself failed: {reportFailure}");
        }
    }

    // Removes exactly the handlers that were added, once — a second Dispose is a no-op.
    private sealed class Unsubscriber : IDisposable
    {
        private Action? _remove;

        public Unsubscriber(Action remove) => _remove = remove;

        public void Dispose()
        {
            var remove = _remove;
            _remove = null;
            remove?.Invoke();
        }
    }

    // Profile + secret + auth services share ONE ProfileService (and, on Windows, one SecretVault) so
    // service-principal reads/writes stay consistent across them. The DPAPI vault + MSAL auth are
    // Windows-only; elsewhere (and on a DB failure) we degrade to in-memory fakes rather than crash.
    // The OData + metadata factories take the shell's active-environment accessor (only known after the
    // shell is built) and pair real authed services with the real auth path, or fake sample services
    // with the degraded one — so an offline/non-Windows run still demos without firing real HTTP.
    // Whenever that degradation happens the returned Degraded descriptor says why, so the shell can shout
    // about it (#164) instead of letting fabricated rows/writes pass for a live environment.
    private static (IProfileStore Profiles, ISecretStore Secrets, IAuthService Auth,
        Func<Func<EnvProfile?>, IODataClient> ODataFactory,
        Func<Func<EnvProfile?>, IMetadataService> MetadataFactory,
        Func<Func<EnvProfile?>, IDualWriteMapReader> MapReaderFactory,
        Func<Func<EnvProfile?>, IVirtualTableReader> VirtualTableReaderFactory,
        DegradedMode? Degraded) BuildServices()
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
                    activeEnv => new CoreDualWriteMapReader(new CoreDataverseClient(auth, activeEnv)),
                    activeEnv => new CoreVirtualTableReader(new CoreDataverseClient(auth, activeEnv)),
                    null);
            }

            return (profileStore, new FakeSecretStore(), new FakeAuthService(),
                _ => new FakeODataClient(), _ => new FakeMetadataService(), _ => new FakeDualWriteMapReader(),
                _ => new FakeVirtualTableReader(),
                new DegradedMode("design mode — non-Windows platform"));
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Profile store unavailable; starting with empty in-memory stores. {ex}");
            return (new FakeProfileStore(Array.Empty<EnvProfile>()), new FakeSecretStore(),
                new FakeAuthService(), _ => new FakeODataClient(), _ => new FakeMetadataService(),
                _ => new FakeDualWriteMapReader(), _ => new FakeVirtualTableReader(),
                new DegradedMode($"profile store unavailable: {ex.Message}"));
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
