using System;
using System.Threading.Tasks;
using System.Windows;

namespace FoToolbox.UiTests;

/// <summary>
/// One mountable view. <see cref="Factory"/> builds the control through its real
/// production lifecycle. <see cref="WarmUp"/> is an optional hook to trigger a primary
/// load command so seeded data flows into item/data-template bindings; most cases leave
/// it null and rely on constructor/InitializeAsync loads settling during the pump.
/// </summary>
internal sealed record ViewCase(
    string Name,
    Func<Task<FrameworkElement>> Factory,
    Action<object?>? WarmUp = null)
{
    public override string ToString() => Name;
}
