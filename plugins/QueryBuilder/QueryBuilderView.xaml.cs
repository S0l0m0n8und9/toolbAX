using System.Windows.Controls;

namespace QueryBuilderPlugin;

public partial class QueryBuilderView : UserControl
{
    public QueryBuilderView(QueryBuilderViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
