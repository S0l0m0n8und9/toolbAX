using FoToolbox.Host.Plugins;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using FoToolbox.Updater;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace FoToolbox.Host.ViewModels;

internal sealed class PluginEntry
{
    public required string Name { get; init; }
    public required UserControl Control { get; init; }
    public LoadedPlugin? Loaded { get; init; }
}

internal sealed class MainWindowViewModel : INotifyPropertyChanged
{
    public ObservableCollection<PluginEntry> Plugins { get; } = new();

    public string UpdateStatus
    {
        get => _updateStatus;
        private set
        {
            _updateStatus = value;
            OnPropertyChanged();
        }
    }

    public string UpdateChannel { get; }
    public string ManifestUrl { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand ApplyUpdateCommand { get; }
    public ICommand RollbackUpdateCommand { get; }

    private PluginEntry? _selected;
    private string _updateStatus = "Updates not checked.";
    private string? _stagedUpdatePath;

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
    public string? StagedUpdatePath
    {
        get => _stagedUpdatePath;
        private set
        {
            _stagedUpdatePath = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel()
    {
        UpdateChannel = Environment.GetEnvironmentVariable("FOTOOLBOX_UPDATE_CHANNEL") ?? "stable";
        ManifestUrl = Environment.GetEnvironmentVariable("FOTOOLBOX_UPDATE_MANIFEST") ?? string.Empty;
        CheckUpdatesCommand = new AsyncCommand(CheckUpdatesAsync);
        ApplyUpdateCommand = new AsyncCommand(ApplyUpdateAsync);
        RollbackUpdateCommand = new AsyncCommand(RollbackUpdateAsync);
    }

    public void LoadPlugins(IEnumerable<LoadedPlugin> plugins, UserControl? profilesControl = null)
    {
        Plugins.Clear();

        if (profilesControl is not null)
        {
            Plugins.Add(new PluginEntry
            {
                Name = "Profiles",
                Control = profilesControl
            });
        }

        foreach (var plugin in plugins)
        {
            Plugins.Add(new PluginEntry
            {
                Name = plugin.Manifest.Name,
                Control = plugin.ToolControl,
                Loaded = plugin
            });
        }

        Selected = Plugins.FirstOrDefault(p => p.Loaded is not null) ?? Plugins.FirstOrDefault();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private async Task CheckUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(ManifestUrl))
        {
            UpdateStatus = "Set FOTOOLBOX_UPDATE_MANIFEST to enable update checks.";
            return;
        }

        try
        {
            UpdateStatus = "Checking for updates...";
            var channel = new UpdateChannelConfig(UpdateChannel, new Uri(ManifestUrl));
            using var http = new HttpClient();
            var fetcher = new ResilientUpdateFetcher(new HttpUpdateFetcher(http));
            var loader = new UpdateManifestLoader(fetcher);
            var updater = new UpdaterClient(fetcher, Path.Combine(AppContext.BaseDirectory, "updates"));
            var orchestrator = new UpdateOrchestrator(loader, updater, channel);

            var staged = await orchestrator.CheckAndStageAsync();
            if (!string.IsNullOrEmpty(staged))
            {
                StagedUpdatePath = staged;
                UpdateStatus = $"Update staged: {Path.GetFileName(staged)}";
            }
            else
            {
                UpdateStatus = "No updates available.";
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update check failed: {ex.Message}";
        }
    }

    private Task ApplyUpdateAsync()
    {
        if (string.IsNullOrWhiteSpace(StagedUpdatePath))
        {
            UpdateStatus = "No staged update to apply.";
            return Task.CompletedTask;
        }

        if (!File.Exists(StagedUpdatePath))
        {
            UpdateStatus = "Staged file missing. Re-run check.";
            return Task.CompletedTask;
        }

        try
        {
            if (!ValidateSignatureIfConfigured(StagedUpdatePath))
            {
                UpdateStatus = "Update signature check failed; aborting.";
                return Task.CompletedTask;
            }

            UpdateStatus = "Launching installer...";
            Process.Start(new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = $"/i \"{StagedUpdatePath}\" /qb!",
                UseShellExecute = true
            });
            UpdateStatus = "Installer launched. Follow prompts to complete update.";
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Failed to launch installer: {ex.Message}";
        }
        return Task.CompletedTask;
    }

    private Task RollbackUpdateAsync()
    {
        UpdateStatus = "Rollback not implemented yet.";
        return Task.CompletedTask;
    }

    private bool ValidateSignatureIfConfigured(string path)
    {
        var expected = Environment.GetEnvironmentVariable("FOTOOLBOX_UPDATE_SIGNER_THUMBPRINT");
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true; // no policy configured
        }

        expected = expected.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        try
        {
            var cert = new X509Certificate2(path);
            var thumb = (cert.Thumbprint ?? string.Empty).Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            if (!string.Equals(thumb, expected, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStatus = $"Staged package signer thumbprint {thumb} does not match expected {expected}.";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Signature validation failed: {ex.Message}";
            return false;
        }
    }
}

internal sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _execute;
    public AsyncCommand(Func<Task> execute) => _execute = execute;
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute();
}
