using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Plugins home (control-map §1): the landing card grid. Filters the catalogue and, when a card is
/// opened, asks the shell to navigate to that tool via the injected <c>openTool</c> callback.
/// </summary>
public partial class PluginsHomeViewModel : ObservableObject
{
    private readonly Action<string>? _openTool;

    public ObservableCollection<PluginCard> Plugins { get; }

    /// <summary>Active environment name shown in the subtitle; updated by the shell on env switch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnv))]
    private string? _envName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilteredPlugins))]
    private string _filter = string.Empty;

    /// <summary>Whether to show the "Connected to {env}" subtitle clause.</summary>
    public bool HasEnv => !string.IsNullOrWhiteSpace(EnvName);

    public PluginsHomeViewModel(IPluginCatalog catalog, string? envName = null, Action<string>? openTool = null)
    {
        _openTool = openTool;
        _envName = envName;
        Plugins = new ObservableCollection<PluginCard>(catalog.Plugins);
    }

    public IEnumerable<PluginCard> FilteredPlugins =>
        string.IsNullOrWhiteSpace(Filter)
            ? Plugins
            : Plugins.Where(p =>
                p.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase) ||
                p.Description.Contains(Filter, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private void OpenPlugin(string? id)
    {
        if (!string.IsNullOrEmpty(id))
        {
            _openTool?.Invoke(id);
        }
    }
}
