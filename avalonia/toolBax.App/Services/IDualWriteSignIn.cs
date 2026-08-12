using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Drives the Data Integrator portal sign-in for an environment and returns the captured
/// <see cref="DualWriteSignInResult"/> — the delegated token <em>and</em> the auto-discovered regional
/// gateway host — exactly as the WPF plugin does. This is the only flow that yields both: the first-party
/// Data Integrator app (<see cref="DualWriteAuthConstants.ClientId"/>) is registered with the portal
/// redirect (not <c>http://localhost</c>), so loopback MSAL can't be used — the token must be captured
/// from an embedded browser driving the portal. The real implementation hosts WebView2 (Windows) and
/// feeds the UI-free Core <see cref="DualWriteSignInCapture"/>; tests use a fake.
/// </summary>
public interface IDualWriteSignIn
{
    /// <summary>
    /// Signs in for the environment's F&amp;O identifier (its URL host). Returns the captured token +
    /// gateway host, or <c>null</c> if the user cancelled or sign-in failed. When
    /// <paramref name="switchAccount"/> is true the cached browser session is forgotten so Entra
    /// re-prompts for an account.
    /// </summary>
    Task<DualWriteSignInResult?> SignInAsync(EnvProfile env, bool switchAccount = false, CancellationToken ct = default);
}

/// <summary>
/// The sign-in window's title. The environment travels to the sign-in on <see cref="EnvProfile"/> already,
/// so no plumbing is needed to name it — but an unlabelled "Data Integrator sign-in" is unattributable the
/// moment more than one environment is involved (Compare signs into two), which is exactly when the user
/// has to decide which account to use. The title is composed here rather than in the WebView2 dialog so it
/// is coverable by the headless tests (the dialog itself is Windows-only, behind <c>#if WEBVIEW2</c>).
/// </summary>
public static class DualWriteSignInTitle
{
    /// <summary>The unqualified title, used when the profile has no usable name.</summary>
    public const string Unqualified = "Data Integrator sign-in";

    /// <summary>e.g. "USMF Dev — Data Integrator sign-in".</summary>
    public static string For(EnvProfile env)
    {
        var name = env?.Name?.Trim();
        return string.IsNullOrEmpty(name) ? Unqualified : $"{name} — {Unqualified}";
    }
}

/// <summary>
/// Sign-in failures that need to reach the caller as themselves. <see cref="IDualWriteSignIn"/> reports a
/// cancelled sign-in by returning <c>null</c>, which the connector turns into "cancelled or did not
/// complete" — accurate for a closed window, actively misleading for an environment that never had a
/// browser to close. Composed here (not in the Windows-only WebView2 dialog) so it is unit-testable.
/// </summary>
public static class DualWriteSignInFailure
{
    /// <summary>
    /// The WebView2 runtime is a separate install from the app, so this is a first-run/deployment
    /// condition, not user error — the message has to say what to install.
    /// </summary>
    public const string BrowserUnavailable =
        "The WebView2 runtime is not installed — install the Evergreen WebView2 runtime to use dual-write sign-in.";

    /// <summary>
    /// The exception to fail the sign-in with when the embedded browser never came up. Carries the
    /// underlying reason in the message (the runtime can also be present-but-broken: a locked/unwritable
    /// user-data folder, a blocked-by-policy install) and keeps the original as the inner exception.
    /// </summary>
    public static Exception BrowserUnavailableError(Exception? error) =>
        new InvalidOperationException(
            error is null ? BrowserUnavailable : $"{BrowserUnavailable} ({error.Message})",
            error);
}
