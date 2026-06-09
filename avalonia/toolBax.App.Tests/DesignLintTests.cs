using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Design-discipline lint: scans the app's .axaml views and fails on the patterns the UX review called
/// out — hardcoded colours (must use a token) and sub-11px text (below the legibility floor). These run
/// as plain file scans (no renderer), so they're fast and catch regressions the moment they're typed.
/// </summary>
public class DesignLintTests
{
    // Walk up from the test output dir to the source tree (present in CI checkouts and locally).
    private static string AppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "toolBax.App", "Views")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate the toolBax.App source tree from the test output directory.");
        return Path.Combine(dir!.FullName, "toolBax.App");
    }

    private static IEnumerable<string> ViewFiles(string appRoot) =>
        // Exclude Themes/ — Tokens.axaml is the ONE place colours are defined (as #hex), by design.
        Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/Themes/"));

    // Replace XML comments with the same number of newlines, so commented-out markup can't trip a lint
    // (false positives) while reported line numbers stay accurate.
    private static string BlankComments(string xaml) =>
        Regex.Replace(xaml, "<!--.*?-->", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);

    private static IEnumerable<(string File, int Line, string Text)> Lines()
    {
        var appRoot = AppRoot();
        foreach (var file in ViewFiles(appRoot))
        {
            var rel = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            var lines = BlankComments(File.ReadAllText(file)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                yield return (rel, i + 1, lines[i]);
            }
        }
    }

    [Fact]
    public void No_hardcoded_colours_on_brush_properties()
    {
        // A colour on a brush/colour property must come from a token (e.g. {StaticResource AccentBrush}),
        // not a literal #hex — so the palette stays single-sourced in Tokens.axaml.
        var rx = new Regex("(Background|Foreground|BorderBrush|Fill|Stroke|Color)\\s*=\\s*\"#[0-9A-Fa-f]{3,8}\"");
        var offenders = Lines().Where(x => rx.IsMatch(x.Text))
            .Select(x => $"{x.File}:{x.Line}").ToList();

        Assert.True(offenders.Count == 0,
            "Hardcoded colours found (use a {StaticResource …} token): " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_sub_11px_font_sizes()
    {
        // 11px is the legibility floor for this desktop app — no 9/10/10.5px text.
        var rx = new Regex("FontSize\\s*=\\s*\"(\\d+(?:\\.\\d+)?)\"");
        var offenders = Lines()
            .SelectMany(x => rx.Matches(x.Text).Select(m =>
                (x.File, x.Line, Size: double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))))
            .Where(x => x.Size < 11)
            .Select(x => $"{x.File}:{x.Line} ({x.Size}px)").ToList();

        Assert.True(offenders.Count == 0,
            "Sub-11px font sizes found (raise to >= 11): " + string.Join(", ", offenders));
    }
}
