#if WEBVIEW2
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using FoToolbox.Core.DualWrite.Auth;
using Microsoft.Web.WebView2.Core;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteSignIn"/> for Windows: embeds WebView2 (via Avalonia's
/// <see cref="NativeControlHost"/>), drives the Data Integrator portal, and sniffs its traffic to capture
/// the delegated token <em>and</em> the regional gateway host — a faithful port of the WPF Operations
/// plugin's sign-in window. The first-party Data Integrator app is registered with the portal redirect
/// (not <c>http://localhost</c>), so this embedded-browser capture is the only flow that works; loopback
/// MSAL fails with AADSTS50011. The capture logic itself is the UI-free, unit-tested Core
/// <see cref="DualWriteSignInCapture"/>; this class is just the WebView2 adapter over it.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WebView2DualWriteSignIn : IDualWriteSignIn
{
    private readonly Window _owner;
    private readonly DualWriteSignInSequencer _sequencer = new();

    public WebView2DualWriteSignIn(Window owner) => _owner = owner;

    public async Task<DualWriteSignInResult?> SignInAsync(EnvProfile env, bool switchAccount = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("Set the F&O environment URL first.");
        }

        return await _sequencer.RunAsync(
            () => new DualWriteSignInDialog(_owner, env, switchAccount), ct).ConfigureAwait(true);
    }
}

/// <summary>The modal sign-in window: a full-window WebView2 host that captures the token + gateway.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DualWriteSignInDialog : Window, IDualWriteSignInAttempt
{
    private readonly Window _owner;
    private readonly string _foIdentifier;
    private readonly bool _switchAccount;
    private readonly DualWriteSignInCapture _capture;
    private readonly WebView2Host _host = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DualWriteClosePreparationCoordinator _closeCoordinator;
    private readonly TaskCompletionSource<DualWriteSignInResult?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _preparationTask = Task.CompletedTask;
    private bool _completed;
    private bool _resumeOwnerCloseAfterClosed;

    public DualWriteSignInDialog(Window owner, EnvProfile env, bool switchAccount)
    {
        _owner = owner;
        _foIdentifier = env.Url;
        _switchAccount = switchAccount;
        _capture = DualWriteNativeCapture.Create(env);
        _closeCoordinator = new DualWriteClosePreparationCoordinator(
            action => Dispatcher.UIThread.Post(action), Close);
        Title = DualWriteSignInTitle.For(env);
        Width = 920;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _host;
        _host.BrowserReady += OnBrowserReady;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    public async Task<DualWriteSignInResult?> RunAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(Cancel);
        await ShowDialog(_owner).ConfigureAwait(true);
        return await _tcs.Task.ConfigureAwait(true);
    }

    public void Cancel()
    {
        _closeCoordinator.Deactivate();
        if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible) Close();
        });
    }

    public async Task DrainPreparationAsync()
    {
        try { await _closeCoordinator.DrainPreparationAsync().ConfigureAwait(true); }
        catch { /* The controlled failure already completed the dialog. */ }
    }

    private async void OnBrowserReady(object? sender, EventArgs e)
    {
        if (IsInactive) return;
        var browser = _host.Browser;
        if (browser is null)
        {
            // The embedded browser never came up — the WebView2 runtime is missing, or present and unable
            // to start. Completing with a null result would reach the user as "sign-in was cancelled or did
            // not complete", but there was never a window to cancel: the real cause has to travel.
            Fail(DualWriteSignInFailure.BrowserUnavailableError(_host.InitializationError));
            return;
        }

        _preparationTask = PrepareBrowserAsync(browser);
        _closeCoordinator.TrackPreparation(_preparationTask);
        try
        {
            await _preparationTask.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!IsInactive)
                Fail(DualWriteSignInFailure.BrowserPreparationError(ex));
            return;
        }

        // Preparation owns navigation and rechecks dialog lifetime after the uninterruptible clear.
    }

    private Task PrepareBrowserAsync(CoreWebView2 browser)
    {
        var policy = DualWriteBrowserPreparation.ResetKinds(_switchAccount);
        var kinds = CoreWebView2BrowsingDataKinds.DiskCache |
                    (policy.HasFlag(DualWriteBrowserDataKinds.Cookies)
                        ? CoreWebView2BrowsingDataKinds.AllSite
                        : CoreWebView2BrowsingDataKinds.AllDomStorage);
        return DualWriteBrowserPreparation.PrepareAndNavigateAsync(
            () => browser.Profile.ClearBrowsingDataAsync(kinds),
            () =>
            {
                browser.WebResourceResponseReceived += OnResponseReceived;
                browser.Navigate(DualWriteAuthConstants.BuildSignInUrl(_foIdentifier));
            },
            () => IsInactive);
    }

    private async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (IsInactive) return;
        var request = e.Request;
        if (request is null) return;
        try
        {
            if (DualWriteSignInCapture.IsTokenEndpoint(request.Uri))
            {
                var observation = await DualWriteCommittedResponseReader.ReadTokenExchangeAsync(
                    request.Uri,
                    request.Method,
                    Header(request, "Content-Type"),
                    request.Content,
                    e.Response.StatusCode,
                    async () => await e.Response.GetContentAsync(),
                    _lifetime.Token).ConfigureAwait(true);
                if (observation is not null)
                    await _capture.ObserveTokenExchangeAsync(observation, _lifetime.Token).ConfigureAwait(true);
            }

            var gateway = DualWriteCommittedResponseReader.CreateGatewayObservation(
                request.Uri, Header(request, "Authorization"), e.Response.StatusCode);
            if (gateway is not null)
            {
                _capture.ObserveGatewayResponse(gateway);
            }
        }
        catch (OperationCanceledException) { return; }
        catch { return; }

        if (!IsInactive && _capture.IsComplete)
        {
            Complete(_capture.Result);
        }
    }

    private static string? Header(CoreWebView2WebResourceRequest request, string name)
    {
        try { return request.Headers.GetHeader(name); }
        catch { return null; }
    }

    private void Complete(DualWriteSignInResult? result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _closeCoordinator.Deactivate();
        _lifetime.Cancel();
        _tcs.TrySetResult(result);
        Dispatcher.UIThread.Post(Close);
    }

    /// <summary>
    /// Completes the sign-in as a FAILURE: the exception surfaces from <c>SignInAsync</c> so the calling
    /// screen (Compare / Operations) reports this cause instead of the generic cancelled-sign-in message.
    /// Also marks the dialog complete, so the manual-close fallback in <see cref="OnClosed"/> can't
    /// overwrite it with a best-effort (empty) result.
    /// </summary>
    private void Fail(Exception error)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _closeCoordinator.Deactivate();
        _lifetime.Cancel();
        _tcs.TrySetException(error);
        Dispatcher.UIThread.Post(Close);
    }

    private bool IsInactive =>
        _completed || _lifetime.IsCancellationRequested || _closeCoordinator.IsInactive;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _closeCoordinator.Deactivate();
        if (!_lifetime.IsCancellationRequested) _lifetime.Cancel();
        if (!_completed)
        {
            _completed = true;
            _tcs.TrySetResult(null);
        }

        var ownerIsClosing = e.CloseReason == WindowCloseReason.OwnerWindowClosing;
        var shutdownCannotWait = e.CloseReason is WindowCloseReason.ApplicationShutdown
            or WindowCloseReason.OSShutdown;
        if (_closeCoordinator.RequestClose(shutdownCannotWait) == DualWriteCloseDecision.DeferAndHide)
        {
            _resumeOwnerCloseAfterClosed |= ownerIsClosing;
            e.Cancel = true;
            if (IsVisible) Hide();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closeCoordinator.MarkClosed();
        _lifetime.Cancel();
        Closing -= OnClosing;
        if (_host.Browser is { } browser)
            browser.WebResourceResponseReceived -= OnResponseReceived;
        if (!_completed)
        {
            _completed = true;
            _tcs.TrySetResult(null);
        }
        if (_resumeOwnerCloseAfterClosed)
        {
            _resumeOwnerCloseAfterClosed = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (_owner.IsVisible) _owner.Close();
            });
        }
    }
}

