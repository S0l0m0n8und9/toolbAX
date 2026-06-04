using System.Windows.Controls;
using Xunit;

namespace FoToolbox.UiTests;

public class SmokeTests
{
    [WpfFact]
    public void Wpf_controls_can_be_constructed_on_the_test_thread()
    {
        var button = new Button { Content = "ok" };
        Assert.Equal("ok", button.Content);
    }
}
