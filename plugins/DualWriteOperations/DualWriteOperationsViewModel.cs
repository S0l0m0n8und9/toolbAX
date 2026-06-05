using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;

namespace DualWriteOperationsPlugin;

/// <summary>
/// Drives the Dual-write Management gateway: resolve environment, list maps, and run
/// lifecycle actions (start/stop/pause/resume/initial-sync) with live status polling.
/// Connection settings (gateway URL and F&amp;O identifier) are owned by the plugin via
/// <see cref="DualWriteConnectionStore"/>; tokens are obtained exclusively through the
/// interactive sign-in flow and stored DPAPI-protected. The host auth/profile schema is untouched.
/// </summary>
public sealed class DualWriteOperationsViewModel : INotifyPropertyChanged
{
    private readonly IPluginContext _ctx;
    private readonly DualWriteConnectionStore _store;
    private readonly IDualWriteGatewayFactory _factory;
    private readonly string _envId;

    private IDualWriteGateway? _gateway;
    private string? _cid;
    private DualWriteEnvironment? _environment;
    private string _gatewayBaseUrl = string.Empty;
    private string _foIdentifier = string.Empty;
    private string _applyVersionAuthorFilter = string.Empty;
    private bool _forceReset;
    private string _mapSearch = string.Empty;
    private string _columnFilterMap = string.Empty;
    private string _columnFilterCeEntity = string.Empty;
    private string _columnFilterVersion = string.Empty;
    private string _columnFilterAuthor = string.Empty;
    private string _columnFilterState = string.Empty;
    private string _mapListSummary = "Showing 0 of 0 map(s)";
    private string _statusMessage = "Configure the connection, then Load Maps.";
    private string _connectionSummary = "Not connected.";
    private bool _isBusy;

    /// <summary>Confirmation gate for mutating actions. Overridable for tests.</summary>
    internal Func<string, string, bool> ConfirmAction { get; set; } = DefaultConfirm;

    /// <summary>Status poll cadence; small in tests.</summary>
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Maximum status polls before giving up and telling the user to check the portal.</summary>
    internal int MaxPollAttempts { get; set; } = 40;

    /// <summary>Chooses an export file path (returns null to cancel). Overridable for tests.</summary>
    internal Func<string, string?> ChooseExportPath { get; set; } = DefaultChooseExportPath;

    /// <summary>Clock for export timestamps. Overridable for tests.</summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Runs the interactive sign-in for an F&amp;O identifier. The bool requests a fresh sign-in
    /// (forget the cached browser account). Overridable for tests.
    /// </summary>
    internal Func<string, bool, Task<DualWriteSignInResult?>> SignInFlow { get; set; } = DefaultSignInAsync;

    public DualWriteOperationsViewModel(IPluginContext ctx)
        : this(ctx, new DualWriteConnectionStore(), new DualWriteGatewayFactory())
    {
    }

