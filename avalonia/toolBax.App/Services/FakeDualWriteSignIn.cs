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

    /// <summary>Returns a usable seeded result (a dummy token + placeholder gateway host).</summary>
    public FakeDualWriteSignIn()
        : this(new DualWriteSignInResult(
            new DualWriteToken("fake-delegated-token", "fake-refresh-token", DateTimeOffset.UtcNow.AddHours(1)),
            "https://fake-gateway.dual-write.example"))
    {
    }

    /// <summary>
    /// Returns exactly <paramref name="result"/> — pass <c>null</c> to simulate a cancelled/failed
    /// sign-in (honoured verbatim, not coalesced to the default).
    /// </summary>
    public FakeDualWriteSignIn(DualWriteSignInResult? result) => _result = result;

    public Task<DualWriteSignInResult?> SignInAsync(EnvProfile env, bool switchAccount = false, CancellationToken ct = default) =>
        Task.FromResult(_result);
}
