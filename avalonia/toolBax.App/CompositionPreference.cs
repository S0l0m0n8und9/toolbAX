using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;

namespace ToolBax.App;

/// <summary>
/// Which Win32 composition backends this run asks Avalonia for, and whether the environment picked them (#212).
/// <para>
/// A live hang (v1.2.2, Windows 11 26200, Avalonia 12.0.4) froze the window with no toolbAX frame anywhere in
/// the dump: the UI thread sat in <c>MediaContext.SyncWaitCompositorBatch</c> inside a synchronous paint while
/// the render thread never returned from the native <c>WinUiCompositedWindowRenderTarget.BeginDraw()</c> called
/// from <c>WinUiCompositorConnection.RunLoopHandler.WatchDog</c> — a deadlock wholly inside the
/// WinUIComposition backend. What distinguishes that backend is acrylic/transparency and blur, none of which
/// this app uses, so on this codebase its only distinguishing behaviour was the wedge. Hence the default below
/// drops it.
/// </para>
/// <para>
/// Parsing is deliberately total: an empty, misspelt or unknown value yields <see cref="Default"/> instead of
/// an exception. This is read while the app builder is being configured, and a throw there is a silent startup
/// death — no window, no message, nothing on screen to explain it — which is a far worse outcome than
/// quietly ignoring a typo in an escape hatch.
/// </para>
/// </summary>
public sealed class CompositionPreference
{
    /// <summary>
    /// Escape hatch, read once at startup: <c>winui</c> (the Avalonia default order, i.e. the pre-#212
    /// behaviour), <c>dxgi</c> or <c>surface</c>. Anything else is ignored in favour of <see cref="Default"/>.
    /// </summary>
    public const string EnvironmentVariable = "TOOLBAX_COMPOSITION";

    private static CompositionPreference? _current;

    private CompositionPreference(IReadOnlyList<Win32CompositionMode> modes, string? source)
    {
        Modes = modes;
        Source = source;
    }

    /// <summary>
    /// The requested backends, highest priority first — the shape
    /// <see cref="Win32PlatformOptions.CompositionMode"/> wants, where Avalonia takes the first entry the
    /// machine can actually provide.
    /// </summary>
    public IReadOnlyList<Win32CompositionMode> Modes { get; }

    /// <summary>
    /// The honoured <see cref="EnvironmentVariable"/> token, normalised to lower case, or <c>null</c> when the
    /// default applied — including when the variable was set to something unrecognised.
    /// </summary>
    public string? Source { get; }

    /// <summary>Whether <see cref="EnvironmentVariable"/> chose these modes. An ignored value counts as no.</summary>
    public bool FromEnvironment => Source is not null;

    /// <summary>
    /// What this process asks for when nothing overrides it: the low-latency DXGI swap chain, with the
    /// redirection surface underneath it. The fallback is not optional — <c>LowLatencyDxgiSwapChain</c> needs
    /// feature level 11_3 and the AngleEgl rendering mode, so a single-entry list would leave any machine that
    /// cannot meet that with no composition mode at all (which Avalonia reports by throwing).
    /// </summary>
    public static CompositionPreference Default { get; } = new(
        new[] { Win32CompositionMode.LowLatencyDxgiSwapChain, Win32CompositionMode.RedirectionSurface },
        source: null);

    /// <summary>This process's preference, resolved once from the environment and cached for the session.</summary>
    public static CompositionPreference Current => _current ??= ResolveFromEnvironment();

    /// <summary>
    /// Maps one <see cref="EnvironmentVariable"/> value to a composition fallback list. Pure and total: no
    /// input throws, and anything not recognised returns <see cref="Default"/>.
    /// </summary>
    public static CompositionPreference Resolve(string? requested)
    {
        var token = requested?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Default;
        }

        return token.ToLowerInvariant() switch
        {
            // Avalonia 12's own default order, so a machine that misbehaves under the new default can be put
            // back exactly where it was without a rebuild.
            "winui" => new CompositionPreference(
                new[]
                {
                    Win32CompositionMode.WinUIComposition,
                    Win32CompositionMode.DirectComposition,
                    Win32CompositionMode.RedirectionSurface,
                },
                "winui"),
            // Same list as the default, but recorded as an explicit choice so the log distinguishes
            // "nobody set anything" from "someone pinned this".
            "dxgi" => new CompositionPreference(Default.Modes, "dxgi"),
            // Nothing sensible sits below the redirection surface: it is itself the compatibility floor.
            "surface" => new CompositionPreference(new[] { Win32CompositionMode.RedirectionSurface }, "surface"),
            _ => Default,
        };
    }

    /// <summary>
    /// One line for the session-log header, e.g.
    /// <c>LowLatencyDxgiSwapChain &gt; RedirectionSurface (default)</c> or
    /// <c>RedirectionSurface (TOOLBAX_COMPOSITION=surface)</c>.
    /// </summary>
    public string Describe()
    {
        var modes = string.Join(" > ", Modes.Select(mode => mode.ToString()));
        return $"{modes} ({(Source is null ? "default" : $"{EnvironmentVariable}={Source}")})";
    }

    private static CompositionPreference ResolveFromEnvironment()
    {
        try
        {
            return Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));
        }
        catch (Exception ex)
        {
            // A denied or unavailable environment block must not cost the start: the default is what an
            // unset variable would have produced anyway, so there is nothing to do but say so and carry on.
            Trace.TraceWarning($"Could not read {EnvironmentVariable}; using the default composition preference. {ex.Message}");
            return Default;
        }
    }
}
