using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using FoToolbox.Host.Diagnostics;
using FoToolbox.Host.Plugins;
using FoToolbox.Host.ViewModels;
using FoToolbox.Host.Views;
using FoToolbox.Core.Auth;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FoToolbox.Host;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _vm;
    private readonly ILogger _logger;
    private readonly AppBootstrapper _bootstrapper;
    private readonly CancellationTokenSource _cts = new();
    private ProfilesView? _profilesView;
    private bool _loadedOnce;

    internal Action<string, string, MessageBoxButton, MessageBoxImage> ShowMessageBox { get; set; } =
        static (message, title, button, image) => MessageBox.Show(message, title, button, image);

    public MainWindow()
    {
        InitializeComponent();

        AppDiagnostics.Initialize();
        _logger = AppDiagnostics.Logger;

        var profileDbPath = ProfilePaths.ResolveProfileDbPath();
        _bootstrapper = new AppBootstrapper(profileDbPath, _logger);
        _bootstrapper.ReauthCoordinator.ReauthRequired += OnReauthRequired;

        _vm = new MainWindowViewModel();
        DataContext = _vm;

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_loadedOnce) return;
        _loadedOnce = true;
        Loaded -= MainWindow_Loaded;

        try
        {
            await LoadPluginsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed during startup plugin load.");
            MessageBox.Show($"Startup failed: {ex.Message}", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadPluginsAsync()
    {
        var ct = _cts.Token;
        var bundle = await _bootstrapper.ResolveProfileAsync(ct);

        _profilesView ??= new ProfilesView(new ProfilesViewModel(
            ProfilePaths.ResolveProfileDbPath(), _logger, ApplyProfile));
        _vm.ProfilesViewModelHost = (ProfilesViewModel)_profilesView.DataContext;

        if (bundle is null)
        {
            // No profile yet - show the Profiles tab so the user can configure an environment.
            _vm.LoadPlugins(Array.Empty<LoadedPlugin>(), _profilesView);
            return;
        }

        await ApplyProfileAsync(bundle);

        // Kick off a background update check (fire-and-forget).
        _ = _vm.CheckUpdatesAsync();
    }

    private void ApplyProfile(ProfileBundle bundle)
    {
        _ = ApplyProfileAsync(bundle);
    }

    private async Task ApplyProfileAsync(ProfileBundle bundle)
    {
        try
        {
            var result = await _bootstrapper.ApplyProfileAsync(bundle, _cts.Token);
            _vm.Shell.SetActiveProfile(
                bundle.FoEnvironment.Id,
                bundle.FoEnvironment.Name);
            _vm.LoadPlugins(result.Plugins, _profilesView);

            result.NavigationBus.PluginActivationRequested += loaded =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    var entry = _vm.Plugins.FirstOrDefault(p => p.Loaded == loaded);
                    if (entry is not null)
                    {
                        _vm.Selected = entry;
                    }
                });
            };
        }
        catch (AuthRecoveryException ex)
        {
            _logger.LogWarning(ex, "Authentication recovery required for profile {EnvId}", bundle.FoEnvironment.Id);
            ShowReauthPrompt(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply profile {EnvId}", bundle.FoEnvironment.Id);
            MessageBox.Show($"Failed to apply profile: {ex.Message}", "FOtoolbox", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnReauthRequired(AuthRecoveryException exception)
    {
        Dispatcher.Invoke(() => ShowReauthPrompt(exception));
    }

    private void ShowReauthPrompt(AuthRecoveryException exception)
    {
        EnsureProfilesTabVisible();
        if (exception.RequiresInteractiveReauth && _profilesView?.DataContext is ProfilesViewModel profilesViewModel)
        {
            _ = profilesViewModel.BeginInteractiveReauthAsync(exception.ServiceName);
        }

        ShowMessageBox(exception.ReauthMessage, exception.PromptTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        _bootstrapper.ReauthCoordinator.Reset();
    }

    private void EnsureProfilesTabVisible()
    {
        if (_profilesView is null)
        {
            return;
        }

        var profilesEntry = _vm.Plugins.FirstOrDefault(p => string.Equals(p.Name, "Profiles", StringComparison.OrdinalIgnoreCase));
        if (profilesEntry is not null)
        {
            _vm.Selected = profilesEntry;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _bootstrapper.ReauthCoordinator.ReauthRequired -= OnReauthRequired;
        _cts.Cancel();
        _cts.Dispose();
        _bootstrapper.Dispose();
        base.OnClosed(e);
    }
}
