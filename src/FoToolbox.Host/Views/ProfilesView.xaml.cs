using FoToolbox.Host.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace FoToolbox.Host.Views;

internal partial class ProfilesView : UserControl
{
    private bool _initialized;

    internal ProfilesView(ProfilesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        Loaded += (_, __) =>
        {
            if (_initialized) return;
            _initialized = true;
            vm.RefreshCommand.Execute(null);
        };

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ProfilesViewModel.Selected))
            {
                FoClientSecretBox.Password = string.Empty;
                FoBearerTokenBox.Password = string.Empty;
                CeClientSecretBox.Password = string.Empty;
                CeBearerTokenBox.Password = string.Empty;
            }
        };

        FoAuthModeComboBox.SelectionChanged += (_, __) =>
        {
            FoClientSecretBox.Password = string.Empty;
            FoBearerTokenBox.Password = string.Empty;
        };

        CeAuthModeComboBox.SelectionChanged += (_, __) =>
        {
            CeClientSecretBox.Password = string.Empty;
            CeBearerTokenBox.Password = string.Empty;
        };

        FoClientSecretBox.PasswordChanged += (_, __) =>
        {
            vm.PendingFoClientSecret = FoClientSecretBox.Password;
        };

        FoBearerTokenBox.PasswordChanged += (_, __) =>
        {
            vm.PendingFoBearerToken = FoBearerTokenBox.Password;
        };

        CeClientSecretBox.PasswordChanged += (_, __) =>
        {
            vm.PendingCeClientSecret = CeClientSecretBox.Password;
        };

        CeBearerTokenBox.PasswordChanged += (_, __) =>
        {
            vm.PendingCeBearerToken = CeBearerTokenBox.Password;
        };
    }
}
