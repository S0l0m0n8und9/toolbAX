using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Design-system guardrails: the shared tokens (spacing scale, typography ramp, font families) must be
/// registered and well-formed so screens can rely on them and a renamed/dropped token fails fast. These
/// codify the rules from docs/design_handoff_avalonia/design-tokens.md.
/// </summary>
public class DesignSystemTests
{
    private static object Resource(string key)
    {
        // Use the app's actual theme variant rather than hardcoding one, so the lookup can't miss a
        // variant-scoped registration (the tokens are variant-neutral today, but this stays correct
        // if that changes).
        Assert.True(
            Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var value),
            $"Design token '{key}' is not registered.");
        Assert.NotNull(value);
        return value!;
    }

    [AvaloniaFact]
    public void Spacing_scale_tokens_are_registered_on_the_4px_grid()
    {
        Assert.Equal(4d, Resource("Space1"));
        Assert.Equal(8d, Resource("Space2"));
        Assert.Equal(12d, Resource("Space3"));
        Assert.Equal(16d, Resource("Space4"));
        Assert.Equal(24d, Resource("Space5"));
    }

    [AvaloniaFact]
    public void Typography_ramp_is_registered_with_an_11px_floor()
    {
        var ramp = new (string Key, double Size)[]
        {
            ("FontSizeTitle", 24d),
            ("FontSizeCardHeader", 14d),
            ("FontSizeBody", 13d),
            ("FontSizeCaption", 12d),
            ("FontSizeEyebrow", 11d),
        };

        foreach (var (key, size) in ramp)
        {
            var value = Assert.IsType<double>(Resource(key));
            Assert.Equal(size, value);
            Assert.True(value >= 11d, $"{key} must be >= 11px (no sub-11px text).");
        }
    }

    [AvaloniaFact]
    public void Font_family_tokens_are_registered()
    {
        Assert.IsType<FontFamily>(Resource("MonoFontFamily"));
        Assert.IsType<FontFamily>(Resource("UiFontFamily"));
    }
}
