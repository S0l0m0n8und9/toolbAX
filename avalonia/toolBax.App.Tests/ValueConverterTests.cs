using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ToolBax.App.Converters;
using ToolBax.App.ViewModels;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Table-driven coverage for the value converters that had none (#167). A converter is the last hop
/// before pixels: an unmapped enum member silently renders as a raw name or a grey fallback, and nothing
/// but a test per input catches that. Each case list is driven off <c>Enum.GetValues</c> where possible,
/// so adding a member without a mapping fails here rather than in the UI.
/// <para>The brush converters resolve theme resources through <c>Application.Current</c>, so they run as
/// <c>[AvaloniaFact]</c> against the headless app (which loads the real token dictionary).</para>
/// </summary>
public class ValueConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ── FoAuthModeLabelConverter ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(FoAuthMode.Interactive, "Interactive (MFA)")]
    [InlineData(FoAuthMode.ClientSecret, "Client secret")]
    [InlineData(FoAuthMode.Certificate, "Certificate")]
    public void FoAuthModeLabel_maps_each_mode_to_its_friendly_label(FoAuthMode mode, string expected) =>
        Assert.Equal(expected, FoAuthModeLabelConverter.Instance.Convert(mode, typeof(string), null, Culture));

    [Fact]
    public void FoAuthModeLabel_covers_every_mode_with_a_label_that_is_not_the_enum_name()
    {
        foreach (var mode in Enum.GetValues<FoAuthMode>())
        {
            var label = FoAuthModeLabelConverter.Instance.Convert(mode, typeof(string), null, Culture) as string;
            Assert.False(string.IsNullOrWhiteSpace(label), $"{mode} has no label.");
            // Certificate happens to read the same either way; the rest must be humanised.
            if (mode != FoAuthMode.Certificate)
            {
                Assert.NotEqual(mode.ToString(), label);
            }
        }
    }

    [Fact]
    public void FoAuthModeLabel_falls_back_to_the_value_itself_for_a_non_mode()
    {
        // The dropdown binds an object; a null or foreign value must render as empty/its own text, never throw.
        Assert.Equal(string.Empty, FoAuthModeLabelConverter.Instance.Convert(null, typeof(string), null, Culture));
        Assert.Equal("Whatever", FoAuthModeLabelConverter.Instance.Convert("Whatever", typeof(string), null, Culture));
        Assert.Equal("7", FoAuthModeLabelConverter.Instance.Convert(7, typeof(string), null, Culture));
    }

    [Fact]
    public void FoAuthModeLabel_is_one_way() =>
        Assert.Throws<NotSupportedException>(() =>
            FoAuthModeLabelConverter.Instance.ConvertBack("Client secret", typeof(FoAuthMode), null, Culture));

    // ── DiAuthModeLabelConverter ──────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DiAuthMode.Interactive, "Interactive (MFA)")]
    [InlineData(DiAuthMode.Ropc, "ROPC (service account)")]
    public void DiAuthModeLabel_maps_each_mode_to_its_friendly_label(DiAuthMode mode, string expected) =>
        Assert.Equal(expected, DiAuthModeLabelConverter.Instance.Convert(mode, typeof(string), null, Culture));

    [Fact]
    public void DiAuthModeLabel_covers_every_mode_with_a_humanised_label()
    {
        // Adding a DiAuthMode member without a Label() arm surfaces here (the raw enum name) rather than
        // in the Profiles DI dropdown.
        foreach (var mode in Enum.GetValues<DiAuthMode>())
        {
            var label = DiAuthModeLabelConverter.Instance.Convert(mode, typeof(string), null, Culture) as string;
            Assert.False(string.IsNullOrWhiteSpace(label), $"{mode} has no label.");
            Assert.NotEqual(mode.ToString(), label);
        }
    }

    [Fact]
    public void DiAuthModeLabel_falls_back_to_empty_for_a_non_mode()
    {
        // Unlike FoAuthModeLabelConverter (which echoes the value), this one's documented fallback is the
        // empty string: the DI dropdown binds an object, and a null or foreign value renders as blank
        // rather than leaking a raw ToString() — and never throws.
        Assert.Equal(string.Empty, DiAuthModeLabelConverter.Instance.Convert(null, typeof(string), null, Culture));
        Assert.Equal(string.Empty, DiAuthModeLabelConverter.Instance.Convert("Ropc", typeof(string), null, Culture));
        Assert.Equal(string.Empty, DiAuthModeLabelConverter.Instance.Convert(1, typeof(string), null, Culture));
        Assert.Equal(string.Empty, DiAuthModeLabelConverter.Instance.Convert(FoAuthMode.Interactive, typeof(string), null, Culture));
    }

    [Fact]
    public void DiAuthModeLabel_is_one_way() =>
        Assert.Throws<NotSupportedException>(() =>
            DiAuthModeLabelConverter.Instance.ConvertBack("Interactive (MFA)", typeof(DiAuthMode), null, Culture));

    // ── EnvIsActiveConverter (multi-value) ────────────────────────────────────────────────────────────

    private static bool IsActive(params object?[] values) =>
        Assert.IsType<bool>(EnvIsActiveConverter.Instance.Convert(values.ToList(), typeof(bool), null, Culture));

    [Fact]
    public void EnvIsActive_is_true_only_for_the_active_rows_id()
    {
        Assert.True(IsActive("env1", "env1"));
        Assert.False(IsActive("env1", "env2"));
    }

    [Fact]
    public void EnvIsActive_matches_the_id_exactly_not_loosely()
    {
        // Ordinal comparison: two profiles differing only in case are two profiles, not one.
        Assert.False(IsActive("env1", "ENV1"));
        Assert.False(IsActive(" env1", "env1"));
    }

    [Theory]
    // A row with no id must never light up, even against an equally-empty active id — otherwise every
    // unsaved row badges itself as active.
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, "env1")]
    [InlineData("env1", null)]
    [InlineData("", "env1")]
    public void EnvIsActive_is_false_when_either_side_is_missing(string? id, string? activeId) =>
        Assert.False(IsActive(id, activeId));

    [Fact]
    public void EnvIsActive_is_false_when_the_multi_binding_has_not_produced_both_values()
    {
        // A MultiBinding raises Convert with fewer (or unset) values during setup; that is not "active".
        Assert.False(IsActive());
        Assert.False(IsActive("env1"));
    }

    [Fact]
    public void EnvIsActive_is_false_for_non_string_values() =>
        Assert.False(IsActive(1, 1));

    // ── EnvStatusToBrushConverter ─────────────────────────────────────────────────────────────────────

    private static IBrush EnvBrush(object? value) =>
        Assert.IsAssignableFrom<IBrush>(
            EnvStatusToBrushConverter.Instance.Convert(value, typeof(IBrush), null, Culture));

    private static Color EnvColour(object? value) => Assert.IsAssignableFrom<ISolidColorBrush>(EnvBrush(value)).Color;

    private static Color Token(string key)
    {
        var found = Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var brush);
        Assert.True(found, $"Theme token '{key}' is missing.");
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
    }

    [AvaloniaFact]
    public void EnvStatusToBrush_maps_each_status_to_its_themed_token()
    {
        Assert.Equal(Token("OkBrush"), EnvColour(EnvStatus.Connected));
        Assert.Equal(Token("WarnBrush"), EnvColour(EnvStatus.TokenExpired));
        Assert.Equal(Token("ErrBrush"), EnvColour(EnvStatus.Disconnected));
    }

    [AvaloniaFact]
    public void EnvStatusToBrush_resolves_a_real_token_for_every_status_and_for_a_non_status()
    {
        // Never the Brushes.Gray "no such resource" fallback — that is the signal a token was renamed.
        foreach (var status in Enum.GetValues<EnvStatus>())
        {
            Assert.NotSame(Brushes.Gray, EnvBrush(status));
        }

        // A null / foreign value is the neutral secondary text colour, not a status colour.
        Assert.Equal(Token("Text2Brush"), EnvColour(null));
        Assert.Equal(Token("Text2Brush"), EnvColour("Connected"));
        Assert.NotEqual(EnvColour(EnvStatus.Connected), EnvColour(null));
    }

    [AvaloniaFact]
    public void EnvStatusToBrush_is_one_way() =>
        Assert.Throws<NotSupportedException>(() =>
            EnvStatusToBrushConverter.Instance.ConvertBack(Brushes.Red, typeof(EnvStatus), null, Culture));

    // ── LogKindToBrushConverter ───────────────────────────────────────────────────────────────────────

    private static Color LogColour(object? value) =>
        Assert.IsAssignableFrom<ISolidColorBrush>(
            LogKindToBrushConverter.Instance.Convert(value, typeof(IBrush), null, Culture)).Color;

    [AvaloniaFact]
    public void LogKindToBrush_maps_each_kind_to_its_themed_token()
    {
        Assert.Equal(Token("Text2Brush"), LogColour(LogKind.Info));
        Assert.Equal(Token("OkBrush"), LogColour(LogKind.Ok));
        Assert.Equal(Token("WarnBrush"), LogColour(LogKind.Warn));
        Assert.Equal(Token("ErrBrush"), LogColour(LogKind.Err));
    }

    [AvaloniaFact]
    public void LogKindToBrush_resolves_a_real_token_for_every_kind_and_for_a_non_kind()
    {
        foreach (var kind in Enum.GetValues<LogKind>())
        {
            Assert.NotSame(Brushes.Gray,
                Assert.IsAssignableFrom<IBrush>(
                    LogKindToBrushConverter.Instance.Convert(kind, typeof(IBrush), null, Culture)));
        }

        Assert.Equal(Token("Text2Brush"), LogColour(null));
        Assert.Equal(Token("Text2Brush"), LogColour("Err"));   // the string "Err" is not LogKind.Err
    }

    [AvaloniaFact]
    public void LogKindToBrush_distinguishes_the_three_non_info_kinds_from_each_other()
    {
        var colours = new List<Color> { LogColour(LogKind.Ok), LogColour(LogKind.Warn), LogColour(LogKind.Err) };

        Assert.Equal(colours.Count, colours.Distinct().Count());
    }

    [AvaloniaFact]
    public void LogKindToBrush_is_one_way() =>
        Assert.Throws<NotSupportedException>(() =>
            LogKindToBrushConverter.Instance.ConvertBack(Brushes.Red, typeof(LogKind), null, Culture));
}
