using System;
using System.Linq;
using FoToolbox.SDK.Plugins;
using Xunit;

namespace FoToolbox.Tests;

/// <summary>
/// Guards the #33 decoupling: the public plugin contract assembly (<c>FoToolbox.SDK</c>) must not
/// depend on WPF, so an alternate UI host (#35) can load plugins. WPF spans three assemblies, so all
/// three are checked — a stray type from any of them re-introduces the coupling.
/// </summary>
public class SdkDecouplingTests
{
    private static readonly string[] WpfAssemblies =
    {
        "PresentationFramework",
        "PresentationCore",
        "WindowsBase",
    };

    [Fact]
    public void FoToolbox_Sdk_does_not_reference_WPF()
    {
        var sdk = typeof(IFoToolPlugin).Assembly;
        var referenced = sdk.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Where(n => n is not null)
            .ToArray();

        var wpfRefs = referenced
            .Where(n => WpfAssemblies.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(
            wpfRefs.Length == 0,
            $"FoToolbox.SDK must not reference WPF, but references: {string.Join(", ", wpfRefs)}.");
    }
}
