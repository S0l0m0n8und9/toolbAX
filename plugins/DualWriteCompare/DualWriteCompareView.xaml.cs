using System.Windows;
using System.Windows.Controls;

namespace DualWriteComparePlugin;

public partial class DualWriteCompareView : UserControl
{
    public DualWriteCompareView(DualWriteCompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // Bearer tokens are secrets, so they live in PasswordBoxes (not bindable). Each side's
    // box pushes into its editor view-model on edit.
    private void LeftTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is DualWriteCompareViewModel vm && sender is PasswordBox box)
        {
            vm.Left.BearerToken = box.Password;
        }
    }

    private void RightTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is DualWriteCompareViewModel vm && sender is PasswordBox box)
        {
            vm.Right.BearerToken = box.Password;
        }
    }
}
