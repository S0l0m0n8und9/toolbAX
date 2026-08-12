using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// The two user-facing details of the dual-write portal sign-in that live OUTSIDE the WebView2 control
/// (#168 lows): which environment a modal sign-in window belongs to, and what the user is told when the
/// embedded browser never comes up. The WebView2 host and its dialog need the Windows runtime and a real
/// HWND, so neither is reachable from the headless tests — both are deliberately thin adapters over the
/// seams asserted here, and the failure path is then followed through the real connector.
/// </summary>
public class DualWriteSignInTests
{
    private static EnvProfile Env(string name) =>
        new("env-1", name, "https://contoso-dev.operations.dynamics.com", "contoso.onmicrosoft.com",
            "USMF", "Tier 2", EnvStatus.Connected);

    [Fact]
    public void A_sign_in_window_is_titled_with_its_environment()
    {
        // Compare signs into two environments back to back, so a bare "Data Integrator sign-in" leaves the
        // user guessing which environment the window in front of them wants an account for.
        Assert.Equal("USMF Dev — Data Integrator sign-in", DualWriteSignInTitle.For(Env("USMF Dev")));
        Assert.Equal("USMF UAT — Data Integrator sign-in", DualWriteSignInTitle.For(Env("  USMF UAT  ")));
    }

    [Fact]
    public void An_unnamed_environment_falls_back_to_the_unqualified_title()
    {
        Assert.Equal(DualWriteSignInTitle.Unqualified, DualWriteSignInTitle.For(Env(string.Empty)));
        Assert.Equal(DualWriteSignInTitle.Unqualified, DualWriteSignInTitle.For(Env("   ")));
    }

    [Fact]
    public void A_browser_that_never_starts_reports_the_missing_runtime_and_its_cause()
    {
        var inner = new InvalidOperationException("Couldn't find a compatible WebView2 Runtime installation.");

        var error = DualWriteSignInFailure.BrowserUnavailableError(inner);

        Assert.Contains("WebView2 runtime is not installed", error.Message, StringComparison.Ordinal);
        Assert.Contains("Evergreen WebView2 runtime", error.Message, StringComparison.Ordinal);
        Assert.Contains(inner.Message, error.Message, StringComparison.Ordinal);   // the underlying reason
        Assert.DoesNotContain("cancel", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Same(inner, error.InnerException);   // the full exception stays available for diagnostics
    }

    [Fact]
    public void A_browser_failure_with_no_captured_cause_still_names_the_runtime()
    {
        var error = DualWriteSignInFailure.BrowserUnavailableError(null);

        Assert.Equal(DualWriteSignInFailure.BrowserUnavailable, error.Message);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task A_failed_sign_in_reaches_the_caller_as_itself_not_as_a_cancellation()
    {
        // What Compare/Operations display is the message out of ConnectAsync. A sign-in that FAILED keeps
        // its own message all the way there — the runtime-missing case is not a user who changed their mind.
        var connector = new CoreDualWriteConnector(
            new FailingSignIn(DualWriteSignInFailure.BrowserUnavailableError(
                new InvalidOperationException("runtime not found"))));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.ConnectAsync(Env("USMF Dev"), CancellationToken.None));

        Assert.Contains("WebView2 runtime is not installed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("runtime not found", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("cancel", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_genuinely_cancelled_sign_in_is_still_reported_as_a_cancellation()
    {
        // The counterpart: returning nothing (the window was closed) keeps the cancellation message, so the
        // distinct runtime message above is a real distinction and not a blanket rewording.
        var connector = new CoreDualWriteConnector(new FakeDualWriteSignIn(null));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.ConnectAsync(Env("USMF Dev"), CancellationToken.None));

        Assert.Contains("cancelled", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("WebView2", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>A sign-in that fails outright, as the WebView2 adapter now does when no browser starts.</summary>
    private sealed class FailingSignIn : IDualWriteSignIn
    {
        private readonly Exception _error;

        public FailingSignIn(Exception error) => _error = error;

        public Task<DualWriteSignInResult?> SignInAsync(
            EnvProfile env, bool switchAccount = false, CancellationToken ct = default) =>
            Task.FromException<DualWriteSignInResult?>(_error);
    }
}
