using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.DualWrite.Auth;
using ToolBax.Core.Models;

namespace ToolBax.App.Services;

internal static class DualWriteNativeCapture
{
    internal static DualWriteSignInCapture Create(
        EnvProfile profile,
        IDualWriteTenantResolver? tenantResolver = null,
        Func<DateTimeOffset>? clock = null) =>
        new(profile.Tenant, tenantResolver, clock);
}

[Flags]
internal enum DualWriteBrowserDataKinds
{
    None = 0,
    DomStorage = 1,
    Cookies = 2,
    DiskCache = 4
}

internal static class DualWriteBrowserPreparation
{
    internal static DualWriteBrowserDataKinds ResetKinds(bool switchAccount) =>
        DualWriteBrowserDataKinds.DomStorage |
        DualWriteBrowserDataKinds.DiskCache |
        (switchAccount ? DualWriteBrowserDataKinds.Cookies : DualWriteBrowserDataKinds.None);

    internal static async Task PrepareAndNavigateAsync(
        Func<Task> clearOwnedProfile,
        Action navigate,
        Func<bool> isInactive)
    {
        await clearOwnedProfile();
        if (!isInactive()) navigate();
    }
}

internal enum DualWriteCloseDecision
{
    AllowPhysicalClose,
    DeferAndHide
}

/// <summary>Coordinates an inactive sign-in attempt with uninterruptible browser preparation.</summary>
internal sealed class DualWriteClosePreparationCoordinator
{
    private readonly object _sync = new();
    private readonly Action<Action> _dispatch;
    private readonly Action _physicalClose;
    private Task _preparation = Task.CompletedTask;
    private Task _deferredClose = Task.CompletedTask;
    private bool _deferredCloseStarted;
    private bool _physicalCloseAuthorized;
    private bool _closed;

    internal DualWriteClosePreparationCoordinator(Action<Action> dispatch, Action physicalClose)
    {
        _dispatch = dispatch;
        _physicalClose = physicalClose;
    }

    internal bool IsInactive { get; private set; }

    internal void Deactivate()
    {
        lock (_sync)
        {
            IsInactive = true;
        }
    }

    internal void TrackPreparation(Task preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        lock (_sync)
        {
            _preparation = preparation;
        }
    }

    internal DualWriteCloseDecision RequestClose(bool forceImmediate = false)
    {
        lock (_sync)
        {
            IsInactive = true;
            if (_closed || _physicalCloseAuthorized)
                return DualWriteCloseDecision.AllowPhysicalClose;

            if (forceImmediate)
            {
                // Application/OS shutdown cannot safely be held open. Drain still keeps the sequencer
                // gate conservative if preparation does not survive native teardown.
                _physicalCloseAuthorized = true;
                return DualWriteCloseDecision.AllowPhysicalClose;
            }

            if (_preparation.IsCompleted)
            {
                _physicalCloseAuthorized = true;
                return DualWriteCloseDecision.AllowPhysicalClose;
            }

            if (!_deferredCloseStarted)
            {
                _deferredCloseStarted = true;
                _deferredClose = FinishDeferredCloseAsync(_preparation);
            }
            return DualWriteCloseDecision.DeferAndHide;
        }
    }

    internal async Task DrainPreparationAsync()
    {
        Task drain;
        lock (_sync)
        {
            drain = _deferredCloseStarted ? _deferredClose : _preparation;
        }
        try { await drain.ConfigureAwait(false); }
        catch { }
    }

    internal void MarkClosed()
    {
        lock (_sync)
        {
            IsInactive = true;
            _closed = true;
        }
    }

    private async Task FinishDeferredCloseAsync(Task preparation)
    {
        try { await preparation.ConfigureAwait(false); }
        catch { }

        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatch(() =>
        {
            try
            {
                lock (_sync)
                {
                    if (_closed || _physicalCloseAuthorized)
                    {
                        dispatched.TrySetResult();
                        return;
                    }
                    _physicalCloseAuthorized = true;
                }
                _physicalClose();
                dispatched.TrySetResult();
            }
            catch (Exception ex)
            {
                dispatched.TrySetException(ex);
            }
        });
        await dispatched.Task.ConfigureAwait(false);
    }
}

/// <summary>Rejects native resources whose asynchronous creation finishes after host destruction.</summary>
internal sealed class DualWriteNativeHostLifetime
{
    private readonly object _sync = new();
    private bool _destroyed;

    internal bool TryPublish(Action publish)
    {
        lock (_sync)
        {
            if (_destroyed) return false;
            publish();
            return true;
        }
    }

    internal void Destroy(Action teardown)
    {
        lock (_sync)
        {
            if (_destroyed) return;
            _destroyed = true;
        }
        teardown();
    }
}

/// <summary>Bounded, platform-neutral mapping from committed browser traffic to Core observations.</summary>
internal static class DualWriteCommittedResponseReader
{
    internal const int MaximumBodyBytes = 256 * 1024;
    internal const string IncompleteDiagnostic =
        "The sign-in response did not include complete trusted request and response evidence.";

    internal static async Task<DualWriteTokenExchangeObservation?> ReadTokenExchangeAsync(
        string? requestUri,
        string? method,
        string? contentType,
        Stream? borrowedRequestContent,
        int responseStatusCode,
        Func<Task<Stream?>> openResponseContent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !DualWriteEndpointPolicy.IsTokenEndpoint(uri) ||
            !string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase) ||
            borrowedRequestContent is null || !borrowedRequestContent.CanSeek)
        {
            return null;
        }

        var requestBody = await ReadBorrowedFromStartAsync(borrowedRequestContent, cancellationToken);
        if (requestBody is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using var responseContent = await openResponseContent();
        cancellationToken.ThrowIfCancellationRequested();
        if (responseContent is null)
        {
            return null;
        }
        var responseBody = await ReadBoundedAsync(responseContent, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return responseBody is null
            ? null
            : new DualWriteTokenExchangeObservation(
                uri,
                HttpMethod.Post,
                contentType,
                requestBody,
                responseStatusCode,
                responseBody);
    }

    internal static DualWriteGatewayResponseObservation? CreateGatewayObservation(
        string? requestUri,
        string? authorization,
        int responseStatusCode)
    {
        return Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) &&
               DualWriteEndpointPolicy.IsGatewayApiRequest(uri)
            ? new DualWriteGatewayResponseObservation(uri, authorization, responseStatusCode)
            : null;
    }

    private static async Task<string?> ReadBorrowedFromStartAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            return await ReadBoundedAsync(stream, cancellationToken);
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }

    private static async Task<string?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            if (buffer.Length + read > MaximumBodyBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

internal interface IDualWriteSignInAttempt
{
    Task<DualWriteSignInResult?> RunAsync(CancellationToken cancellationToken);
    void Cancel();
    Task DrainPreparationAsync();
}

/// <summary>Serializes modal sign-in attempts and drains any started browser preparation before release.</summary>
internal sealed class DualWriteSignInSequencer
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    internal async Task<DualWriteSignInResult?> RunAsync(
        Func<IDualWriteSignInAttempt> createAttempt,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        IDualWriteSignInAttempt? attempt = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt = createAttempt();
            return await attempt.RunAsync(cancellationToken);
        }
        finally
        {
            try
            {
                if (attempt is not null) attempt.Cancel();
            }
            finally
            {
                try
                {
                    if (attempt is not null) await attempt.DrainPreparationAsync();
                }
                finally
                {
                    _gate.Release();
                }
            }
        }
    }
}
