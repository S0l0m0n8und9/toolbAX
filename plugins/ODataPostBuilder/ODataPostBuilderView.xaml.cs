using System.Windows.Controls;

namespace ODataPostBuilderPlugin;

public partial class ODataPostBuilderView : UserControl
{
    public ODataPostBuilderView(ODataPostBuilderViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}