    internal DualWriteOperationsViewModel(IPluginContext ctx, DualWriteConnectionStore store, IDualWriteGatewayFactory factory)
    {
        _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _envId = ctx.CurrentEnv.Id;

        Action<Exception> onError = ex =>
        {
            _ctx.Logger.LogError(ex, "DualWriteOperations command failed.");
            StatusMessage = $"Command failed: {ex.Message}";
            IsBusy = false;
        };

        SignInCommand = new AsyncRelayCommand(SignInAsync, onError);
        SignInFreshCommand = new AsyncRelayCommand(SignInFreshAsync, onError);
        SaveConnectionCommand = new AsyncRelayCommand(SaveConnectionAsync, onError);
        LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
        StartCommand = new AsyncRelayCommand(ct => ExecuteActionAsync(DualWriteActionType.Start, ct), onError);
        StopCommand = new AsyncRelayCommand(ct => ExecuteActionAsync(DualWriteActionType.Stop, ct), onError);
        PauseCommand = new AsyncRelayCommand(ct => ExecuteActionAsync(DualWriteActionType.Pause, ct), onError);
        ResumeCommand = new AsyncRelayCommand(ct => ExecuteActionAsync(DualWriteActionType.Resume, ct), onError);
        InitialSyncCommand = new AsyncRelayCommand(ct => ExecuteActionAsync(DualWriteActionType.InitialSync, ct), onError);
        ApplyLatestVersionCommand = new AsyncRelayCommand(ApplyLatestVersionAsync, onError);
        RefreshTablesCommand = new AsyncRelayCommand(RefreshTablesAsync, onError);
        ExportConfigCommand = new AsyncRelayCommand(ExportConfigAsync, onError);
        ResetLinkCommand = new AsyncRelayCommand(ResetLinkAsync, onError);
        ApplyIntegrationKeysCommand = new AsyncRelayCommand(ApplyIntegrationKeysAsync, onError);
        ClearTokenCommand = new AsyncRelayCommand(ClearTokenAsync, onError);

        MapsView = CollectionViewSource.GetDefaultView(Maps);
        MapsView.Filter = o => o is DualWriteMapRow row && RowVisible(row);
        // The summary is recomputed once after each bulk load (LoadMaps/RefreshMapStates) and on
        // search or column-filter change — avoiding an O(n^2) scan if we recomputed on every per-item Add.
        UpdateMapListSummary();

        _ = InitializeAsync();
    }

    public ObservableCollection<DualWriteMapRow> Maps { get; } = new();

    /// <summary>Filtered/searchable view over <see cref="Maps"/> bound by the grid (#31).</summary>
    public ICollectionView MapsView { get; }

    public AsyncRelayCommand SignInCommand { get; }
    public AsyncRelayCommand SignInFreshCommand { get; }
    public AsyncRelayCommand SaveConnectionCommand { get; }
    public AsyncRelayCommand LoadMapsCommand { get; }
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand ResumeCommand { get; }
    public AsyncRelayCommand InitialSyncCommand { get; }
    public AsyncRelayCommand ApplyLatestVersionCommand { get; }
    public AsyncRelayCommand RefreshTablesCommand { get; }
    public AsyncRelayCommand ExportConfigCommand { get; }
    public AsyncRelayCommand ResetLinkCommand { get; }
    public AsyncRelayCommand ApplyIntegrationKeysCommand { get; }
    public AsyncRelayCommand ClearTokenCommand { get; }

    public string EnvironmentName => _ctx.CurrentEnv.Name;

