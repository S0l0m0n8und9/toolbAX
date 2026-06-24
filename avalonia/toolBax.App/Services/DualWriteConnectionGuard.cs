using FoToolbox.Core.DualWrite;

namespace ToolBax.App.Services;

/// <summary>
/// Shared check + message for "did the gateway return a usable dual-write connection (cid)?".
/// <see cref="DualWriteGatewayClient.GetEnvironmentAsync"/> returns an <em>empty</em> cid (rather than
/// erroring) when the F&amp;O environment isn't part of a dual-write connection set the resolved gateway
/// knows about. Callers must therefore guard it explicitly — otherwise the blank cid slips through and
/// surfaces much later as a cryptic "A connection id (cid) is required." on the first gateway call.
/// </summary>
public static class DualWriteConnectionGuard
{
    /// <summary>True when the gateway returned a real connection id.</summary>
    public static bool IsLinked(DualWriteEnvironment linkage) => !string.IsNullOrWhiteSpace(linkage.Cid);

    /// <summary>Actionable message for the no-connection case, naming the F&amp;O URL and the gateway used.</summary>
    public static string NoConnectionMessage(string foUrl, string gatewayBaseUrl) =>
        $"No dual-write connection was found for '{foUrl}' on gateway '{gatewayBaseUrl}'. " +
        "Check the F&O environment URL is correct and that dual-write is set up/linked for this " +
        "environment — and that you signed in with an account in the right tenant.";
}
