using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>A lifecycle action the command bar can run (label + gateway action type + danger note).</summary>
public sealed record OpsAction(string Label, DualWriteActionType Type, bool Danger, string? Caveat);

/// <summary>
/// Dual-Write Operations screen (control-map §3). Connects to the live dual-write gateway for the active
/// environment (<see cref="IDualWriteConnector"/> over the real FoToolbox.Core gateway), lists its maps,
/// and runs lifecycle actions: select map(s) → confirm → <c>StartActionAsync</c> → poll
/// <c>GetStatusAsync</c> until terminal → refresh. The screen gates on a connection + a selection + the
/// connection still belonging to the active environment (see <c>SessionEnvMismatch</c>), and honours the
/// gateway's own per-map action eligibility (<c>DualWriteMap.Actions</c>) by excluding maps the gateway
/// says can't take the action and reporting them — see <see cref="RunAction"/>.
/// </summary>
public partial class DualWriteOpsViewModel : ObservableObject, IDisposable
{
    private readonly IDualWriteConnector _connector;
    private readonly Func<EnvProfile?> _activeEnv;
    private readonly IDialogService _dialogs;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _actionTimeout;
    // Debug mode is a finance-and-operations OData write (DualWriteProjectConfiguration.IsDebugMode),
    // separate from the gateway lifecycle actions — hence the F&O OData client + $metadata resolver.
    private readonly IODataClient _odata;
    private readonly IMetadataService _metadata;
    private DualWriteSession? _session;
    private int _generation;
    private int _operationLeaseHeld;
    private bool _disposed;
    private CancellationTokenSource? _acceptedCancellation;
    private DualWriteSession? _lifecycleSession;
    private DualWriteSession? _debugSession;
    private bool _lastWasDebug;
    public IAsyncRelayCommand RunActionCommand { get; }
    public IRelayCommand RunActionCancelCommand { get; }
    public IAsyncRelayCommand EnableDebugForSelectedCommand { get; }
    public IAsyncRelayCommand DisableDebugForSelectedCommand { get; }
    public IAsyncRelayCommand ReconcileCommand { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWriteReceipt))]
    [NotifyPropertyChangedFor(nameof(WriteReceiptText))]
    [NotifyPropertyChangedFor(nameof(ReconcileReason))]
    private LifecycleWriteReceipt? _lastLifecycleReceipt;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWriteReceipt))]
    [NotifyPropertyChangedFor(nameof(WriteReceiptText))]
    [NotifyPropertyChangedFor(nameof(ReconcileReason))]
    private DebugWriteReceipt? _lastDebugReceipt;
    [ObservableProperty] private string _readbackText = string.Empty;
    public bool HasWriteReceipt => LastLifecycleReceipt is not null || LastDebugReceipt is not null;
    public string WriteReceiptText => string.Join("\n\n", new[] { LastLifecycleReceipt?.Summary, LastDebugReceipt?.Summary }.Where(s => s is not null));
    public string ReconcileReason
    {
        get
        {
            var session = _lastWasDebug ? _debugSession : _lifecycleSession;
            if (!HasWriteReceipt) return "No captured write to inspect.";
            if (!Current(session)) return "Readback disabled: the captured session or environment changed; no profile will be switched automatically.";
            if (_lastWasDebug && LastDebugReceipt?.EntitySet is null) return "No captured project-configuration entity; inspect these projects manually in Query Builder.";
            return "";
        }
    }
    private bool Current(DualWriteSession? session) => !_disposed && session is not null && ReferenceEquals(session, _session) && session.Identity.IsCurrent(_activeEnv());
    private string PriorWarning(EnvironmentIdentity identity) =>
        (LastLifecycleReceipt is { Unconfirmed: true } lifecycle && lifecycle.Scope.Identity == identity) ||
        (LastDebugReceipt is { Unconfirmed: true } debug && debug.Scope.Identity == identity)
            ? " A prior write in this environment remains unconfirmed. Inspect its current state before a separate new attempt." : "";

    public ObservableCollection<MapRowViewModel> Maps { get; } = new();

    /// <summary>In-app gateway request/response log — surfaces the discovered gateway host, the resolved
    /// connection (cid), and each operation's outcome so a connection/cid failure is self-diagnosable.</summary>
    public ObservableCollection<GatewayLogEntry> GatewayLog { get; } = new();

    public IReadOnlyList<OpsAction> Actions { get; } = new[]
    {
        new OpsAction("Start", DualWriteActionType.Start, Danger: false, Caveat: null),
        new OpsAction("Stop", DualWriteActionType.Stop, Danger: true, Caveat: "This halts replication for the selected maps."),
        new OpsAction("Pause", DualWriteActionType.Pause, Danger: false, Caveat: null),
        new OpsAction("Resume", DualWriteActionType.Resume, Danger: false, Caveat: null),
        new OpsAction("Initial sync", DualWriteActionType.InitialSync, Danger: true, Caveat: "Initial sync re-synchronises all data and can be long-running."),
    };

    // Named actions so each command-bar button binds RunActionCommand with its own parameter. Looked up
    // by type (not index) so reordering the Actions list can't silently swap which button does what.
    public OpsAction StartAction => Action(DualWriteActionType.Start);
    public OpsAction StopAction => Action(DualWriteActionType.Stop);
    public OpsAction PauseAction => Action(DualWriteActionType.Pause);
    public OpsAction ResumeAction => Action(DualWriteActionType.Resume);
    public OpsAction InitialAction => Action(DualWriteActionType.InitialSync);

    private OpsAction Action(DualWriteActionType type) => Actions.Single(a => a.Type == type);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableDebugForSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableDebugForSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReconcileCommand))]
    private bool _isBusy;

    /// <summary>True from the start of mutation confirmation/auth through submit, polling, and refresh.</summary>
    [ObservableProperty]
    private bool _mutationInProgress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyCanExecuteChangedFor(nameof(RunActionCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableDebugForSelectedCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableDebugForSelectedCommand))]
    private string? _connectionName;

    [ObservableProperty]
    private string _status = "Not connected.";

    [ObservableProperty]
    private string? _loadError;

    /// <summary>Outcome of the last debug-mode toggle (empty until one is attempted).</summary>
    [ObservableProperty]
    private string _debugStatus = string.Empty;

    public bool IsConnected => ConnectionName is not null;

    public bool HasMaps => Maps.Count > 0;

    public int SelectedCount => Maps.Count(m => m.IsSelected);

    public DualWriteOpsViewModel(
        IDualWriteConnector connector,
        Func<EnvProfile?> activeEnv,
        IDialogService dialogs,
        TimeSpan? pollInterval = null,
        TimeSpan? actionTimeout = null,
        IODataClient? odata = null,
        IMetadataService? metadata = null)
    {
        RunActionCommand = new WriteOperationCommand((arg, ct) => arg is OpsAction action ? RunActionExclusive(action, ct) : Task.CompletedTask, arg => CanRunAction(arg as OpsAction),
            () => TryBeginOperation(true), () => EndOperation(true), c => _acceptedCancellation = c);
        EnableDebugForSelectedCommand = new WriteOperationCommand((_, ct) => SetDebugForSelectedExclusiveAsync(true, ct), _ => CanToggleDebug(),
            () => TryBeginOperation(true), () => EndOperation(true), c => _acceptedCancellation = c);
        DisableDebugForSelectedCommand = new WriteOperationCommand((_, ct) => SetDebugForSelectedExclusiveAsync(false, ct), _ => CanToggleDebug(),
            () => TryBeginOperation(true), () => EndOperation(true), c => _acceptedCancellation = c);
        ReconcileCommand = new WriteOperationCommand((_, ct) => Reconcile(ct), _ => !IsBusy && HasWriteReceipt && ReconcileReason.Length == 0,
            () => TryBeginOperation(false), () => EndOperation(false), c => _acceptedCancellation = c);
        RunActionCancelCommand = new RelayCommand(CancelAcceptedOperation);
        _connector = connector;
        _activeEnv = activeEnv;
        _dialogs = dialogs;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(600);
        // Safety net: never poll a stuck request forever (a hung gateway worker would otherwise lock the UI).
        _actionTimeout = actionTimeout ?? TimeSpan.FromSeconds(120);
        _odata = odata ?? new FakeODataClient();
        _metadata = metadata ?? new FakeMetadataService();
        Maps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMaps));
        GatewayLog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGatewayLog));
    }

    public bool HasGatewayLog => GatewayLog.Count > 0;

    /// <summary>
    /// Appends one line to the in-app gateway log. Only ever called from VM operations that resume on the
    /// UI thread (each awaits without ConfigureAwait(false)), so the ObservableCollection is touched on it.
    /// <paramref name="traceText"/> replaces <paramref name="text"/> in the session log for the lines whose
    /// on-screen wording must not be persisted verbatim — see <see cref="Traceable"/>.
    /// </summary>
    private void Log(string text, LogKind kind = LogKind.Info, string? note = null, string? traceText = null)
    {
        GatewayLog.Add(new GatewayLogEntry(text, note, kind));

        // Warn/Err lines also go to Trace so the session log (#168) keeps them: the in-app log dies with
        // the window, and a dual-write connection failure is exactly the thing a user reports after the
        // fact. Info/Ok is live-diagnosis chatter and stays on screen only. What reaches the file carries
        // gateway hosts, map names, connection ids, request ids and status text — never a token, and never
        // a response body (which is what traceText exists to keep out).
        switch (kind)
        {
            case LogKind.Warn:
                Trace.TraceWarning($"Dual-write operations: {traceText ?? text}");
                break;
            case LogKind.Err:
                Trace.TraceError($"Dual-write operations: {traceText ?? text}");
                break;
        }
    }

    /// <summary>
    /// What a failed operation is allowed to say in the session log. Two Core exceptions quote the gateway's
    /// <b>raw response body</b> in their message — <see cref="DualWriteGatewayException"/> up to 500
    /// characters of it on a non-success status, and <see cref="DualWriteGatewayResponseException"/> the
    /// first line of a non-JSON body (an HTML sign-in or proxy page). Both are fine in a banner, where the
    /// user is reading about their own gateway; neither may be written to disk (#168). The persisted form
    /// keeps the diagnosis — a status code, or the fact that the answer wasn't JSON — and drops everything
    /// the gateway echoed back.
    /// <para>
    /// Matched on the exception <i>type</i> and never on the message text, so rewording Core cannot silently
    /// un-redact this. <b>A new Core exception that quotes a response body needs a case here</b>; anything
    /// unmatched falls through to <see cref="Concise"/>, which trusts the message.
    /// </para>
    /// </summary>
    private static string Traceable(Exception ex) => ex switch
    {
        DualWriteGatewayException gateway =>
            $"the gateway returned {(int)gateway.StatusCode} {gateway.StatusCode} (response body not logged)",
        DualWriteGatewayResponseException =>
            "the gateway returned a non-JSON response, e.g. an HTML sign-in or proxy page (response body not logged)",
        _ => Concise(ex),
    };

    private bool CanLoad() => !_disposed && !IsBusy && Volatile.Read(ref _operationLeaseHeld) == 0;

    /// <summary>Connects to the gateway for the active environment and loads its maps.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanLoad))]
    private async Task Load(CancellationToken ct)
    {
        if (!TryBeginOperation(isMutation: false)) return;
        try
        {
            await LoadExclusive(ct);
        }
        finally
        {
            EndOperation(isMutation: false);
        }
    }

    private async Task LoadExclusive(CancellationToken ct)
    {
        if (_disposed) return;
        var env = _activeEnv();
        if (env is null)
        {
            LoadError = "Select an environment first.";
            Status = "No active environment.";
            return;
        }

        var identity = EnvironmentIdentity.Create(env);
        var generation = Volatile.Read(ref _generation);

        LoadError = null;
        Status = "Connecting…";
        // Scope the log to this attempt: a retry after a failure starts clean rather than interleaving
        // entries from earlier connects, which is the whole point of the log (isolate the current attempt).
        GatewayLog.Clear();
        Log($"Connecting to {env.Name} ({env.Url})…");
        try
        {
            var session = await _connector.ConnectAsync(env, ct);
            if (ct.IsCancellationRequested)
            {
                DisposeGateway(session);
                ct.ThrowIfCancellationRequested();
            }
            if (!CanCommitLoad(identity, generation))
            {
                DisposeGateway(session);
                return;
            }

            DisposeSession();
            _session = session;
            ConnectionName = session.Cname;
            if (!string.IsNullOrWhiteSpace(session.GatewayBaseUrl))
            {
                Log($"Gateway host: {session.GatewayBaseUrl}");
            }
            Log($"Connected: {session.Cname} (cid {session.Cid}).", LogKind.Ok);

            var maps = await session.Gateway.GetMapsAsync(session.Cid, ct);
            if (ct.IsCancellationRequested)
            {
                if (ReferenceEquals(_session, session))
                {
                    DisposeSession();
                }
                ct.ThrowIfCancellationRequested();
            }
            if (!CanCommitLoad(identity, generation) || !ReferenceEquals(_session, session))
            {
                if (ReferenceEquals(_session, session))
                {
                    DisposeSession();
                }
                return;
            }

            PopulateMaps(maps, keepSelectedIds: null);
            Status = Maps.Count == 0 ? "No maps on this connection." : $"{Maps.Count} map(s).";
            Log($"Loaded {Maps.Count} map(s).", Maps.Count == 0 ? LogKind.Warn : LogKind.Ok);
        }
        // An HTTP/socket timeout also arrives as an OperationCanceledException, but with OUR token still
        // live — so only a cancelled token means the user asked for this. Conflating them told people they
        // had cancelled a connect that in fact silently timed out, and showed no error banner to correct it.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (_disposed || generation != Volatile.Read(ref _generation)) return;
            const string timedOut = "Gateway did not respond (timed out).";
            LoadError = timedOut;
            ResetDisconnected(timedOut);
            Log(timedOut, LogKind.Err);
        }
        catch (OperationCanceledException)
        {
            if (_disposed || generation != Volatile.Read(ref _generation)) return;
            ResetDisconnected("Cancelled.");
            Log("Cancelled.", LogKind.Warn);
        }
        catch (Exception ex)
        {
            if (_disposed || generation != Volatile.Read(ref _generation)) return;
            LoadError = ex.Message;
            ResetDisconnected("Connection failed.");
            Log(ex.Message, LogKind.Err, traceText: Traceable(ex));
        }
    }

    /// <summary>Message shown (and logged) when the session and the active environment have diverged.</summary>
    private const string ReconnectRequired = "Connected to a different environment — reconnect required.";

    // True when the live session's captured connection identity differs from the current profile. Shell
    // switches normally dispose this VM, while this guard also covers same-id profile edits and any direct
    // caller retaining the VM — especially debug mode, whose project ids and OData client must never straddle
    // two identities.
    private bool SessionEnvMismatch() =>
        _session is not null && !_session.Identity.IsCurrent(_activeEnv());

    private bool CanCommitLoad(EnvironmentIdentity identity, int generation) =>
        !_disposed && generation == Volatile.Read(ref _generation) && identity.IsCurrent(_activeEnv());

    // Hard guard for every use site. Nothing re-raises CanExecuteChanged on an environment switch, so the
    // CanExecute gating below is a UI courtesy only — this is what actually stops the call. Returns true
    // (and reports it) when the operation must not proceed; leaves all other state untouched. Safe to call
    // repeatedly inside one operation: multi-request operations re-check it before every request, since an
    // entry-only check expires at the first await.
    private bool BlockedByEnvMismatch()
    {
        if (!SessionEnvMismatch())
        {
            return false;
        }

        Status = ReconnectRequired;
        Log($"Environment changed: this session is connected to {ConnectionName}, but the active environment " +
            $"is now {_activeEnv()?.Name ?? "none"}. Reconnect before running operations.", LogKind.Warn);
        return true;
    }

    // Deliberately NOT gated on per-map action eligibility. The grid selection is multi-select and mixed
    // states are the norm, so a button disabled because ONE selected map can't take the action would lie
    // about the rest — and a button enabled for a mixed selection can't say which maps it will act on
    // either. The honest surface is the outcome: RunAction excludes the maps the gateway says are
    // ineligible, the confirm dialog lists only the ones actually being sent, and the skipped ones are
    // named in the status line and the gateway log.
    private bool CanRunAction(OpsAction? action) =>
        action is not null && !IsBusy && Volatile.Read(ref _operationLeaseHeld) == 0
        && _session is not null && !SessionEnvMismatch() && SelectedCount > 0;

    private async Task RunActionExclusive(OpsAction action, CancellationToken ct)
    {
        var session = _session;
        if (session is null)
        {
            return;
        }

        // Before the confirm dialog and before any network call: never act on a stale environment.
        if (BlockedByEnvMismatch())
        {
            return;
        }

        var selected = Maps.Where(m => m.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        // The gateway tells us, per map, which lifecycle actions its current state accepts
        // (DualWriteMap.Actions, e.g. ["4","5"] = Stop/Pause for a Running map). Honour it here because an
        // ineligible map doesn't fail alone: MapActionPayloadBuilder batches the whole selection into a
        // single details[] array, so ONE map the gateway rejects fails the action for every map selected
        // with it. A map whose action list the gateway didn't report (Supports == null — older gateways
        // omit the field) is sent: refusing on absent data would lock the user out of the screen entirely.
        var targets = selected.Where(t => t.Map.Supports(action.Type) != false).ToList();
        var skipped = selected.Where(t => t.Map.Supports(action.Type) == false).ToList();
        var skipNote = skipped.Count == 0
            ? null
            : $"Skipped {skipped.Count} map(s) that don't support {action.Label} in their current state: " +
              $"{string.Join(", ", skipped.Select(t => t.Name))}.";

        if (targets.Count == 0)
        {
            // Nothing the gateway would accept — refuse before the confirm dialog rather than send a batch
            // that can only come back as an opaque 500.
            Status = skipNote!;
            Log(skipNote!, LogKind.Warn);
            return;
        }

        if (skipNote is not null)
        {
            // Logged before the dialog so the shortened target list below is explained, not just shorter.
            Log(skipNote, LogKind.Warn);
        }

        var maps = targets.Select(t => t.Map).ToArray();
        var receipt = new LifecycleWriteReceipt(WriteScope.Capture(session.Profile), session.Cid, action.Label,
            DateTimeOffset.UtcNow, maps.Select(m => new CapturedWriteTarget(m.Id, m.Name, m.ProjectId)).ToImmutableArray(), new WriteObservation(false));
        var invoked = false;
        try
        {
            var request = new ConfirmRequest($"{action.Label} {targets.Count} map(s)?",
                $"Sends {action.Label} to the dual-write gateway for {receipt.Scope.Display}." + PriorWarning(session.Identity),
                targets.Select(t => $"{t.Name} · {t.CeEntity} · {t.State}").ToList(), action.Label, action.Danger, action.Caveat);
            if (!await _dialogs.ConfirmAsync(request, ct) || ct.IsCancellationRequested)
            { if (!_disposed) Status = "Not sent — confirmation cancelled."; return; }
            if (!Current(session)) { if (!_disposed) Status = "Not sent — environment changed; reconnect required."; return; }
            _lifecycleSession = session;
            _lastWasDebug = false;
            ReadbackText = "";
            LastLifecycleReceipt = receipt = receipt with { Observation = new WriteObservation(null) };
            Status = $"{action.Label}…";
            invoked = true;
            var response = await session.Gateway.StartActionAsync(action.Type, maps, session.Cid, ct);
            if (_disposed || !ReferenceEquals(session, _session) || LastLifecycleReceipt?.AttemptId != receipt.AttemptId) return;
            // A returned action DTO is an acknowledgment even for legacy implementations without HTTP metadata.
            var observation = response.Acknowledgment is null ? new WriteObservation(true, GatewayAcknowledged: true) : WriteObservation.From(response.Acknowledgment);
            LastLifecycleReceipt = receipt = receipt with { Observation = observation, RequestId = response.RequestId };
            if (!Current(session)) { Status = "Context changed — captured submission evidence retained; reconnect to the original context before readback."; return; }
            if (string.IsNullOrWhiteSpace(response.RequestId))
            {
                Status = $"{action.Label} submitted — the gateway did not return a request id; refresh to see the result.";
                Log(Status, LogKind.Warn, traceText: "Mutation acknowledged without request identifier.");
            }
            else await PollAndReportAsync(action, session, response.RequestId, ct);
            if (!_disposed && !Current(session)) Status = "Context changed — captured submission evidence retained; return to the original context before readback.";
            if (ct.IsCancellationRequested || !Current(session)) return;
            try
            {
                var keep = selected.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var refreshed = await session.Gateway.GetMapsAsync(session.Cid, ct);
                if (!ct.IsCancellationRequested && Current(session)) PopulateMaps(refreshed, keep);
            }
            catch (Exception)
            {
                if (Current(session)) { Status += " (map list could not refresh)"; Log("Map list could not refresh.", LogKind.Warn); }
            }
        }
        catch (Exception ex)
        {
            if (_disposed || !ReferenceEquals(session, _session) || (invoked && LastLifecycleReceipt?.AttemptId != receipt.AttemptId)) return;
            if (invoked && string.IsNullOrWhiteSpace(LastLifecycleReceipt?.RequestId))
            {
                var evidence = (ex as IDualWriteMutationFailure)?.Evidence;
                LastLifecycleReceipt = receipt with
                {
                    Observation = WriteObservation.From(evidence),
                    Diagnostic = CompleteGatewayDiagnostic(ex, evidence)
                };
            }
            if (!Current(session)) { Status = "Context changed — captured submission evidence retained; reconnect to the original context before readback."; return; }
            if (!invoked) { Status = "Not sent — confirmation stopped or failed."; return; }
            if (LastLifecycleReceipt?.RequestId is { Length: > 0 } id)
                Status = $"{action.Label} submitted (request {id}) — stopped waiting; cancellation/error does not undo submission.";
            else
            {
                var evidence = (ex as IDualWriteMutationFailure)?.Evidence;
                LastLifecycleReceipt = receipt with
                {
                    Observation = WriteObservation.From(evidence),
                    Diagnostic = CompleteGatewayDiagnostic(ex, evidence)
                };
                Status = LastLifecycleReceipt.Observation.Summary +
                    (LastLifecycleReceipt.Diagnostic is null ? "" : $" Gateway detail: {LastLifecycleReceipt.Diagnostic}");
            }
            Log(Status, LogKind.Warn, traceText: "Mutation observation stopped; inspect retained evidence.");
        }
        finally { if (!_disposed && skipNote is not null) Status = $"{Status} {skipNote}"; }
    }
    private bool TryBeginOperation(bool isMutation)
    {
        if (_disposed || Interlocked.CompareExchange(ref _operationLeaseHeld, 1, 0) != 0)
        {
            return false;
        }

        if (_disposed || IsBusy)
        {
            Volatile.Write(ref _operationLeaseHeld, 0);
            return false;
        }

        if (isMutation) MutationInProgress = true;
        IsBusy = true;
        return true;
    }

    private void CancelAcceptedOperation()
    {
        // The shared visible control belongs to the one accepted lease owner. Load uses the Toolkit
        // command's cancellation source; writes and readback use WriteOperationCommand's private source.
        if (LoadCommand.IsRunning)
        {
            LoadCommand.Cancel();
            return;
        }

        _acceptedCancellation?.Cancel();
    }

    private void EndOperation(bool isMutation)
    {
        if (isMutation) MutationInProgress = false;
        Volatile.Write(ref _operationLeaseHeld, 0);
        // IsBusy drives the generated CanExecuteChanged notifications. Release first so handlers that
        // immediately re-query CanExecute observe the available lease rather than leaving buttons stale.
        IsBusy = false;
        ReconcileCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ReconcileReason));
    }

    // Polls a submitted request to a terminal state and reports the outcome. Neither our own polling budget
    // running out nor the status check itself erroring is a failed action or a lost request: both are
    // reported as submitted, and the caller still refreshes the map list. Only a genuine user cancel, and a
    // failure of the submit itself (which happens before this is called), propagate as such.
    private async Task PollAndReportAsync(OpsAction action, DualWriteSession session, string requestId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_actionTimeout);
        var pollToken = timeoutCts.Token;

        DualWriteRequestStatus? final = null;
        try
        {
            using var timer = new PeriodicTimer(_pollInterval);
            while (await timer.WaitForNextTickAsync(pollToken))
            {
                if (!Current(session)) return;
                var status = await session.Gateway.GetStatusAsync(requestId, pollToken);
                if (pollToken.IsCancellationRequested || !Current(session)) { pollToken.ThrowIfCancellationRequested(); return; }
                if (status.IsTerminal)
                {
                    final = status;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            if (!Current(session)) return;
            // The timeout tripped, not the user. A genuine cancel keeps propagating to RunAction.
            Status = $"{action.Label} submitted — still running after {_actionTimeout.TotalSeconds:0.##}s " +
                     $"(request {requestId}). The map list will refresh; check states again shortly.";
            Log(Status, LogKind.Warn);
            return;
        }
        // The status check broke (gateway 500, network blip, a non-JSON body), not the action: the submit
        // already succeeded. Letting this reach RunAction's outer catch reported "{action} failed" and
        // skipped the refresh — a failure message over a stale grid, which is how the same Initial sync gets
        // submitted twice. Warn, not Err: the action is most likely still running. A cancel is excluded from
        // the filter so it keeps propagating to RunAction.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!Current(session)) return;
            var submitted = $"{action.Label} submitted — status check failed";
            Status = $"{submitted} ({Concise(ex)}); refreshing the map list.";
            Log(Status, LogKind.Warn, traceText: $"{submitted} ({Traceable(ex)}); refreshing the map list.");
            return;
        }

        if (!Current(session)) return;
        if (final is not null && LastLifecycleReceipt is { } receipt)
            LastLifecycleReceipt = receipt with { TerminalState = final.State, TerminalSuccess = final.IsSuccess };
        if (final is { IsSuccess: true })
        {
            Status = $"{action.Label} completed.";
            Log($"{action.Label} completed.", LogKind.Ok);
        }
        else
        {
            var why = final?.Message ?? final?.State ?? "unknown";
            Status = $"{action.Label} failed: {why}.";
            Log($"{action.Label} failed: {why}.", LogKind.Err);
        }
    }

    // One line, no trailing stop — a gateway that answers with an HTML error page or a truncated body
    // surfaces as a multi-line parse message, and this is spliced into a single-line status sentence.
    private static string Concise(Exception ex)
    {
        var first = ex.Message.Split('\n', 2)[0].Trim().TrimEnd('.');
        return first.Length == 0 ? ex.GetType().Name : first;
    }

    private static string? CompleteGatewayDiagnostic(Exception exception, DualWriteMutationEvidence? evidence) =>
        exception is DualWriteGatewayException gateway &&
        evidence is { StatusCode: not null, BodyComplete: true }
            ? WriteUiEvidence.FromGateway(gateway)
            : null;

    private bool CanToggleDebug() =>
        !IsBusy && Volatile.Read(ref _operationLeaseHeld) == 0
        && _session is not null && !SessionEnvMismatch() && SelectedCount > 0;

    private async Task SetDebugForSelectedExclusiveAsync(bool enabled, CancellationToken ct)
    {
        var session = _session;
        if (session is null)
        {
            DebugStatus = "Connect to the gateway first.";
            return;
        }

        // The project ids come from the old session's maps while the PATCH would go through _odata, which
        // resolves the ACTIVE environment per call — so a mismatch here would write env B using env A's
        // ids. Refuse before reading metadata or issuing a request. Entry is not enough, though: the
        // environment can change across any of the awaits below, so this is re-checked before every request
        // (see StopIfEnvChanged).
        if (BlockedByEnvMismatch())
        {
            DebugStatus = ReconnectRequired;
            return;
        }

        var targets = Maps.Where(m => m.IsSelected).Select(m => m.Map).ToList();
        if (targets.Count == 0)
        {
            DebugStatus = "Select a map first.";
            return;
        }

        var env = _activeEnv();
        if (env is null || string.IsNullOrWhiteSpace(env.Url))
        {
            DebugStatus = "No finance & operations URL is configured for this environment.";
            return;
        }

        var projectIds = targets
            .Select(m => m.ProjectId)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (projectIds.Count == 0)
        {
            DebugStatus = "The selected map(s) have no project id, so debug mode can't be targeted.";
            return;
        }

        var identity = EnvironmentIdentity.Create(env);
        if (session.Identity != identity)
        {
            DebugStatus = ReconnectRequired;
            return;
        }

        var skipped = targets.Count - targets.Count(m => !string.IsNullOrWhiteSpace(m.ProjectId));
        var actionVerb = enabled ? "Enable" : "Disable";
        var request = new ConfirmRequest(
            Title: $"{actionVerb} debug mode for {projectIds.Count} project(s)?",
            Message: $"This changes project-level debug flags and dual-write logging for {env.Name} ({env.Url})." + PriorWarning(identity),
            Targets: targets.Where(m => !string.IsNullOrWhiteSpace(m.ProjectId))
                .Select(m => $"{m.Name} · {m.ProjectId}").ToList(),
            ConfirmLabel: actionVerb + " debug mode",
            IsDanger: true,
            Caveat: skipped == 0 ? null : $"{skipped} selected map(s) without a project id will be skipped.");
        if (ct.IsCancellationRequested)
        {
            DebugStatus = "No debug changes sent.";
            return;
        }

        bool confirmed;
        try
        {
            confirmed = await _dialogs.ConfirmAsync(request, ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            if (!_disposed) DebugStatus = "No debug changes sent.";
            return;
        }
        catch (Exception ex)
        {
            if (!_disposed) DebugStatus = $"Debug-mode toggle not sent: {ex.Message}";
            return;
        }

        if (_disposed) return;
        if (!confirmed || ct.IsCancellationRequested)
        {
            DebugStatus = "No debug changes sent.";
            return;
        }

        if (!ReferenceEquals(session, _session) || !identity.IsCurrent(_activeEnv()))
        {
            DebugStatus = "Debug-mode toggle not sent — the active environment changed; reconnect required.";
            return;
        }

        _debugSession = session;
        _lastWasDebug = true;
        ReadbackText = "";
        var attempt = new DebugWriteReceipt(WriteScope.Capture(env), session.Cid, DateTimeOffset.UtcNow,
            projectIds.Select(pid => new DebugProjectAttempt(pid, enabled)).ToImmutableArray());
        LastDebugReceipt = attempt;
        DebugStatus = enabled ? "Enabling debug mode…" : "Disabling debug mode…";
        var activeIndex = -1;
        var patchInvoked = false;
        var acknowledged = 0;
        bool Stop()
        {
            if (ct.IsCancellationRequested || !Current(session))
            {
                if (!_disposed) DebugStatus = ct.IsCancellationRequested
                    ? "Stopped waiting; inspect per-project evidence. Cancellation does not undo writes."
                    : $"Stopped after {acknowledged} of {projectIds.Count} — environment changed; reconnect required.";
                if (!_disposed && !Current(session))
                {
                    Status = ReconnectRequired;
                    Log($"Environment changed: {session.Profile.Name} to {_activeEnv()?.Name ?? "none"}; reconnect before further operations.",
                        LogKind.Warn, traceText: "Environment changed; reconnect before further operations.");
                }
                return true;
            }
            return false;
        }
        void Leg(int index, WriteObservation observation, bool pending = false,
            string? stage = null, string? diagnostic = null)
        {
            if (_disposed || !ReferenceEquals(session, _session) || LastDebugReceipt is not { } r || r.AttemptId != attempt.AttemptId) return;
            LastDebugReceipt = r with { Projects = r.Projects.SetItem(index, r.Projects[index] with
            {
                Observation = observation,
                Stage = pending ? "Pending" : stage ?? "Observed",
                Diagnostic = diagnostic
            }) };
        }
        try
        {
            if (Stop()) return;
            if (_metadata.GetEntities().Count == 0) await _metadata.LoadEntitiesAsync(ct);
            if (Stop()) return;
            var set = _metadata.GetEntities().Select(e => e.Name)
                .FirstOrDefault(n => n.Contains(DualWriteDebugMode.EntityLogicalName, StringComparison.OrdinalIgnoreCase));
            if (set is null)
            { DebugStatus = $"This environment's OData metadata exposes no '{DualWriteDebugMode.EntityLogicalName}' entity, so debug mode can't be toggled from here."; return; }
            LastDebugReceipt = LastDebugReceipt with { EntitySet = set };
            var getHeaders = new Dictionary<string, string> { ["Accept"] = "application/json;odata.metadata=full" };
            var patchHeaders = new Dictionary<string, string> { ["If-Match"] = "*" };
            for (var i = 0; i < projectIds.Count; i++)
            {
                activeIndex = i;
                patchInvoked = false;
                if (Stop()) return;
                var get = await _odata.SendAsync("GET", DebugReadPath(set, projectIds[i]), null, getHeaders, ct);
                if (ct.IsCancellationRequested)
                {
                    Leg(i, new WriteObservation(false), stage: "Read cancelled",
                        diagnostic: "Read cancelled before any write was sent.");
                    Stop();
                    return;
                }
                if (Stop()) return;
                if (!get.IsSuccess)
                {
                    Leg(i, new WriteObservation(false), stage: "Read failed",
                        diagnostic: WriteUiEvidence.FromReadResponse(get));
                    continue;
                }
                var record = DualWriteDebugMode.ReadFirstRecord(get.Body);
                if (record is null)
                {
                    Leg(i, new WriteObservation(false), stage: "Record missing",
                        diagnostic: "Read succeeded but no usable project configuration record was returned.");
                    continue;
                }
                if (Stop()) return;
                Leg(i, new WriteObservation(null), pending: true);
                patchInvoked = true;
                var patch = await _odata.SendAsync("PATCH", record.ODataId, DualWriteDebugMode.BuildPatchBody(enabled), patchHeaders, ct);
                Leg(i, WriteObservation.From(patch));
                if (patch.IsSuccess) acknowledged++;
                if (!Current(session)) { Stop(); return; }
                if (ct.IsCancellationRequested || WriteObservation.From(patch).Unconfirmed) break;
            }
            DebugStatus = $"Debug mode {(enabled ? "enabled" : "disabled")} request: {acknowledged} HTTP acknowledgment(s). Inspect per-project evidence; unconfirmed writes must not be replayed automatically.";
        }
        catch (Exception ex)
        {
            if (_disposed || !ReferenceEquals(session, _session)) return;
            if (activeIndex >= 0)
            {
                var observation = !patchInvoked ? new WriteObservation(false) : ex is ODataWriteCanceledException cancelled
                    ? cancelled.ObservedResponse is { } observed ? WriteObservation.From(observed) : new WriteObservation(cancelled.DispatchStarted)
                    : new WriteObservation(null);
                var callerCancelled = ex is OperationCanceledException && ct.IsCancellationRequested;
                var diagnostic = patchInvoked ? null : callerCancelled
                    ? "Read cancelled before any write was sent."
                    : ex is OperationCanceledException
                        ? "Read timed out or was interrupted before any write was sent."
                        : $"Read failed before any write was sent: {WriteUiEvidence.FromException(ex)}";
                Leg(activeIndex, observation,
                    stage: patchInvoked ? "Observed" : callerCancelled ? "Read cancelled" : "Read failed",
                    diagnostic: diagnostic);
            }
            if (!Current(session)) { Stop(); return; }
            DebugStatus = patchInvoked ? "Debug write outcome unknown or incompletely observed — stopped waiting. Inspect per-project evidence; remaining projects were not attempted."
                : "Debug reads stopped before the next write; inspect per-project evidence.";
        }
    }

    private static string DebugReadPath(string set, string pid) =>
        $"data/{set}?$filter=ProjectId eq '{pid.Replace("'", "''")}'";

    private async Task Reconcile(CancellationToken ct)
    {
        var session = _lastWasDebug ? _debugSession : _lifecycleSession;
        if (!Current(session) || ReconcileReason.Length != 0) return;
        var debug = _lastWasDebug ? LastDebugReceipt : null;
        var lifecycle = _lastWasDebug ? null : LastLifecycleReceipt;
        ReadbackText = "Reading current state…";
        try
        {
            if (debug is not null)
            {
                var observations = new List<string>();
                var headers = new Dictionary<string, string> { ["Accept"] = "application/json;odata.metadata=full" };
                foreach (var project in debug.Projects)
                {
                    if (ct.IsCancellationRequested || !Current(session)) return;
                    var response = await _odata.SendAsync("GET", DebugReadPath(debug.EntitySet!, project.ProjectId), null, headers, ct);
                    if (ct.IsCancellationRequested || !Current(session)) return;
                    var record = response.IsSuccess ? DualWriteDebugMode.ReadFirstRecord(response.Body) : null;
                    var flag = record?.IsDebugMode is { } value ? value ? "enabled" : "disabled" : "unknown";
                    observations.Add($"{project.ProjectId}: current debug flag {flag}; desired {(project.DesiredValue ? "enabled" : "disabled")}. Original: {project.Observation?.Summary ?? project.Stage}");
                }
                ReadbackText = "Current state only — does not prove which request caused it. Original evidence is unchanged.\n" + string.Join("\n", observations);
            }
            else if (lifecycle is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(lifecycle.RequestId))
                {
                    var status = await session!.Gateway.GetStatusAsync(lifecycle.RequestId, ct);
                    if (ct.IsCancellationRequested || !Current(session)) return;
                    ReadbackText = $"Current request {lifecycle.RequestId}: {status.State}; terminal: {status.IsTerminal}. Original submission evidence is unchanged.";
                }
                else
                {
                    var maps = await session!.Gateway.GetMapsAsync(lifecycle.Cid, ct);
                    if (ct.IsCancellationRequested || !Current(session)) return;
                    ReadbackText = "Current map states only — not proof of which request caused them. Original evidence is unchanged.\n" +
                        string.Join("\n", lifecycle.Targets.Select(t => $"{t.Name}: {maps.FirstOrDefault(m => m.Id == t.Id)?.State ?? "not returned"}"));
                }
            }
        }
        catch (Exception)
        {
            if (Current(session)) ReadbackText = "Current-state read stopped or failed. Original write evidence is unchanged.";
        }
        finally
        {
            if (ct.IsCancellationRequested && Current(session))
                ReadbackText = "Current-state read cancelled. Original write evidence is unchanged.";
            else if (!_disposed && !Current(session))
                ReadbackText = "Readback discarded: the captured session or environment changed. Original write evidence is unchanged.";
        }
    }
    // Rebuilds the rows, re-subscribing to selection changes and restoring prior selection by id.
    private void PopulateMaps(IReadOnlyList<DualWriteMap> maps, ISet<string>? keepSelectedIds)
    {
        foreach (var old in Maps)
        {
            old.PropertyChanged -= OnRowChanged;
        }

        Maps.Clear();
        foreach (var map in maps)
        {
            var row = MapRowViewModel.From(map);
            if (keepSelectedIds is not null && keepSelectedIds.Contains(row.Id))
            {
                row.IsSelected = true;
            }

            row.PropertyChanged += OnRowChanged;
            Maps.Add(row);
        }

        OnPropertyChanged(nameof(SelectedCount));
        NotifySelectionCommands();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MapRowViewModel.IsSelected))
        {
            OnPropertyChanged(nameof(SelectedCount));
            NotifySelectionCommands();
        }
    }

    private void NotifySelectionCommands()
    {
        RunActionCommand.NotifyCanExecuteChanged();
        EnableDebugForSelectedCommand.NotifyCanExecuteChanged();
        DisableDebugForSelectedCommand.NotifyCanExecuteChanged();
    }

    private void ResetDisconnected(string status)
    {
        Status = status;
        ConnectionName = null;
        PopulateMaps(Array.Empty<DualWriteMap>(), keepSelectedIds: null);
        DisposeSession();
    }

    private void DisposeSession()
    {
        if (_session?.Gateway is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _session = null;
    }

    private static void DisposeGateway(DualWriteSession session)
    {
        (session.Gateway as IDisposable)?.Dispose();
    }

    // The shell discards this VM (without finalization) when the active environment changes; dispose the
    // live gateway session so its owned HttpClient / connection pool isn't leaked for the app's lifetime.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _generation);
        _acceptedCancellation?.Cancel();
        LoadCommand.Cancel();
        DisposeSession();
    }
}
