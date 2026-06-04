using System.Windows.Controls;

namespace DualWriteOperationsPlugin;

public partial class DualWriteOperationsView : UserControl
{
    public DualWriteOperationsView(DualWriteOperationsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
