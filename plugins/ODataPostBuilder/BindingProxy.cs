using System.Windows;

namespace ODataPostBuilderPlugin;

/// <summary>
/// Freezable proxy that carries the view's DataContext into places that are not part of
/// the visual tree (e.g. DataGrid column definitions), so their bindings can reach the
/// view model. Freezables propagate the inheritance (data) context even from a resource
/// dictionary, which RelativeSource FindAncestor cannot do for columns.
/// </summary>
internal sealed class BindingProxy : Freezable
{
    protected override Freezable CreateInstanceCore() => new BindingProxy();

    public object? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new PropertyMetadata(null));
}
