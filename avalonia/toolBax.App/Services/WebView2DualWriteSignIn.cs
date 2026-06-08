#if WEBVIEW2
using System;
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

    public WebView2DualWriteSignIn(Window owner) => _owner = owner;

    public async Task<DualWriteSignInResult?> SignInAsync(EnvProfile env, bool switchAccount = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(env.Url))
        {
            throw new InvalidOperationException("Set the F&O environment URL first.");
        }

        // The portal's axenv identifier is the F&O environment URL (the WPF plugin passes BaseUrl as-is;
        // the gateway lookup normalises it to a host later).
        var dialog = new DualWriteSignInDialog(env.Url, switchAccount);
        using var reg = ct.CanBeCanceled
            ? ct.Register(() => Dispatcher.UIThread.Post(dialog.Close))
            : default;

        var capture = dialog.ResultAsync();
        await dialog.ShowDialog(_owner).ConfigureAwait(true);
        return await capture.ConfigureAwait(true);
    }
}

/// <summary>The modal sign-in window: a full-window WebView2 host that captures the token + gateway.</summary>
[SupportedOSPlatform("windows")]
internal sealed class DualWriteSignInDialog : Window
{
    private readonly string _foIdentifier;
    private readonly bool _switchAccount;
    private readonly DualWriteSignInCapture _capture = new();
    private readonly WebView2Host _host = new();
    private readonly TaskCompletionSource<DualWriteSignInResult?> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _completed;

    public DualWriteSignInDialog(string foIdentifier, bool switchAccount)
    {
        _foIdentifier = foIdentifier;
        _switchAccount = switchAccount;
        Title = "Data Integrator sign-in";
        Width = 920;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _host;
        _host.BrowserReady += OnBrowserReady;
        Closed += OnClosed;
    }

    /// <summary>Resolves with the captured result (null if cancelled / nothing captured).</summary>
    public Task<DualWriteSignInResult?> ResultAsync() => _tcs.Task;

    private void OnBrowserReady(object? sender, EventArgs e)
    {
        var browser = _host.Browser;
        if (browser is null)
        {
            Complete(null);
            return;
        }

        // "Switch account": forget the persisted browser session so Entra re-prompts.
        if (_switchAccount)
        {
            try
            {
                browser.CookieManager.DeleteAllCookies();
            }
            catch
            {
                // Best-effort: fall through to a normal (cached) sign-in if clearing fails.
            }
        }

        browser.WebResourceResponseReceived += OnResponseReceived;
        browser.Navigate(DualWriteAuthConstants.BuildSignInUrl(_foIdentifier));
    }

    private async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (_completed)
        {
            return;
        }

        var url = e.Request?.Uri;
        _capture.ObserveUrl(url);

        if (DualWriteSignInCapture.IsTokenEndpoint(url))
        {
            try
            {
                using var stream = await e.Response.GetContentAsync();
                if (stream is not null)
                {
                    using var reader = new StreamReader(stream);
                    _capture.ObserveTokenResponseBody(await reader.ReadToEndAsync());
                }
            }
            catch
            {
                // Body not retained/available — keep watching.
            }
        }

        if (_capture.IsComplete)
        {
            Complete(_capture.Result);
        }
    }

    private void Complete(DualWriteSignInResult? result)
    {
        if (_completed)
        {
            return;
        }

        _completed = true;
        _tcs.TrySetResult(result);
        Dispatcher.UIThread.Post(Close);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Closed before an API call pinned the regional gateway → fall back to the best-effort result so a
        // manual close still works (mirrors the WPF window).
        if (!_completed)
        {
            _completed = true;
            _tcs.TrySetResult(_capture.BestEffortResult);
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

    /// <summary>The underlying browser, available after <see cref="BrowserReady"/>.</summary>
    public CoreWebView2? Browser { get; private set; }

    /// <summary>Raised on the UI thread once the WebView2 controller is created.</summary>
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
            _controller = await environment.CreateCoreWebView2ControllerAsync(hwnd);
            Browser = _controller.CoreWebView2;
            UpdateBounds();
            BrowserReady?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            // WebView2 runtime missing or failed to start — signal "no browser"; the dialog completes null.
            BrowserReady?.Invoke(this, EventArgs.Empty);
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
        try
        {
            _controller?.Close();
        }
        catch
        {
            // Best-effort teardown.
        }

        _controller = null;
        Browser = null;
        base.DestroyNativeControlCore(control);
    }
}
#endif
