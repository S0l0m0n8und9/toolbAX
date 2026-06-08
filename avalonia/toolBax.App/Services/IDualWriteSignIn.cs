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
