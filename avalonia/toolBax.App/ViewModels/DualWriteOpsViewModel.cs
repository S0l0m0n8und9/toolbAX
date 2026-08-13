using System;
using System.Collections.Generic;
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
    private bool _isBusy;

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
    /// What a failed operation is allowed to say in the session log. A <see cref="DualWriteGatewayException"/>
    /// message embeds up to 500 characters of the gateway's <b>raw response body</b> — which
    /// <c>DualWriteGatewayClient</c> trims but does not redact. That is fine in a banner, where the user is
    /// reading about their own gateway, but a response body must never be written to disk (#168), so the
    /// persisted form keeps the status code and drops everything the gateway echoed back.
    /// <para>
    /// Matched on the exception <i>type</i> and not on the message text, so rewording Core's message cannot
    /// silently un-redact this. Every other exception reaching these handlers carries one of our own messages.
    /// </para>
    /// </summary>
    private static string Traceable(Exception ex) => ex is DualWriteGatewayException gateway
        ? $"the gateway returned {(int)gateway.StatusCode} {gateway.StatusCode} (response body not logged)"
        : Concise(ex);

    private bool CanLoad() => !IsBusy;

    /// <summary>Connects to the gateway for the active environment and loads its maps.</summary>
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanLoad))]
    private async Task Load(CancellationToken ct)
    {
        var env = _activeEnv();
        if (env is null)
        {
            LoadError = "Select an environment first.";
            Status = "No active environment.";
            return;
        }

        IsBusy = true;
        LoadError = null;
        Status = "Connecting…";
        // Scope the log to this attempt: a retry after a failure starts clean rather than interleaving
        // entries from earlier connects, which is the whole point of the log (isolate the current attempt).
        GatewayLog.Clear();
        Log($"Connecting to {env.Name} ({env.Url})…");
        try
        {
            var session = await _connector.ConnectAsync(env, ct);
            DisposeSession();
            _session = session;
            ConnectionName = session.Cname;
            if (!string.IsNullOrWhiteSpace(session.GatewayBaseUrl))
            {
                Log($"Gateway host: {session.GatewayBaseUrl}");
            }
            Log($"Connected: {session.Cname} (cid {session.Cid}).", LogKind.Ok);

            var maps = await session.Gateway.GetMapsAsync(session.Cid, ct);
            PopulateMaps(maps, keepSelectedIds: null);
            Status = Maps.Count == 0 ? "No maps on this connection." : $"{Maps.Count} map(s).";
            Log($"Loaded {Maps.Count} map(s).", Maps.Count == 0 ? LogKind.Warn : LogKind.Ok);
        }
        // An HTTP/socket timeout also arrives as an OperationCanceledException, but with OUR token still
        // live — so only a cancelled token means the user asked for this. Conflating them told people they
        // had cancelled a connect that in fact silently timed out, and showed no error banner to correct it.
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            const string timedOut = "Gateway did not respond (timed out).";
            LoadError = timedOut;
            ResetDisconnected(timedOut);
            Log(timedOut, LogKind.Err);
        }
        catch (OperationCanceledException)
        {
            ResetDisconnected("Cancelled.");
            Log("Cancelled.", LogKind.Warn);
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            ResetDisconnected("Connection failed.");
            Log(ex.Message, LogKind.Err, traceText: Traceable(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Message shown (and logged) when the session and the active environment have diverged.</summary>
    private const string ReconnectRequired = "Connected to a different environment — reconnect required.";

    // True when the live session belongs to an environment other than the one that's now active. The shell
    // can switch the active environment while this cached VM keeps its session (the "Refresh open tools?"
    // prompt is declinable), which would otherwise leave the gateway/cid pointing at the old environment
    // while the header shows the new one — and, for the debug toggle, straddle both in one operation
    // (project ids from the old session, the PATCH through an _odata that resolves the active env per call).
    private bool SessionEnvMismatch() =>
        _session is not null && !string.Equals(_session.EnvId, _activeEnv()?.Id, StringComparison.Ordinal);

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
        action is not null && !IsBusy && _session is not null && !SessionEnvMismatch() && SelectedCount > 0;

    // Opens the confirm dialog; mutates nothing until the user accepts, then submits + polls to terminal.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRunAction))]
    private async Task RunAction(OpsAction action, CancellationToken ct)
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

        var request = new ConfirmRequest(
            Title: $"{action.Label} {targets.Count} map(s)?",
            Message: $"Sends {action.Label} to the dual-write gateway for {ConnectionName}.",
            // Only the maps actually being sent: the dialog is the last chance to see what will happen.
            Targets: targets.Select(t => $"{t.Name} · {t.CeEntity} · {t.State}").ToList(),
            ConfirmLabel: action.Label,
            IsDanger: action.Danger,
            Caveat: action.Caveat);

        if (!await _dialogs.ConfirmAsync(request))
        {
            return;
        }

        IsBusy = true;
        Status = $"{action.Label}…";
        Log($"{action.Label}: {targets.Count} map(s) (cid {session.Cid})…");
        try
        {
            var maps = targets.Select(t => t.Map).ToList();
            var response = await session.Gateway.StartActionAsync(action.Type, maps, session.Cid, ct);

            // Past this line the action IS submitted; everything below only reports on it. So nothing here
            // may read as "the action failed", and nothing here may skip the refresh — a user shown a
            // failure over a stale grid is a user who submits the same Initial sync a second time.
            if (string.IsNullOrWhiteSpace(response.RequestId))
            {
                // 202 with no body, or a bare id the gateway didn't label. Submitted, but there is nothing
                // to poll — and GetStatusAsync with a blank id is an error, so it must not be called.
                Status = $"{action.Label} submitted — the gateway did not return a request id; refresh to see the result.";
                Log(Status, LogKind.Warn);
            }
            else
            {
                await PollAndReportAsync(action, session, response.RequestId, ct);
            }

            // Refresh the maps to pick up their new states, preserving the user's selection. A refresh
            // failure must NOT overwrite the action result above (the action still happened), so it's
            // caught separately — the grid just keeps its pre-refresh display.
            try
            {
                // Keep the whole selection, not just the maps sent: the user selected the skipped ones too
                // and nothing was done to them, so silently deselecting them would hide the skip.
                var keep = selected.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
                var refreshed = await session.Gateway.GetMapsAsync(session.Cid, ct);
                PopulateMaps(refreshed, keep);
            }
            catch (Exception)
            {
                Status += " (map list could not refresh)";
                Log("Map list could not refresh.", LogKind.Warn);
            }
        }
        catch (OperationCanceledException)
        {
            // Only the submit itself or a user cancel reaches here: the polling timeout is handled inside
            // PollAndReportAsync so that it still reaches the refresh, and the refresh has its own catch.
            Status = ct.IsCancellationRequested ? "Cancelled." : $"{action.Label} timed out.";
            Log(Status, LogKind.Warn);
        }
        catch (Exception ex)
        {
            Status = $"{action.Label} failed: {ex.Message}";
            Log(Status, LogKind.Err, traceText: $"{action.Label} failed: {Traceable(ex)}");
        }
        finally
        {
            // Appended last so it survives every branch above (completed, submitted-but-unpollable, timed
            // out, failed, cancelled): whatever the outcome for the maps that were sent, the ones left out
            // were still left out, and the user has to be told which.
            if (skipNote is not null)
            {
                Status = $"{Status} {skipNote}";
            }

            IsBusy = false;
        }
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
                var status = await session.Gateway.GetStatusAsync(requestId, pollToken);
                if (status.IsTerminal)
                {
                    final = status;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
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
            var submitted = $"{action.Label} submitted — status check failed";
            Status = $"{submitted} ({Concise(ex)}); refreshing the map list.";
            Log(Status, LogKind.Warn, traceText: $"{submitted} ({Traceable(ex)}); refreshing the map list.");
            return;
        }

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

    private bool CanToggleDebug() =>
        !IsBusy && _session is not null && !SessionEnvMismatch() && SelectedCount > 0;

    // Enable dual-write debug mode for the selected map(s); scoped to the selection, never all maps.
    [RelayCommand(CanExecute = nameof(CanToggleDebug))]
    private Task EnableDebugForSelected(CancellationToken ct) => SetDebugForSelectedAsync(true, ct);

    // Disable dual-write debug mode for the selected map(s).
    [RelayCommand(CanExecute = nameof(CanToggleDebug))]
    private Task DisableDebugForSelected(CancellationToken ct) => SetDebugForSelectedAsync(false, ct);

    // Toggles IsDebugMode on the F&O DualWriteProjectConfiguration entity for each selected map's
    // project. Debug mode is an F&O-side, project-level flag (verbose dual-write logging to the
    // DualWriteErrorLog table) — so this targets F&O OData, not the gateway. The OData set name is
    // resolved from live $metadata (it varies per environment); every step degrades to a clear,
    // non-sensitive message rather than throwing, and no token is ever surfaced.
    private async Task SetDebugForSelectedAsync(bool enabled, CancellationToken ct)
    {
        if (_session is null)
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

        IsBusy = true;
        DebugStatus = enabled ? "Enabling debug mode…" : "Disabling debug mode…";
        try
        {
            // Resolve the F&O OData set that exposes IsDebugMode from the environment's live metadata.
            // Only fetch $metadata if it isn't cached yet — the set name is stable within a session, and
            // a re-fetch of that large XML document would add a needless round-trip before the toggle.
            if (_metadata.GetEntities().Count == 0)
            {
                await _metadata.LoadEntitiesAsync(ct).ConfigureAwait(true);
            }

            var set = _metadata.GetEntities()
                .Select(e => e.Name)
                .FirstOrDefault(n => n.Contains(DualWriteDebugMode.EntityLogicalName, StringComparison.OrdinalIgnoreCase));
            if (set is null)
            {
                DebugStatus = $"This environment's OData metadata exposes no '{DualWriteDebugMode.EntityLogicalName}' entity, so debug mode can't be toggled from here.";
                return;
            }

            var body = DualWriteDebugMode.BuildPatchBody(enabled);
            // Full metadata so each record carries an @odata.id we can PATCH directly.
            var getHeaders = new Dictionary<string, string> { ["Accept"] = "application/json;odata.metadata=full" };
            var patchHeaders = new Dictionary<string, string> { ["If-Match"] = "*" };

            var ok = 0;
            var failures = new List<string>();

            // Re-checked immediately before EVERY request (the initial GET and each PATCH) — the entry guard
            // above expires the moment we await. _odata resolves the active environment per call while the
            // project ids came from this session's maps, so a switch mid-run would apply env A's ids to
            // env B. On a trip we stop where we are and say how far we got; already-applied PATCHes are not
            // rolled back, because each was valid for the environment it was issued against.
            bool StopIfEnvChanged(int applied)
            {
                if (!BlockedByEnvMismatch())
                {
                    return false;
                }

                DebugStatus = applied == 0
                    ? ReconnectRequired
                    : $"Stopped after {applied} of {projectIds.Count} — environment changed; reconnect required.";
                return true;
            }

            foreach (var pid in projectIds)
            {
                if (StopIfEnvChanged(ok))
                {
                    return;
                }

                // OData string-literal escaping doubles single quotes (not %27); GUID ids are URL-safe.
                var getPath = $"data/{set}?$filter=ProjectId eq '{pid.Replace("'", "''")}'";
                var get = await _odata.SendAsync("GET", getPath, null, getHeaders, ct).ConfigureAwait(true);
                if (!get.IsSuccess)
                {
                    failures.Add($"{pid}: query failed ({get.StatusLine})");
                    continue;
                }

                var record = DualWriteDebugMode.ReadFirstRecord(get.Body);
                if (record is null)
                {
                    failures.Add($"{pid}: no project-config record found");
                    continue;
                }

                if (StopIfEnvChanged(ok))
                {
                    return;
                }

                var patch = await _odata.SendAsync("PATCH", record.ODataId, body, patchHeaders, ct).ConfigureAwait(true);
                if (patch.IsSuccess)
                {
                    ok++;
                }
                else
                {
                    failures.Add($"{pid}: {patch.StatusLine}");
                }
            }

            var verb = enabled ? "enabled" : "disabled";
            DebugStatus = failures.Count == 0
                ? $"Debug mode {verb} for {ok} project(s)."
                : ok == 0
                    ? $"Debug mode not {verb}: {string.Join("; ", failures)}"
                    : $"Debug mode {verb} for {ok} project(s); {failures.Count} failed: {string.Join("; ", failures)}";
        }
        catch (OperationCanceledException)
        {
            DebugStatus = "Cancelled.";
        }
        catch (Exception ex)
        {
            DebugStatus = $"Debug-mode toggle failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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

    // The shell discards this VM (without finalization) when the active environment changes; dispose the
    // live gateway session so its owned HttpClient / connection pool isn't leaked for the app's lifetime.
    public void Dispose() => DisposeSession();
}