    /// <summary>
    /// Optional comma/semicolon-separated list of template authors that scopes ONLY the
    /// "Apply Latest Version" action: it restricts which author's template versions are considered
    /// when picking the latest (empty = any author). It does NOT filter the visible map list and
    /// does NOT affect Start/Stop/Pause/Resume, Refresh Tables, Export, Reset Link, or Apply
    /// Integration Keys — those operate on the explicitly selected/loaded maps (#29).
    /// </summary>
    public string ApplyVersionAuthorFilter
    {
        get => _applyVersionAuthorFilter;
        set { if (_applyVersionAuthorFilter != value) { _applyVersionAuthorFilter = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// Case-insensitive search applied to the map list view (#31). Filters by name, CE entity,
    /// version, author, state and identifiers. Blank shows all maps; it only narrows the displayed
    /// list and does not change which maps an action targets (actions use the row checkboxes).
    /// </summary>
    public string MapSearch
    {
        get => _mapSearch;
        set
        {
            if (_mapSearch == value)
            {
                return;
            }

            _mapSearch = value;
            OnPropertyChanged();
            RefreshMapView();
        }
    }

    /// <summary>Per-column filter for the Map column (#30). ANDed with the search and other columns.</summary>
    public string ColumnFilterMap
    {
        get => _columnFilterMap;
        set { if (_columnFilterMap != value) { _columnFilterMap = value; OnPropertyChanged(); RefreshMapView(); } }
    }

    /// <summary>Per-column filter for the CE Entity column (#30).</summary>
    public string ColumnFilterCeEntity
    {
        get => _columnFilterCeEntity;
        set { if (_columnFilterCeEntity != value) { _columnFilterCeEntity = value; OnPropertyChanged(); RefreshMapView(); } }
    }

    /// <summary>Per-column filter for the Version column (#30).</summary>
    public string ColumnFilterVersion
    {
        get => _columnFilterVersion;
        set { if (_columnFilterVersion != value) { _columnFilterVersion = value; OnPropertyChanged(); RefreshMapView(); } }
    }

    /// <summary>Per-column filter for the Author column (#30). Distinct from the apply-version author filter.</summary>
    public string ColumnFilterAuthor
    {
        get => _columnFilterAuthor;
        set { if (_columnFilterAuthor != value) { _columnFilterAuthor = value; OnPropertyChanged(); RefreshMapView(); } }
    }

    /// <summary>Per-column filter for the State column (#30).</summary>
    public string ColumnFilterState
    {
        get => _columnFilterState;
        set { if (_columnFilterState != value) { _columnFilterState = value; OnPropertyChanged(); RefreshMapView(); } }
    }

    /// <summary>"Showing X of Y map(s)" — reflects the active search/filters so the empty-result state is clear.</summary>
    public string MapListSummary
    {
        get => _mapListSummary;
        private set { if (_mapListSummary != value) { _mapListSummary = value; OnPropertyChanged(); } }
    }

    /// <summary>True when a row passes the search and every active per-column filter (#31/#30).</summary>
    private bool RowVisible(DualWriteMapRow row) =>
        row.Matches(MapSearch) &&
        row.MatchesColumnFilters(ColumnFilterMap, ColumnFilterCeEntity, ColumnFilterVersion, ColumnFilterAuthor, ColumnFilterState);

    private void RefreshMapView()
    {
        MapsView.Refresh();
        UpdateMapListSummary();
    }

    private void UpdateMapListSummary()
    {
        var total = Maps.Count;
        var visible = Maps.Count(RowVisible);
        MapListSummary = $"Showing {visible} of {total} map(s)";
    }

    /// <summary>When true, the reset-link request sets forceReset=true.</summary>
    public bool ForceReset
    {
        get => _forceReset;
        set { if (_forceReset != value) { _forceReset = value; OnPropertyChanged(); } }
    }

    public string GatewayBaseUrl
    {
        get => _gatewayBaseUrl;
        set { if (_gatewayBaseUrl != value) { _gatewayBaseUrl = value; OnPropertyChanged(); } }
    }

    public string FoIdentifier
    {
        get => _foIdentifier;
        set { if (_foIdentifier != value) { _foIdentifier = value; OnPropertyChanged(); } }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string ConnectionSummary
    {
        get => _connectionSummary;
        set { _connectionSummary = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotBusy)); } }
    }

    public bool IsNotBusy => !IsBusy;

    private async Task InitializeAsync()
    {
        try
        {
            var settings = await _store.GetAsync(_envId, CancellationToken.None);
            GatewayBaseUrl = settings.GatewayBaseUrl;
            FoIdentifier = string.IsNullOrWhiteSpace(settings.FoIdentifier)
                ? _ctx.CurrentEnv.BaseUrl
                : settings.FoIdentifier;
            UpdateConnectionSummary(settings);
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed loading saved dual-write connection.");
        }
    }

    private Task SignInAsync(CancellationToken ct) => SignInCoreAsync(clearCachedAccount: false, ct);

    /// <summary>"Switch account": forget the cached browser session and sign in again.</summary>
    private Task SignInFreshAsync(CancellationToken ct) => SignInCoreAsync(clearCachedAccount: true, ct);

    private async Task SignInCoreAsync(bool clearCachedAccount, CancellationToken ct)
    {
        var identifier = string.IsNullOrWhiteSpace(FoIdentifier) ? _ctx.CurrentEnv.BaseUrl : FoIdentifier.Trim();
        if (string.IsNullOrWhiteSpace(identifier))
        {
            StatusMessage = "Set the F&O identifier before signing in.";
            return;
        }

        StatusMessage = clearCachedAccount
            ? "Opening interactive sign-in (switch account)..."
            : "Opening interactive sign-in...";
        var result = await SignInFlow(identifier, clearCachedAccount);
        if (result is null)
        {
            StatusMessage = "Sign-in cancelled or no token captured.";
            return;
        }

        var settings = new DualWriteConnectionSettings(_envId, result.GatewayBaseUrl, identifier, result.Token.AccessToken)
        {
            RefreshToken = result.Token.RefreshToken,
            AccessTokenExpiryUtc = result.Token.ExpiresUtc
        };
        await _store.SaveAsync(settings, ct);

        GatewayBaseUrl = result.GatewayBaseUrl;
        FoIdentifier = identifier;
        _gateway = null;
        _cid = null;
        _environment = null;
        UpdateConnectionSummary(settings);
        StatusMessage = $"Signed in. Gateway discovered: {result.GatewayBaseUrl}. Click Load Maps.";
    }

    private IDualWriteGateway BuildGateway(DualWriteConnectionSettings settings)
    {
        if (!settings.HasDelegatedSession)
        {
            return _factory.Create(settings);
        }

        // Delegated session: renew the access token via the refresh token and persist the
        // rotated token so the next session/operation stays signed in.
        return _factory.CreateRefreshing(settings, async refreshed =>
        {
            var updated = settings with
            {
                BearerToken = refreshed.AccessToken,
                RefreshToken = refreshed.RefreshToken,
                AccessTokenExpiryUtc = refreshed.ExpiresUtc
            };
            await _store.SaveAsync(updated, CancellationToken.None);
        });
    }

    private static async Task<DualWriteSignInResult?> DefaultSignInAsync(string foIdentifier, bool clearCachedAccount)
    {
        var window = new DualWriteSignInWindow(foIdentifier, clearCachedAccount);
        if (Application.Current?.MainWindow is not null && !ReferenceEquals(Application.Current.MainWindow, window))
        {
            window.Owner = Application.Current.MainWindow;
        }

        return await window.SignInAsync();
    }

    private async Task SaveConnectionAsync(CancellationToken ct)
    {
        var url = GatewayBaseUrl?.Trim() ?? string.Empty;
        var identifier = FoIdentifier?.Trim() ?? string.Empty;

        // Tokens are never entered in the UI — they come from the interactive sign-in flow only.
        // Saving the connection just updates the gateway URL / identifier and keeps whatever
        // sign-in session is already stored.
        var existing = await _store.GetAsync(_envId, ct);
        var settings = existing with { GatewayBaseUrl = url, FoIdentifier = identifier };

        await _store.SaveAsync(settings, ct);
        _gateway = null;
        _cid = null;
        UpdateConnectionSummary(settings);
        StatusMessage = settings.IsComplete
            ? "Connection saved. Click Load Maps."
            : "Saved. Provide the gateway URL and F&O identifier, then sign in to enable operations.";
    }

    private async Task ClearTokenAsync(CancellationToken ct)
    {
        var existing = await _store.GetAsync(_envId, ct);
        var cleared = new DualWriteConnectionSettings(_envId, existing.GatewayBaseUrl, existing.FoIdentifier, null);
        await _store.SaveAsync(cleared, ct);
        _gateway = null;
        _cid = null;
        UpdateConnectionSummary(cleared);
        StatusMessage = "Sign-in session cleared. Sign in again before loading maps.";
    }

    private async Task LoadMapsAsync(CancellationToken ct)
    {
        var settings = await _store.GetAsync(_envId, ct);
        if (!settings.IsComplete)
        {
            StatusMessage = "Configure the gateway URL and F&O identifier, sign in, then Load Maps.";
            return;
        }

        IsBusy = true;
        try
        {
            _gateway = BuildGateway(settings);
            StatusMessage = "Resolving dual-write environment...";
            var env = await _gateway.GetEnvironmentAsync(settings.FoIdentifier, ct);
            _cid = env.Cid;
            _environment = env;
            if (string.IsNullOrWhiteSpace(_cid))
            {
                StatusMessage = "Environment resolved but no connection id (cid) was returned. Check the identifier and token.";
                return;
            }

            // The cid is a GUID — keep it in the diagnostic surface only, never in UI status text (#27).
            _ctx.Logger.LogInformation("Dual-write connected (cid={Cid}, cname={Cname}).", env.Cid, env.Cname);

            StatusMessage = $"Loading maps for {DescribeEnv(env)}...";
            var maps = await _gateway.GetMapsAsync(_cid, ct);
            Maps.Clear();
            foreach (var map in maps)
            {
                Maps.Add(new DualWriteMapRow(map));
            }
            UpdateMapListSummary();

            ConnectionSummary = $"Connected to {DescribeEnv(env)} — {maps.Count} map(s).";
            StatusMessage = $"Loaded {maps.Count} map(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExecuteActionAsync(DualWriteActionType action, CancellationToken ct)
    {
        if (_gateway is null || string.IsNullOrWhiteSpace(_cid))
        {
            StatusMessage = "Load maps before running an action.";
            return;
        }

        var selected = Maps.Where(r => r.IsSelected).Select(r => r.Map).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one map (checkbox) before running an action.";
            return;
        }

        // Validate required gateway context up front: a selected map missing its template id (tid)
        // or, for non-initial-sync actions, its project id (pid) would otherwise be sent and surface
        // as an opaque gateway "500 NullReferenceException" (#25). Fail with a clear, map-named error.
        var missingContext = selected.Where(m => !HasRequiredActionContext(m, action)).ToList();
        if (missingContext.Count > 0)
        {
            var missingNames = string.Join(", ", missingContext.Select(DescribeMap));
            StatusMessage =
                $"Cannot {action.ToDisplayName()}: map(s) {missingNames} are missing required dual-write context " +
                "(project id / template). Reload maps and verify the dual-write configuration in the portal.";
            return;
        }

        var names = string.Join(", ", selected.Select(DescribeMap));
        var detail = $"{action.ToDisplayName()} the following map(s) on the LIVE environment '{EnvironmentName}'?\n\n{names}";
        if (action == DualWriteActionType.InitialSync)
        {
            detail += "\n\nInitial sync re-synchronises data and can be long-running.";
        }

        if (!ConfirmAction($"{action.ToDisplayName()} dual-write map(s)", detail))
        {
            StatusMessage = $"{action.ToDisplayName()} cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var actionSucceeded = false;
            try
            {
                StatusMessage = $"Submitting {action.ToDisplayName()} for {selected.Count} map(s)...";
                var response = await _gateway.StartActionAsync(action, selected, _cid!, ct);
                if (string.IsNullOrWhiteSpace(response.RequestId))
                {
                    StatusMessage = $"{action.ToDisplayName()} submitted (gateway returned no request id to poll).";
                }
                else
                {
                    await PollUntilTerminalAsync(action, response.RequestId, ct);
                }

                actionSucceeded = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Name the action + affected map(s) so a gateway failure is diagnosable; the
                // message carries only non-sensitive gateway detail (never the bearer token).
                _ctx.Logger.LogError(ex, "Dual-write {Action} failed for {MapCount} map(s).", action.ToDisplayName(), selected.Count);
                StatusMessage = $"{action.ToDisplayName()} failed for map(s) {names}: {ex.Message}";
            }

            // Refresh map states separately: a transient refresh failure must never be reported as
            // the lifecycle action having failed (the action already succeeded, and a spurious
            // "failed" message could prompt a harmful retry of a map that already transitioned).
            try
            {
                await RefreshMapStatesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _ctx.Logger.LogError(ex, "Refreshing dual-write map states after {Action} failed.", action.ToDisplayName());
                if (actionSucceeded)
                {
                    StatusMessage = $"{action.ToDisplayName()} finished, but refreshing the map list failed: {ex.Message}. Reload maps to see the current state.";
                }
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// True when the map carries the gateway context a lifecycle action requires: a template id
    /// (tid) for every action, plus a project id (pid) for all actions except initial sync.
    /// </summary>
    private static bool HasRequiredActionContext(DualWriteMap map, DualWriteActionType action)
    {
        var hasTemplate = !string.IsNullOrWhiteSpace(map.ActiveTemplate?.Id) || !string.IsNullOrWhiteSpace(map.Id);
        if (!hasTemplate)
        {
            return false;
        }

        return action == DualWriteActionType.InitialSync || !string.IsNullOrWhiteSpace(map.ProjectId);
    }

    /// <summary>A human-friendly identifier for a map, for status/error messages.</summary>
    private static string DescribeMap(DualWriteMap map) =>
        !string.IsNullOrWhiteSpace(map.DisplayName) ? map.DisplayName
        : !string.IsNullOrWhiteSpace(map.Name) ? map.Name
        : !string.IsNullOrWhiteSpace(map.Id) ? map.Id
        : "(unknown map)";

    private async Task ApplyLatestVersionAsync(CancellationToken ct)
    {
        if (_gateway is null || string.IsNullOrWhiteSpace(_cid))
        {
            StatusMessage = "Load maps before applying a version.";
            return;
        }

        var selected = Maps.Where(r => r.IsSelected).Select(r => r.Map).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one map (checkbox) before applying a version.";
            return;
        }

        var authors = TemplateSelector.ParseAuthorFilter(ApplyVersionAuthorFilter);
        var plan = new List<(DualWriteMap Map, DualWriteTemplate Template)>();
        var skipped = new List<string>();
        foreach (var map in selected)
        {
            var template = TemplateSelector.SelectLatest(map.Templates, authors);
            if (template is null)
            {
                skipped.Add(MapLabel(map));
            }
            else
            {
                plan.Add((map, template));
            }
        }

        if (plan.Count == 0)
        {
            StatusMessage = "No applicable template version found for the selected map(s).";
            return;
        }

        var planLines = string.Join("\n", plan.Select(p => $"  {MapLabel(p.Map)} → v{p.Template.Version} ({p.Template.Author})"));
        var authorNote = authors.Count == 0 ? "latest version (any author)" : $"latest version by [{string.Join(", ", authors)}]";
        if (!ConfirmAction("Apply map version(s)",
                $"Apply the {authorNote} for these map(s) on the LIVE environment '{EnvironmentName}'?\n\n{planLines}"))
        {
            StatusMessage = "Apply version cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var applied = 0;
            foreach (var (map, template) in plan)
            {
                ct.ThrowIfCancellationRequested();
                StatusMessage = $"Applying v{template.Version} to {MapLabel(map)}...";
                await _gateway.SwitchActiveTemplateAsync(_cid!, map.ProjectId, template.Id, ct);
                applied++;
            }

            await RefreshMapStatesAsync(ct);
            var skippedNote = skipped.Count == 0 ? string.Empty : $" Skipped {skipped.Count} with no matching version.";
            StatusMessage = $"Applied version to {applied} map(s).{skippedNote}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshTablesAsync(CancellationToken ct)
    {
        if (_gateway is null || string.IsNullOrWhiteSpace(_cid))
        {
            StatusMessage = "Load maps before refreshing tables.";
            return;
        }

        var selected = Maps.Where(r => r.IsSelected).Select(r => r.Map).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one map (checkbox) before refreshing tables.";
            return;
        }

        var names = string.Join(", ", selected.Select(MapLabel));
        if (!ConfirmAction("Refresh tables",
                $"Refresh table metadata for these map(s) on the LIVE environment '{EnvironmentName}'?\n\n{names}"))
        {
            StatusMessage = "Refresh tables cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            var refreshed = 0;
            foreach (var map in selected)
            {
                ct.ThrowIfCancellationRequested();
                StatusMessage = $"Refreshing tables for {MapLabel(map)}...";
                var fieldMappings = await _gateway.GetFieldMappingsAsync(map.ProjectId, ct);
                foreach (var fieldMapping in fieldMappings)
                {
                    ct.ThrowIfCancellationRequested();
                    await _gateway.RefreshTablesAsync(fieldMapping.Name, ct);
                    refreshed++;
                }
            }

            StatusMessage = refreshed == 0
                ? "No field mappings found to refresh for the selected map(s)."
                : $"Refreshed {refreshed} field mapping(s) across {selected.Count} map(s).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportConfigAsync(CancellationToken ct)
    {
        if (_environment is null || Maps.Count == 0)
        {
            StatusMessage = "Load maps before exporting the configuration.";
            return;
        }

        var maps = Maps.Select(r => r.Map).ToList();
        var json = DualWriteConfigExporter.ExportJson(_environment, maps, Clock());

        var suggestedName = $"dualwrite-config-{Sanitize(EnvironmentName)}.json";
        var path = ChooseExportPath(suggestedName);
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = "Export cancelled.";
            return;
        }

        await File.WriteAllTextAsync(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        StatusMessage = $"Exported {maps.Count} map(s) to {Path.GetFileName(path)}.";
    }

    private async Task ResetLinkAsync(CancellationToken ct)
    {
        if (_gateway is null || _environment is null || string.IsNullOrWhiteSpace(_environment.Cname) || string.IsNullOrWhiteSpace(_cid))
        {
            StatusMessage = "Load maps before resetting the link.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Loading connection set...";
            var connectionSet = await _gateway.GetConnectionSetAsync(_environment.Cname, ct);
            var legalEntities = connectionSet.LegalEntities;
            if (legalEntities.Count == 0)
            {
                StatusMessage = "No legal entities found in the connection set; nothing to reset.";
                return;
            }

            var forceNote = ForceReset ? "\n\nForce reset is ON." : string.Empty;
            if (!ConfirmAction("Reset dual-write link",
                    $"Reset the dual-write link on the LIVE environment '{EnvironmentName}' for {legalEntities.Count} legal entit{(legalEntities.Count == 1 ? "y" : "ies")}?\n\n{string.Join(", ", legalEntities)}\n\nThis re-initialises the link and can disrupt running maps.{forceNote}"))
            {
                StatusMessage = "Reset link cancelled.";
                return;
            }

            StatusMessage = "Submitting reset link...";
            await _gateway.ResetLinksAsync(_cid!, connectionSet, legalEntities, ForceReset, ct);
            StatusMessage = $"Reset link submitted for {legalEntities.Count} legal entit{(legalEntities.Count == 1 ? "y" : "ies")}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ApplyIntegrationKeysAsync(CancellationToken ct)
    {
        if (_gateway is null || _environment is null || string.IsNullOrWhiteSpace(_environment.Cname) || string.IsNullOrWhiteSpace(_cid))
        {
            StatusMessage = "Load maps before applying integration keys.";
            return;
        }

        var selected = Maps.Where(r => r.IsSelected).Select(r => r.Map).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "Select at least one map (checkbox) before applying integration keys.";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "Loading connection set...";
            var connectionSet = await _gateway.GetConnectionSetAsync(_environment.Cname, ct);
            var ce = connectionSet.CeEnvironment;
            if (ce is null)
            {
                StatusMessage = "No CE environment found in the connection set.";
                return;
            }

            // Resolve the integration key for each map up-front so we can show a precise plan
            // and skip maps we can't resolve rather than risk a wrong call.
            var plan = new List<(DualWriteMap Map, DualWriteSchemaKey Key)>();
            var skipped = new List<string>();
            foreach (var map in selected)
            {
                var key = string.IsNullOrWhiteSpace(map.RightEntityName)
                    ? null
                    : connectionSet.GetIntegrationKey(map.RightEntityName);
                if (key is null || key.Fields.Count == 0)
                {
                    skipped.Add(MapLabel(map));
                }
                else
                {
                    plan.Add((map, key));
                }
            }

            if (plan.Count == 0)
            {
                StatusMessage = "Could not resolve integration keys for the selected map(s) (no CE entity match).";
                return;
            }

            var planLines = string.Join("\n", plan.Select(p => $"  {p.Map.RightEntityName}: {string.Join(", ", p.Key.Fields)}"));
            if (!ConfirmAction("Apply integration keys",
                    $"Apply integration keys on the LIVE environment '{EnvironmentName}' for these CE entit(y/ies)?\n\n{planLines}"))
            {
                StatusMessage = "Apply integration keys cancelled.";
                return;
            }

            var applied = 0;
            foreach (var (map, key) in plan)
            {
                ct.ThrowIfCancellationRequested();
                StatusMessage = $"Applying integration keys for {map.RightEntityName}...";
                await _gateway.ApplyIntegrationKeysAsync(ce.Name, map.RightEntityName, key.Fields, ct);
                applied++;
            }

            var skippedNote = skipped.Count == 0 ? string.Empty : $" Skipped {skipped.Count} (no key resolved).";
            StatusMessage = $"Applied integration keys for {applied} entit{(applied == 1 ? "y" : "ies")}.{skippedNote}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "environment" : cleaned;
    }

    private static string? DefaultChooseExportPath(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export dual-write configuration",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = suggestedName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string MapLabel(DualWriteMap map) =>
        string.IsNullOrWhiteSpace(map.DisplayName) ? map.Name : map.DisplayName;

    private async Task PollUntilTerminalAsync(DualWriteActionType action, string requestId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var status = await _gateway!.GetStatusAsync(requestId, ct);
            if (status.IsTerminal)
            {
                StatusMessage = status.IsSuccess
                    ? $"{action.ToDisplayName()} completed."
                    : $"{action.ToDisplayName()} failed: {status.Message ?? status.State}";
                return;
            }

            StatusMessage = $"{action.ToDisplayName()} in progress ({status.State})...";
            await Task.Delay(PollInterval, ct);
        }

        StatusMessage = $"{action.ToDisplayName()} still running after timeout; check the Power Platform portal.";
    }

    private async Task RefreshMapStatesAsync(CancellationToken ct)
    {
        if (_gateway is null || string.IsNullOrWhiteSpace(_cid))
        {
            return;
        }

        var maps = await _gateway.GetMapsAsync(_cid!, ct);
        var selectedNames = Maps.Where(r => r.IsSelected).Select(r => r.Map.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Maps.Clear();
        foreach (var map in maps)
        {
            Maps.Add(new DualWriteMapRow(map) { IsSelected = selectedNames.Contains(map.Id) });
        }
        UpdateMapListSummary();
    }

    private void UpdateConnectionSummary(DualWriteConnectionSettings settings)
    {
        ConnectionSummary = settings.IsComplete
            ? $"Configured for {settings.GatewayBaseUrl} (identifier: {settings.FoIdentifier})."
            : "Not connected — configure the gateway URL and F&O identifier, then sign in.";
    }

    // #27: never surface the raw connection id (cid, a GUID) in user-facing text. Prefer the
    // gateway's friendly connection name (cname), then the F&O environment's friendly name, and
    // fall back to a neutral label — but never the GUID. The cid stays available via logs.
    private string DescribeEnv(DualWriteEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(env.Cname))
        {
            return env.Cname;
        }

        return string.IsNullOrWhiteSpace(EnvironmentName) ? "environment" : EnvironmentName;
    }

    private static bool DefaultConfirm(string title, string message) =>
        MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.OK;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