/// <summary>
/// Hosts a WebView2 control inside Avalonia via <see cref="NativeControlHost"/>: creates the
/// <see cref="CoreWebView2Controller"/> over the host HWND and keeps its bounds in sync with the control.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WebView2Host : NativeControlHost
{
    private CoreWebView2Controller? _controller;
    private readonly DualWriteNativeHostLifetime _lifetime = new();

    /// <summary>The underlying browser, available after <see cref="BrowserReady"/>.</summary>
    public CoreWebView2? Browser { get; private set; }

    /// <summary>
    /// Why <see cref="Browser"/> is null after <see cref="BrowserReady"/> — the WebView2 runtime is missing
    /// (<c>WebView2RuntimeNotFoundException</c>) or failed to start. Null when the browser came up. Kept so
    /// the dialog can report the real cause rather than an indistinguishable "no browser".
    /// </summary>
    public Exception? InitializationError { get; private set; }

    /// <summary>
    /// Raised on the UI thread once the WebView2 controller is created — or once creation has definitively
    /// failed, in which case <see cref="Browser"/> is null and <see cref="InitializationError"/> is set.
    /// </summary>
    public event EventHandler? BrowserReady;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);
        _ = InitializeAsync(handle.Handle);
        return handle;
    }

    private async Task InitializeAsync(IntPtr hwnd)
    {
        try
        {
            // A dedicated, app-scoped user-data folder under %LocalAppData% (WebView2 needs a writable
            // location). Not %TEMP% — that's swept by OS/3rd-party cleaners, which would wipe the cached
            // session and force a full re-sign-in every time.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "toolBax", "webview2");
            Directory.CreateDirectory(userDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, null);
            var controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);
            if (!_lifetime.TryPublish(() =>
                {
                    _controller = controller;
                    Browser = controller.CoreWebView2;
                    UpdateBounds();
                    BrowserReady?.Invoke(this, EventArgs.Empty);
                }))
            {
                TryClose(controller);
            }
        }
        catch (Exception ex)
        {
            // WebView2 runtime missing or failed to start. Keep the exception: swallowing it left the dialog
            // completing null, which the connector reports as a cancelled sign-in — so a machine without the
            // runtime looked like a user who changed their mind. Full exception to the trace log (stack +
            // inner exceptions, for a support dump); only the message travels to the UI.
            _lifetime.TryPublish(() =>
            {
                InitializationError = ex;
                Trace.WriteLine($"Dual-write sign-in: WebView2 initialization failed.{Environment.NewLine}{ex}");
                BrowserReady?.Invoke(this, EventArgs.Empty);
            });
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateBounds();
        return size;
    }

    private void UpdateBounds()
    {
        if (_controller is not null)
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)Bounds.Width, (int)Bounds.Height);
        }
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _lifetime.Destroy(() =>
        {
            if (_controller is not null) TryClose(_controller);
            _controller = null;
            Browser = null;
        });
        base.DestroyNativeControlCore(control);
    }

    private static void TryClose(CoreWebView2Controller controller)
    {
        try { controller.Close(); }
        catch { /* Best-effort teardown. */ }
    }
}
#endif
