using FoToolbox.Host.ViewModels;
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

        ClientSecretBox.PasswordChanged += (_, __) =>
        {
            vm.PendingClientSecret = ClientSecretBox.Password;
        };
    }
}
