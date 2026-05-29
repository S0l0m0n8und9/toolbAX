using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DualWriteOperationsPlugin;

public partial class DualWriteOperationsView : UserControl
{
    private readonly DualWriteOperationsViewModel _viewModel;

    public DualWriteOperationsView(DualWriteOperationsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // The bearer token is a secret, so it lives in a PasswordBox (not bindable). Push
    // changes into the view-model on edit; the box is cleared after a successful save.
    private void BearerTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
        {
            _viewModel.BearerToken = box.Password;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DualWriteOperationsViewModel.BearerToken) &&
            string.IsNullOrEmpty(_viewModel.BearerToken) &&
            BearerTokenBox.Password.Length > 0)
        {
            BearerTokenBox.Clear();
        }
    }
}
