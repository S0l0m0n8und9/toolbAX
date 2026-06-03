using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FoToolbox.UiTests.Infrastructure;
using Xunit;

namespace FoToolbox.UiTests.Infrastructure;

public class OffscreenHostTests
{
    private sealed class Model { public string Title { get; set; } = "hello"; }

    [WpfFact]
    public void Mount_evaluates_bindings_against_the_data_context()
    {
        var text = new TextBlock { DataContext = new Model() };
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(Model.Title)));

        using var host = OffscreenHost.Mount(text);

        Assert.Equal("hello", text.Text);
    }
}
