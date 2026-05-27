using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using FoToolbox.SDK.Plugins;

namespace FoToolbox.Host.ViewModels;

/// <summary>
/// Cross-cutting shell state surfaced by the title bar and status bar:
/// active profile, aggregate busy, connection status, and last successful ping time.
/// </summary>
internal sealed class AppShellViewModel : INotifyPropertyChanged
{
    private readonly List<IPluginBusyState> _busyPlugins = new();
    private bool _isBusy;
    private string? _activeProfileEnvId;
    private string? _activeProfileName;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Unknown;
    private DateTimeOffset? _lastPingAt;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveProfileEnvId
    {
        get => _activeProfileEnvId;
        private set
        {
            if (_activeProfileEnvId == value) return;
            _activeProfileEnvId = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveProfileName
    {
        get => _activeProfileName;
        private set
        {
            if (_activeProfileName == value) return;
            _activeProfileName = value;
            OnPropertyChanged();
        }
    }

    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        private set
        {
            if (_connectionStatus == value) return;
            _connectionStatus = value;
            OnPropertyChanged();
        }
    }

    public DateTimeOffset? LastPingAt
    {
        get => _lastPingAt;
        private set
        {
            if (_lastPingAt == value) return;
            _lastPingAt = value;
            OnPropertyChanged();
        }
    }

    public event EventHandler? NavigateToProfilesRequested;

    public void RaiseNavigateToProfiles() =>
        NavigateToProfilesRequested?.Invoke(this, EventArgs.Empty);

    public void RegisterPluginBusy(IPluginBusyState busy)
    {
        if (busy is null) return;
        if (_busyPlugins.Contains(busy)) return;
        _busyPlugins.Add(busy);
        busy.PropertyChanged += OnPluginBusyChanged;
        RecomputeIsBusy();
    }

    public void UnregisterPluginBusy(IPluginBusyState busy)
    {
        if (busy is null) return;
        if (!_busyPlugins.Remove(busy)) return;
        busy.PropertyChanged -= OnPluginBusyChanged;
        RecomputeIsBusy();
    }

    public void SetActiveProfile(string? envId, string? name)
    {
        if (string.Equals(_activeProfileEnvId, envId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_activeProfileName, name, StringComparison.Ordinal))
        {
            return;
        }
        ActiveProfileEnvId = envId;
        ActiveProfileName = name;
        ConnectionStatus = ConnectionStatus.Unknown;
        LastPingAt = null;
    }

    public void OnConnectionTested(ConnectionTestedEventArgs e)
    {
        if (e is null) return;
        if (!string.Equals(_activeProfileEnvId, e.EnvironmentId, StringComparison.OrdinalIgnoreCase))
        {
            // Test result for a profile that isn't the active one - ignore.
            return;
        }

        ConnectionStatus = e.Success ? ConnectionStatus.Ok : ConnectionStatus.Error;
        LastPingAt = e.Success ? e.TestedAt : LastPingAt;
    }

    private void OnPluginBusyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IPluginBusyState.IsBusy))
        {
            RecomputeIsBusy();
        }
    }

    private void RecomputeIsBusy() =>
        IsBusy = _busyPlugins.Any(p => p.IsBusy);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
