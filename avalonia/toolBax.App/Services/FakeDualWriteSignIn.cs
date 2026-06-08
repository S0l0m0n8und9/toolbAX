using System;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / non-Windows <see cref="IDualWriteSignIn"/>: returns a seeded result (a dummy token and
/// a placeholder gateway host) without opening a browser, so the Operations/Compare flows and their
/// viewmodels are exercisable in headless tests. The real WebView2 capture is Windows-only.
/// </summary>
public sealed class FakeDualWriteSignIn : IDualWriteSignIn
{
    private readonly DualWriteSignInResult? _result;

    /// <summary>
    /// By default returns a usable fake result; pass <paramref name="result"/> (e.g. null) to simulate a
    /// cancelled/failed sign-in.
    /// </summary>
    public FakeDualWriteSignIn(DualWriteSignInResult? result = null)
    {
        _result = result ?? new DualWriteSignInResult(
            new DualWriteToken("fake-delegated-token", "fake-refresh-token", DateTimeOffset.UtcNow.AddHours(1)),
            "https://fake-gateway.dual-write.example");
    }

    public Task<DualWriteSignInResult?> SignInAsync(EnvProfile env, bool switchAccount = false, CancellationToken ct = default) =>
        Task.FromResult(_result);
}
