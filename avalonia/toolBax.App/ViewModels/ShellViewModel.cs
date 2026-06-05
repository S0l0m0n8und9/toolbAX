using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private object? _operationsContent;
    private object? _profilesContent;

    [ObservableProperty]
    private NavTool _currentTool;

    /// <summary>The active screen VM the content host renders (routed from <see cref="CurrentTool"/>).</summary>
    [ObservableProperty]
    private object? _currentContent;

    [ObservableProperty]
    private EnvProfile _activeEnvironment;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    public ShellViewModel(Func<object>? operationsContentFactory = null)
    {
        _operationsContentFactory = operationsContentFactory ?? DefaultOperationsContent;

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

        Environments = new ObservableCollection<EnvProfile>(FakeProfileStore.Seed());

        _currentTool = Tools[0];
        _activeEnvironment = Environments[0];
        Palette = new CommandPaletteViewModel(Tools, NavigateTo);
        _currentContent = ResolveContent(_currentTool);
    }

    private void NavigateTo(NavTool tool)
    {
        CurrentTool = tool;
        IsCommandPaletteOpen = false;
    }

    partial void OnCurrentToolChanged(NavTool value) => CurrentContent = ResolveContent(value);

    private object ResolveContent(NavTool tool) => tool.Id switch
    {
        "ops" => _operationsContent ??= _operationsContentFactory(),
        "profiles" => _profilesContent ??= new ProfilesViewModel(new FakeProfileStore()),
        _ => new PlaceholderScreenViewModel(tool.Title),
    };

    // TODO: design-mode only — wired to FakeDualWriteGateway with prototype seed data. Replace with
    // the live IDualWriteGateway (resolving the gateway + loading maps async) once it's implemented;
    // do not ship the fake as the default.
    private static object DefaultOperationsContent() => new DualWriteOpsViewModel(
        new FakeDualWriteGateway(),
        new DialogService(),
        FakeDualWriteGateway.SeedGateway(),
        FakeDualWriteGateway.SeedMaps());

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
        }
    }
}
