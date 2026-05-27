using System.ComponentModel;

namespace FoToolbox.SDK.Plugins;

/// <summary>
/// Optional opt-in for plugins that want their busy state surfaced in the host status bar.
/// Plugins that do not implement this interface are treated as idle by the shell.
/// </summary>
public interface IPluginBusyState : INotifyPropertyChanged
{
    bool IsBusy { get; }
}
