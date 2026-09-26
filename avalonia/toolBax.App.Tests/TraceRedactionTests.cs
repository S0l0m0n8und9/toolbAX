using System;
using ToolBax.App.Services;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class TraceRedactionTests
{
    [Theory]
    [InlineData((int)AppTraceEvent.SessionLoggingUnavailable, "Session logging is unavailable")]
    [InlineData((int)AppTraceEvent.SessionLogRetentionFailed, "Session-log retention failed")]
    [InlineData((int)AppTraceEvent.ProfileSecretCleanupFailed, "Profile secret cleanup failed")]
    [InlineData((int)AppTraceEvent.ProfileSessionEvictionFailed, "Profile session eviction failed")]
    [InlineData((int)AppTraceEvent.WebView2InitializationFailed, "sign-in browser initialization failed")]
    [InlineData((int)AppTraceEvent.BackgroundActionFailed, "background action failed")]
    [InlineData((int)AppTraceEvent.BackgroundTaskFailed, "background task failed")]
    [InlineData((int)AppTraceEvent.BackgroundFailureReportingFailed, "Background failure reporting failed")]
    [InlineData((int)AppTraceEvent.ProfileStoreUnavailable, "Profile store unavailable")]
    [InlineData((int)AppTraceEvent.CompositionPreferenceReadFailed, "Composition preference read failed")]
    [InlineData((int)AppTraceEvent.DegradedMode, "Starting in degraded mode")]
    public void App_diagnostics_keep_finite_category_and_type_but_omit_identifiers_and_exception_text(
        int traceEventValue,
        string expectedCategory)
    {
        using var trace = new TraceCapture();
        const string profileCanary = "PROFILE-NAME-CANARY";
        const string secretRefCanary = "SECRET-REF-CANARY";
        const string gatewayCanary = "GATEWAY-HOST-CANARY";
        const string innerCanary = "INNER-EXCEPTION-CANARY";
        var exception = new InvalidOperationException(
            $"{profileCanary}\r\n{secretRefCanary}\r\n{gatewayCanary}",
            new Exception(innerCanary));

        AppTrace.Error((AppTraceEvent)traceEventValue, exception);

        Assert.Contains(expectedCategory, trace.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(InvalidOperationException), trace.Text);
        Assert.DoesNotContain(profileCanary, trace.Text);
        Assert.DoesNotContain(secretRefCanary, trace.Text);
        Assert.DoesNotContain(gatewayCanary, trace.Text);
        Assert.DoesNotContain(innerCanary, trace.Text);
    }

    [Fact]
    public void Request_failure_keeps_allowlisted_category_verb_and_status_but_omits_untrusted_text()
    {
        using var trace = new TraceCapture();
        const string pathCanary = "RAW-KEY-CANARY";
        const string encodedCanary = "ENCODED%2FKEY-CANARY";
        const string queryCanary = "QUERY-CANARY";
        const string fragmentCanary = "FRAGMENT-CANARY";
        const string reasonCanary = "REASON-CANARY\r\nFORGED-LINE";
        const string bodyCanary = "BODY-CANARY";

        RequestTrace.Failure(
            "F&O",
            "GET",
            $"https://GATEWAY-HOST-CANARY.example/data/Customers('{pathCanary}/{encodedCanary}')?$filter=Name eq '{queryCanary}'#{fragmentCanary}",
            new ODataResponse(503, reasonCanary, bodyCanary, 1));

        Assert.Contains("F&O request failed", trace.Text);
        Assert.Contains("status 503", trace.Text);
        Assert.Contains("verb GET", trace.Text);
        Assert.DoesNotContain(pathCanary, trace.Text);
        Assert.DoesNotContain(encodedCanary, trace.Text);
        Assert.DoesNotContain(queryCanary, trace.Text);
        Assert.DoesNotContain(fragmentCanary, trace.Text);
        Assert.DoesNotContain(reasonCanary, trace.Text);
        Assert.DoesNotContain("FORGED-LINE", trace.Text);
        Assert.DoesNotContain(bodyCanary, trace.Text);
        Assert.DoesNotContain("GATEWAY-HOST-CANARY", trace.Text);
    }

    [Fact]
    public void Unknown_request_category_and_verb_use_static_fallbacks()
    {
        using var trace = new TraceCapture();

        RequestTrace.Failure(
            "API-CANARY",
            "VERB-CANARY",
            "/PATH-CANARY",
            new ODataResponse(418, "REASON-CANARY", "BODY-CANARY", 1));

        Assert.Contains("Unknown API request failed", trace.Text);
        Assert.Contains("verb OTHER", trace.Text);
        Assert.Contains("status 418", trace.Text);
        Assert.DoesNotContain("API-CANARY", trace.Text);
        Assert.DoesNotContain("VERB-CANARY", trace.Text);
        Assert.DoesNotContain("PATH-CANARY", trace.Text);
        Assert.DoesNotContain("REASON-CANARY", trace.Text);
        Assert.DoesNotContain("BODY-CANARY", trace.Text);
    }
}
