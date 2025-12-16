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
                ClientSecretBox.Password = string.Empty;
                BearerTokenBox.Password = string.Empty;
            }
        };

        AuthModeComboBox.SelectionChanged += (_, __) =>
        {
            ClientSecretBox.Password = string.Empty;
            BearerTokenBox.Password = string.Empty;
        };

        ClientSecretBox.PasswordChanged += (_, __) =>
        {
            vm.PendingClientSecret = ClientSecretBox.Password;
        };

        BearerTokenBox.PasswordChanged += (_, __) =>
        {
            vm.PendingBearerToken = BearerTokenBox.Password;
        };
    }
}
