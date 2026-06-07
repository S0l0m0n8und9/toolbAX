using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Models;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

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
    private object? _operationsContent;
    private object? _profilesContent;
    private object? _metadataContent;
    private object? _postContent;
    private object? _queryContent;
    private object? _mapBrowserContent;
    private object? _compareContent;
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
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

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
        IDualWriteCompareService? compareService = null)
    {
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

        Tools = new[]
        {
            new NavTool("home", "Plugins", '\0'),
            new NavTool("query", "Query Builder", 'Q'),
            new NavTool("ops", "Dual-Write Operations", 'O', IsLive: true),
            new NavTool("mapbrowser", "Dual-Write Map Browser", 'D'),
            new NavTool("compare", "Dual-Write Compare", 'C'),
            new NavTool("metadata", "Metadata Browser", 'M'),
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
        // TODO: design-mode FakePluginCatalog — swap for the live IPluginCatalog once available.
        "home" => _homeContent ??= new PluginsHomeViewModel(new FakePluginCatalog(), ActiveEnvironment?.Name, OpenToolById),
        "ops" => _operationsContent ??= _operationsContentFactory(),
        "profiles" => _profilesContent ??= CreateProfilesContent(),
        "metadata" => _metadataContent ??= new MetadataViewModel(_metadataService),
        "post" => _postContent ??= new PostBuilderViewModel(_odataClient, _clipboard, _metadataService),
        "query" => _queryContent ??= new QueryBuilderViewModel(_metadataService, _odataClient, _clipboard, _fileSave),
        "mapbrowser" => _mapBrowserContent ??= new DualWriteMapViewModel(_mapReader, _fileSave, _odataClient, _metadataService),
        "compare" => _compareContent ??= new DualWriteCompareViewModel(_profileStore, _compareService),
        _ => new PlaceholderScreenViewModel(tool.Title),
    };

    // Profiles shares the shell's single IProfileStore; activating or editing a profile there keeps
    // the shell's environment switcher in sync.
    private ProfilesViewModel CreateProfilesContent()
    {
        var profiles = new ProfilesViewModel(_profileStore, _secretStore, _authBroker, _authService, _gatewayTester);
        profiles.ActiveChanged += id =>
        {
            var match = Environments.FirstOrDefault(e => e.Id == id);
            if (match is not null)
            {
                ActiveEnvironment = match;
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

            if (ActiveEnvironment?.Id == id)
            {
                ActiveEnvironment = Environments.FirstOrDefault(); // active env removed → pick another/none
            }
        };
        return profiles;
    }

    // Design-mode default: connects via the seeded fake connector (real wiring passes a
    // CoreDualWriteConnector + the shell's active-environment accessor from App.axaml.cs).
    private object DefaultOperationsContent() =>
        new DualWriteOpsViewModel(new FakeDualWriteConnector(), () => ActiveEnvironment, new DialogService());

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;

    [RelayCommand]
    private void OpenCommandPalette()
    {
        Palette.Reset();
        IsCommandPaletteOpen = true;
    }

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    [RelayCommand]
    private void SetActiveEnvironment(EnvProfile? env)
    {
        if (env is not null)
        {
            ActiveEnvironment = env;
            _profileStore.ActiveId = env.Id;
        }
    }
}
