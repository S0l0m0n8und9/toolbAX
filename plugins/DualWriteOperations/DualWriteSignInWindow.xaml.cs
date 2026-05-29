using FoToolbox.Core.DualWrite.Auth;
using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace DualWriteOperationsPlugin;

/// <summary>
/// Embedded-browser interactive sign-in. Drives the Data Integrator portal and watches its
/// network traffic to capture, in one flow, the delegated access/refresh token (from the
/// Entra token-endpoint response) and the regional gateway host (from the first
/// <c>projectmanagementservice</c> request) — the only mechanism that yields both, mirroring
/// the MS tool's CDP sniffing with WebView2. Renewal afterwards is the clean refresh POST.
/// </summary>
public partial class DualWriteSignInWindow : Window
{
    private readonly string _foIdentifier;
    private readonly DualWriteSignInCapture _capture = new();
    private readonly TaskCompletionSource<DualWriteSignInResult?> _tcs = new();
    private bool _completed;

    public DualWriteSignInWindow(string foIdentifier)
    {
        InitializeComponent();
        _foIdentifier = foIdentifier;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    /// <summary>Shows the window and resolves with the captured result (null if cancelled/failed).</summary>
    public Task<DualWriteSignInResult?> SignInAsync()
    {
        Show();
        return _tcs.Task;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await Web.EnsureCoreWebView2Async();
            Web.CoreWebView2.WebResourceResponseReceived += OnResponseReceived;
            Web.CoreWebView2.Navigate(DualWriteAuthConstants.BuildSignInUrl(_foIdentifier));
        }
        catch (Exception)
        {
            // WebView2 runtime missing or failed to start — treat as cancelled.
            Complete(null);
        }
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
                // Body not retained/available — ignore and keep watching.
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
        Dispatcher.BeginInvoke(new Action(Close));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // If the user closed the window before capture finished, surface whatever we have (null = cancelled).
        if (!_completed)
        {
            _completed = true;
            _tcs.TrySetResult(_capture.Result);
        }
    }
}
