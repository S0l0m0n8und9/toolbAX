using System.Linq;
using ToolBax.App;
using Xunit;
using static Avalonia.Win32CompositionMode;

namespace ToolBax.App.Tests;

/// <summary>
/// The <c>TOOLBAX_COMPOSITION</c> escape hatch (#212). The builder wiring itself cannot be exercised headlessly
/// — the headless platform never loads the Win32 backend — so the part worth testing is the pure mapping from
/// one environment-variable value to a composition fallback list, and above all that no value can make it
/// throw: this is read while the app builder is being configured, where an exception is a startup death with
/// no window and nothing on screen to explain it.
/// <para>
/// Expectations are written as the joined mode names rather than enum arrays so that a failure names the modes
/// in order ("WinUIComposition &gt; ..." vs "LowLatencyDxgiSwapChain &gt; ...") instead of printing two
/// collections and leaving the reader to diff them.
/// </para>
/// </summary>
public class CompositionPreferenceTests
{
    private const string DefaultModes = "LowLatencyDxgiSwapChain > RedirectionSurface";
    private const string WinUiModes = "WinUIComposition > DirectComposition > RedirectionSurface";

    private static string Joined(CompositionPreference preference) =>
        string.Join(" > ", preference.Modes.Select(mode => mode.ToString()));

    public static TheoryData<string?, string> Tokens => new()
    {
        // The three documented values.
        { "winui", WinUiModes },
        { "dxgi", DefaultModes },
        { "surface", "RedirectionSurface" },

        // Case and surrounding whitespace are the two things a user typing an env var gets wrong by accident,
        // and neither is a mistake worth punishing.
        { "WinUI", WinUiModes },
        { "DXGI", DefaultModes },
        { " Surface ", "RedirectionSurface" },
        { "\tdxgi\r\n", DefaultModes },

        // Everything below is "not a value we know", which is the default and never an exception: unset, set
        // but blank, misspelt, a mode name that exists in Avalonia but is not offered here, and a list — the
        // hatch takes one token, so comma syntax is a typo like any other.
        { null, DefaultModes },
        { "", DefaultModes },
        { "   ", DefaultModes },
        { "wunui", DefaultModes },
        { "DirectComposition", DefaultModes },
        { "opengl", DefaultModes },
        { "dxgi,surface", DefaultModes },
        { "true", DefaultModes },
        { "0", DefaultModes },
    };

    [Theory]
    [MemberData(nameof(Tokens))]
    public void Resolve_maps_each_token_to_its_composition_fallback_list(string? token, string expected)
    {
        Assert.Equal(expected, Joined(CompositionPreference.Resolve(token)));
    }

    [Theory]
    [MemberData(nameof(Tokens))]
    public void Every_resolved_preference_ends_in_the_redirection_surface_so_no_machine_is_left_without_one(
        string? token,
        string expected)
    {
        _ = expected;   // the same table, read for a different invariant

        // Avalonia throws when no entry in the list matches the machine, and RedirectionSurface is the one
        // mode documented to work everywhere. Whatever route through Resolve a value takes, it must end there.
        Assert.Equal(RedirectionSurface, CompositionPreference.Resolve(token).Modes.Last());
    }

    [Theory]
    [InlineData("dxgi", "dxgi")]
    [InlineData("WinUI", "winui")]
    [InlineData(" surface ", "surface")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("nonsense", null)]
    public void Resolve_credits_the_environment_only_when_it_actually_honoured_the_value(
        string? token,
        string? expectedSource)
    {
        // An ignored value must not read as an override in the log, or the next person to see the hang goes
        // looking for a machine-specific setting that was never in force.
        var preference = CompositionPreference.Resolve(token);

        Assert.Equal(expectedSource, preference.Source);
        Assert.Equal(expectedSource is not null, preference.FromEnvironment);
    }

    [Fact]
    public void The_default_drops_the_WinUIComposition_backend_that_wedged()
    {
        // The whole point of #212: WinUIComposition is the component that deadlocked, and this app uses none
        // of what it offers over the alternatives.
        Assert.DoesNotContain(WinUIComposition, CompositionPreference.Default.Modes);
        Assert.Equal(LowLatencyDxgiSwapChain, CompositionPreference.Default.Modes[0]);
    }

    [Fact]
    public void Winui_restores_the_pre_fix_order_so_the_hatch_can_undo_the_change_entirely()
    {
        // Avalonia 12's own documented default: WinUIComposition, DirectComposition, RedirectionSurface.
        Assert.Equal(WinUiModes, Joined(CompositionPreference.Resolve("winui")));
    }

    [Theory]
    [InlineData(null, "LowLatencyDxgiSwapChain > RedirectionSurface (default)")]
    [InlineData("dxgi", "LowLatencyDxgiSwapChain > RedirectionSurface (TOOLBAX_COMPOSITION=dxgi)")]
    [InlineData("WinUI", "WinUIComposition > DirectComposition > RedirectionSurface (TOOLBAX_COMPOSITION=winui)")]
    [InlineData("garbage", "LowLatencyDxgiSwapChain > RedirectionSurface (default)")]
    public void Describe_names_the_modes_in_priority_order_and_where_the_choice_came_from(
        string? token,
        string expected)
    {
        // This string is what lands in the session-log header, so it is the only record a bug report carries
        // of which composition backend the frozen session was actually pointed at.
        Assert.Equal(expected, CompositionPreference.Resolve(token).Describe());
    }
}
