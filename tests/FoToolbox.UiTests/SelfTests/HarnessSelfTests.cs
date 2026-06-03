using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests.SelfTests;

public class HarnessSelfTests
{
    private sealed class Model { public string Title { get; set; } = "ok"; }

    [WpfFact]
    public void Scope_catches_a_broken_binding_path()
    {
        var text = new TextBlock { DataContext = new Model() };
        // "Nope" does not exist on Model => WPF emits a data-binding error.
        text.SetBinding(TextBlock.TextProperty, new Binding("Nope"));

        using var scope = new BindingErrorScope();
        using var host = OffscreenHost.Mount(text);
        host.PumpToIdle();

        Assert.NotEmpty(scope.Errors);
    }

    [WpfFact]
    public void Scope_reports_no_errors_for_a_correct_binding()
    {
        var text = new TextBlock { DataContext = new Model() };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Title)));

        using var scope = new BindingErrorScope();
        using var host = OffscreenHost.Mount(text);
        host.PumpToIdle();

        Assert.Empty(scope.Errors);
    }
}
