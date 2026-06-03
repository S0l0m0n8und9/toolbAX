using System.Threading.Tasks;
using System.Windows;
using FoToolbox.Host.Plugins;
using FoToolbox.Host.Views;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests;

public class ViewWiringTests
{
    // Case names are strings (serializable) so xUnit gets one discoverable test per view
    // and no xUnit1045 non-serializable-data warning (which CI treats as an error).
    public static TheoryData<string> ViewCaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ViewRegistry.All.Keys)
        {
            data.Add(name);
        }
        return data;
    }

    [WpfTheory]
    [MemberData(nameof(ViewCaseNames))]
    public async Task View_constructs_and_has_no_binding_errors(string caseName)
    {
        var view = ViewRegistry.All[caseName];

        using var scope = new BindingErrorScope();
        var control = await view.Factory();       // construct + lifecycle (throws => fail)
        using var host = OffscreenHost.Mount(control);
        view.WarmUp?.Invoke(control.DataContext);  // optional seeded-data load
        host.PumpToIdle();

        Assert.True(
            scope.Errors.Count == 0,
            $"'{caseName}' produced {scope.Errors.Count} binding error(s):\n" + string.Join("\n", scope.Errors));
    }

    [WpfFact]
    public void PluginConsentWindow_constructs_with_no_binding_errors()
    {
        using var scope = new BindingErrorScope();

        var window = new PluginConsentWindow(
            new PluginConsentRequest("Demo.Plugin.dll", @"C:\plugins\Demo.Plugin.dll", "abc123def456"));

        // Reparent the window content so it can be measured/bound offscreen without Show().
        var content = (FrameworkElement)window.Content;
        window.Content = null;
        using var host = OffscreenHost.Mount(content);
        host.PumpToIdle();

        Assert.True(scope.Errors.Count == 0, string.Join("\n", scope.Errors));
    }
}
