using System.Windows.Controls;

namespace DualWriteMapBrowserPlugin;

public partial class DualWriteMapBrowserView : UserControl
{
    public DualWriteMapBrowserView(DualWriteMapBrowserViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
