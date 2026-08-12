using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Models;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Why the app is running on the offline fake stack (#164) — design mode on a non-Windows platform, or a
/// profile-store failure. Non-null means NOTHING on screen came from a real environment.
/// </summary>
public sealed record DegradedMode(string Reason);

/// <summary>
/// Root shell view model (control-map §0): owns the tool list that drives the nav rail + content
/// host, the active environment, busy state, and the Ctrl+K command palette. Tool screens plug into
/// <see cref="CurrentTool"/> as they are built; for now the content host shows the tool title.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    public IReadOnlyList<NavTool> Tools { get; }
    public ObservableCollection<EnvProfile> Environments { get; }
    public CommandPaletteViewModel Palette { get; }

    // Factory for the (heavier) Operations screen VM, injected so tests can supply fakes. Built once
    // on first navigation to the Operations tool.
    private readonly Func<object> _operationsContentFactory;
    private readonly IProfileStore _profileStore;
    private readonly ISecretStore _secretStore;
    private readonly IInteractiveAuthBroker _authBroker;
    private readonly IClipboardService _clipboard;
    private readonly IAuthService _authService;
    private readonly IODataClient _odataClient;
    private readonly IMetadataService _metadataService;
    private readonly IDualWriteMapReader _mapReader;
    private readonly IFileSaveService _fileSave;
    private readonly IDualWriteGatewayTester _gatewayTester;
    private readonly IDualWriteCompareService _compareService;
    private readonly IConnectionTester _connectionTester;
    private readonly IDialogService _dialogs;
    private readonly IUrlLauncher _launcher;
    private readonly IVirtualTableReader _virtualTableReader;
    private object? _operationsContent;
    private object? _profilesContent;
    private object? _metadataContent;
    private object? _postContent;
    private object? _queryContent;
    private object? _mapBrowserContent;
    private object? _compareContent;
    private object? _virtualTablesContent;
    private object? _homeContent;

    [ObservableProperty]
    private NavTool _currentTool;

    /// <summary>The active screen VM the content host renders (routed from <see cref="CurrentTool"/>).</summary>
    [ObservableProperty]
    private object? _currentContent;

    // Nullable: a fresh profile store can be empty (no environments configured yet).
    [ObservableProperty]
    private EnvProfile? _activeEnvironment;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    /// <summary>
    /// Last last-resort failure message (see <c>App.InstallLastResortExceptionHandlers</c>), shown in the
    /// status strip; empty when none. Deliberately non-modal: the action failed, the app is still usable.
    /// </summary>
    [ObservableProperty]
    private string _backgroundError = string.Empty;

    /// <summary>Why the app is on the offline fake stack, or null when everything is real (#164).</summary>
    public DegradedMode? Degraded { get; }

    /// <summary>True when nothing on screen came from a real environment.</summary>
    public bool IsDegraded => Degraded is not null;

    /// <summary>Banner copy for the persistent degraded-mode strip; empty when not degraded.</summary>
    public string DegradedBannerText => Degraded is null
        ? string.Empty
        : $"Offline sample data — {Degraded.Reason}. Nothing on screen is live.";

    /// <summary>Surfaces a last-resort background failure in the status strip.</summary>
    public void ReportBackgroundFailure(string message) => BackgroundError = message;

    public ShellViewModel(
        Func<object>? operationsContentFactory = null,
        IProfileStore? profileStore = null,
        ISecretStore? secretStore = null,
        IInteractiveAuthBroker? authBroker = null,
        IClipboardService? clipboard = null,
        IAuthService? authService = null,
        IODataClient? odataClient = null,
        IMetadataService? metadataService = null,
        IDualWriteMapReader? mapReader = null,
        IFileSaveService? fileSave = null,
        IDualWriteGatewayTester? gatewayTester = null,
        IDualWriteCompareService? compareService = null,
        IConnectionTester? connectionTester = null,
        IDialogService? dialogs = null,
        IUrlLauncher? launcher = null,
        IVirtualTableReader? virtualTableReader = null,
        DegradedMode? degraded = null)
    {
        Degraded = degraded;
        _operationsContentFactory = operationsContentFactory ?? DefaultOperationsContent;
        _profileStore = profileStore ?? new FakeProfileStore();
        // TODO: design-mode fakes — swap the interactive broker (WebView2) for the real Windows
        // implementation as it's wired (profiles + secrets + auth + OData are already real on Windows).
        _secretStore = secretStore ?? new FakeSecretStore();
        _authBroker = authBroker ?? new FakeInteractiveAuthBroker();
        _clipboard = clipboard ?? new FakeClipboardService();
        _authService = authService ?? new FakeAuthService();
        _odataClient = odataClient ?? new FakeODataClient();
        _metadataService = metadataService ?? new FakeMetadataService();
        _mapReader = mapReader ?? new FakeDualWriteMapReader();
        _fileSave = fileSave ?? new FakeFileSaveService();
        _gatewayTester = gatewayTester ?? new FakeDualWriteGatewayTester();
        _compareService = compareService ?? new FakeDualWriteCompareService();
        _connectionTester = connectionTester ?? new FakeConnectionTester();
        // Real Fluent confirm dialog for mutating actions (POST Builder send); tests pass a stub.
        _dialogs = dialogs ?? new DialogService();
        _launcher = launcher ?? new FakeUrlLauncher();
        _virtualTableReader = virtualTableReader ?? new FakeVirtualTableReader();

        Tools = new[]
        {
            new NavTool("home", "Plugins", '\0'),
            new NavTool("query", "Query Builder", 'Q'),
            new NavTool("ops", "Dual-Write Operations", 'O', IsLive: true),
            new NavTool("mapbrowser", "Dual-Write Map Browser", 'D'),
            new NavTool("compare", "Dual-Write Compare", 'C'),
            new NavTool("metadata", "Metadata Browser", 'M'),
            new NavTool("virtualtables", "Virtual Tables", 'V'),
            new NavTool("post", "POST Builder", 'P'),
            new NavTool("profiles", "Profiles", 'E'),
        };

        Environments = new ObservableCollection<EnvProfile>(_profileStore.GetAll());

        _currentTool = Tools[0];
        _activeEnvironment = Environments.FirstOrDefault(e => e.Id == _profileStore.ActiveId)
            ?? Environments.FirstOrDefault();
        Palette = new CommandPaletteViewModel(Tools, NavigateTo);
        _currentContent = ResolveContent(_currentTool);
    }

    private void NavigateTo(NavTool tool)
    {
        CurrentTool = tool;
        IsCommandPaletteOpen = false;
    }

    // Opens a tool by id from the Plugins home card grid; unknown ids (e.g. the unsigned sample) are
    // ignored rather than navigating to a dead screen.
    private void OpenToolById(string id)
    {
        var tool = Tools.FirstOrDefault(t => t.Id == id);
        if (tool is not null)
        {
            NavigateTo(tool);
        }
    }

    partial void OnCurrentToolChanged(NavTool value) => CurrentContent = ResolveContent(value);

    // Keep the (cached) Plugins-home subtitle in sync with the active environment.
    partial void OnActiveEnvironmentChanged(EnvProfile? value)
    {
        if (_homeContent is PluginsHomeViewModel home)
        {
            home.EnvName = value?.Name;
        }
    }

    private object ResolveContent(NavTool tool) => tool.Id switch
    {
        "home" => _homeContent ??= new PluginsHomeViewModel(new BuiltInToolCatalog(), ActiveEnvironment?.Name, OpenToolById),
        "ops" => _operationsContent ??= _operationsContentFactory(),
        "profiles" => _profilesContent ??= CreateProfilesContent(),
        "metadata" => _metadataContent ??= new MetadataViewModel(_metadataService),
        "virtualtables" => _virtualTablesContent ??= new VirtualTablesViewModel(_virtualTableReader, () => ActiveEnvironment, _launcher),
        "post" => _postContent ??= new PostBuilderViewModel(_odataClient, _clipboard, _metadataService, _dialogs),
        "query" => _queryContent ??= new QueryBuilderViewModel(_metadataService, _odataClient, _clipboard, _fileSave),
        "mapbrowser" => _mapBrowserContent ??= new DualWriteMapViewModel(_mapReader, _fileSave, _odataClient, _metadataService, () => ActiveEnvironment, _clipboard, _launcher),
        "compare" => _compareContent ??= new DualWriteCompareViewModel(_profileStore, _compareService),
        _ => new PlaceholderScreenViewModel(tool.Title),
    };

    // Profiles shares the shell's single IProfileStore; activating or editing a profile there keeps
    // the shell's environment switcher in sync.
    private ProfilesViewModel CreateProfilesContent()
    {
        var profiles = new ProfilesViewModel(_profileStore, _secretStore, _authBroker, _authService, _gatewayTester, _connectionTester);
        profiles.ActiveChanged += id =>
        {
            var match = Environments.FirstOrDefault(e => e.Id == id);
            if (match is not null)
            {
                _ = ApplyActiveEnvironmentSwitchAsync(match);
            }
        };
        profiles.ProfileSaved += updated =>
        {
            var existing = Environments.FirstOrDefault(e => e.Id == updated.Id);
            if (existing is not null)
            {
                Environments[Environments.IndexOf(existing)] = updated;
            }
            else
            {
                Environments.Add(updated); // a newly added profile joins the switcher
            }

            if (ActiveEnvironment?.Id == updated.Id)
            {
                ActiveEnvironment = updated; // refresh the header / home subtitle with the new name
            }
        };
        profiles.ProfileDeleted += id =>
        {
            var existing = Environments.FirstOrDefault(e => e.Id == id);
            if (existing is not null)
            {
                Environments.Remove(existing);
            }

            if (ActiveEnvironment?.Id != id)
            {
                return; // a non-active profile went away — the switcher list is all that changes.
            }

            var replacement = Environments.FirstOrDefault(); // active env removed → pick another/none
            ActiveEnvironment = replacement;

            // Deleting the active profile made the store clear the persisted default, so the replacement
            // has to be written back or the next launch starts with no active environment. When nothing
            // is left (last profile deleted) the null the store already wrote is the correct answer.
            if (replacement is not null)
            {
                _profileStore.ActiveId = replacement.Id;
            }

            // A deliberate switch asks before discarding open tool state; a deletion does not. Whatever
            // those tools are showing belongs to an environment that no longer exists, so there is no
            // unsaved input worth protecting — rebuild them unconditionally against the replacement.
            InvalidateToolContent();
        };
        return profiles;
    }

    // Design-mode default: connects via the seeded fake connector (real wiring passes a
    // CoreDualWriteConnector + the shell's active-environment accessor from App.axaml.cs).
    private object DefaultOperationsContent() =>
        new DualWriteOpsViewModel(new FakeDualWriteConnector(), () => ActiveEnvironment, new DialogService(),
            odata: _odataClient, metadata: _metadataService);

    [RelayCommand]
    private void OpenCommandPalette()
    {
        Palette.Reset();
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    [RelayCommand]
    private Task SetActiveEnvironment(EnvProfile? env) => ApplyActiveEnvironmentSwitchAsync(env);

    // The single funnel for a deliberate active-environment switch (header switcher OR Profiles' "Set
    // active"). Profile rename/delete update ActiveEnvironment directly and intentionally bypass this —
    // they aren't switches, so they must not raise this prompt (rename must not discard open tool state
    // at all; deletion refreshes unconditionally — see the ProfileDeleted handler). The switch is
    // all-or-nothing: it moves the shell AND persists the choice, or it moves neither and reports why.
    // Only once it has committed is refreshing the open tools (which discards their unsaved input)
    // offered, gated behind a confirm prompt.
    private async Task ApplyActiveEnvironmentSwitchAsync(EnvProfile? target)
    {
        if (target is null)
        {
            return;
        }

        var previous = ActiveEnvironment;

        // Persisting the active id is part of the switch, not a side effect of it: either the shell and the
        // store both move to the target or neither does. A store that rejects the write (a locked
        // profile.db) previously left the header on the new environment, the tools on the old one and
        // nothing persisted — a half-switched shell that lies about where it is pointing, with only a trace
        // line to show for it. Roll the in-memory switch back and say so instead.
        ActiveEnvironment = target;
        try
        {
            _profileStore.ActiveId = target.Id;
        }
        catch (Exception ex)
        {
            ActiveEnvironment = previous;
            ReportBackgroundFailure(
                $"Couldn't switch environment — the profile store rejected the write: {ex.Message}");
            System.Diagnostics.Trace.TraceWarning(
                $"Switching to '{target.Name}' was rolled back: the profile store rejected the active-id write: {ex}");
            return; // no prompt, no invalidate — the switch did not happen.
        }

        if (previous is null || previous.Id == target.Id)
        {
            return; // first selection, or re-selecting the current one — nothing to refresh.
        }

        // Best-effort from here on: this also runs from the fire-and-forget ActiveChanged handler, so a
        // dialog dying with the window mid-prompt must surface as a trace warning, not an unobserved
        // exception the dispatcher later rethrows. The switch itself is already committed at both levels,
        // so losing the prompt only costs the tool refresh.
        try
        {
            var refresh = await _dialogs.ConfirmAsync(new ConfirmRequest(
                Title: "Active environment changed",
                Message: $"Switched to '{target.Name}'. Refresh open tools so they use this environment? Unsaved input in those tools will be discarded.",
                Targets: Array.Empty<string>(),
                ConfirmLabel: "Refresh tools",
                IsDanger: false));

            if (refresh)
            {
                // Rebuild the open data tool so its cached entities/metadata/results reflect the new environment.
                InvalidateToolContent();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning(
                $"Switched to '{target.Name}', but the refresh prompt did not complete cleanly: {ex}");
        }
    }

    // Drops the cached data-tool view-models so they rebuild against the active environment on next view.
    // Home (just a subtitle) and Profiles (owns the switcher + its event subscriptions) are preserved.
    private void InvalidateToolContent()
    {
        // Dispose any discarded tool VM that owns unmanaged/IDisposable state (e.g. the Operations VM's
        // live gateway HttpClient) before dropping it — GC alone won't close those sockets promptly.
        (_operationsContent as IDisposable)?.Dispose();
        (_metadataContent as IDisposable)?.Dispose();
        (_postContent as IDisposable)?.Dispose();
        (_queryContent as IDisposable)?.Dispose();
        (_mapBrowserContent as IDisposable)?.Dispose();
        (_compareContent as IDisposable)?.Dispose();
        (_virtualTablesContent as IDisposable)?.Dispose();

        _operationsContent = null;
        _metadataContent = null;
        _postContent = null;
        _queryContent = null;
        _mapBrowserContent = null;
        _compareContent = null;
        _virtualTablesContent = null;
        CurrentContent = ResolveContent(CurrentTool);
    }
}
