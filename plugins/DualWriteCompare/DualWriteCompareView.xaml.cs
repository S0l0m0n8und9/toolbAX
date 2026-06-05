using System.Windows.Controls;

namespace DualWriteComparePlugin;

public partial class DualWriteCompareView : UserControl
{
    public DualWriteCompareView(DualWriteCompareViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
