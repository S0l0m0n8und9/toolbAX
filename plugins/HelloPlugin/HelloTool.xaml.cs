using System.Windows.Controls;

namespace HelloPlugin;

public partial class HelloTool : UserControl
{
    public HelloTool(HelloToolViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
