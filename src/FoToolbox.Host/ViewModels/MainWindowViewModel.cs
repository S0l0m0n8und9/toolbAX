using FoToolbox.Host.Plugins;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;

namespace FoToolbox.Host.ViewModels;

internal sealed class PluginEntry
{
    public required string Name { get; init; }
    public required UserControl Control { get; init; }
    public required LoadedPlugin Loaded { get; init; }
}

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public ObservableCollection<PluginEntry> Plugins { get; } = new();

    private PluginEntry? _selected;
    public PluginEntry? Selected
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActiveControl));
            }
        }
    }

    public UserControl? ActiveControl => Selected?.Control;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadPlugins(IEnumerable<LoadedPlugin> plugins)
    {
        Plugins.Clear();
        foreach (var plugin in plugins)
        {
            Plugins.Add(new PluginEntry
            {
                Name = plugin.Manifest.Name,
                Control = plugin.ToolControl,
                Loaded = plugin
            });
        }

        Selected = Plugins.FirstOrDefault();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
