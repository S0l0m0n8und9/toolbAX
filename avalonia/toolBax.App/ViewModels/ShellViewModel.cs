using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Models;

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

    [ObservableProperty]
    private NavTool _currentTool;

    [ObservableProperty]
    private EnvProfile _activeEnvironment;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPaneOpen = true;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    public ShellViewModel()
    {
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

        Environments = new ObservableCollection<EnvProfile>
        {
            new("usmf", "Contoso USMF", "USMF", EnvStatus.Connected),
            new("uat", "Contoso UAT", "USMF", EnvStatus.TokenExpired),
            new("dev", "Contoso Dev", "DAT", EnvStatus.Disconnected),
        };

        _currentTool = Tools[0];
        _activeEnvironment = Environments[0];
        Palette = new CommandPaletteViewModel(Tools, NavigateTo);
    }

    private void NavigateTo(NavTool tool)
    {
        CurrentTool = tool;
        IsCommandPaletteOpen = false;
    }

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
