using System.Windows.Controls;

namespace TableEntityBrowserPlugin;

public partial class TableEntityBrowserView : UserControl
{
    public TableEntityBrowserView(TableEntityBrowserViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
