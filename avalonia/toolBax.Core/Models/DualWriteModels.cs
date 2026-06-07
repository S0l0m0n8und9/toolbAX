using System;
using System.Collections.Generic;

namespace ToolBax.Core.Models;

/// <summary>Dual-write map lifecycle state. The last five are transitional (a verb in flight).</summary>
public enum MapState
{
    Idle,
    Stopped,
    Running,
    Paused,
    Errored,
    Starting,
    Stopping,
    Pausing,
    Resuming,
    InitialSyncing,
}

/// <summary>Sync direction between the F&amp;O entity and the Dataverse table.</summary>
public enum DwDirection
{
    Both,
    FoToDv,
    DvToFo,
}

/// <summary>Shared glyphs for a sync direction (both ↔, fo→dv →, dv→fo ←).</summary>
public static class DwDirectionExtensions
{
    public static string Arrow(this DwDirection direction) => direction switch
    {
        DwDirection.Both => "↔",
        DwDirection.FoToDv => "→",
        DwDirection.DvToFo => "←",
        _ => "·",
    };
}

/// <summary>
/// A lifecycle action. <see cref="Code"/> is fixed by the gateway API
/// (start=1, stop=4, pause=5, resume=6, initial=8); <see cref="AppliesTo"/> gates eligibility.
/// </summary>
public sealed record DwAction(
    string Id,
    int Code,
    string Label,
    bool Mutating,
    bool Danger,
    string Verb,
    IReadOnlySet<MapState> AppliesTo);

/// <summary>The five gateway actions in CommandBar order, with their eligibility sets.</summary>
public static class DwActions
{
    public static IReadOnlyList<DwAction> All { get; } = new[]
    {
        new DwAction("start", 1, "Start", true, false, "starting",
            new HashSet<MapState> { MapState.Stopped, MapState.Idle }),
        new DwAction("stop", 4, "Stop", true, true, "stopping",
            new HashSet<MapState> { MapState.Running, MapState.Paused }),
        new DwAction("pause", 5, "Pause", true, false, "pausing",
            new HashSet<MapState> { MapState.Running }),
        new DwAction("resume", 6, "Resume", true, false, "resuming",
            new HashSet<MapState> { MapState.Paused }),
        new DwAction("initial", 8, "Initial sync", true, true, "initial-syncing",
            new HashSet<MapState> { MapState.Running, MapState.Stopped, MapState.Idle, MapState.Paused }),
    };

    /// <summary>The state a target map is set to while the action is in flight.</summary>
    public static MapState VerbState(DwAction action) => action.Id switch
    {
        "start" => MapState.Starting,
        "stop" => MapState.Stopping,
        "pause" => MapState.Pausing,
        "resume" => MapState.Resuming,
        "initial" => MapState.InitialSyncing,
        // Surface a missing mapping (e.g. a new gateway action) instead of optimistically
        // reporting Running, which would give wrong feedback and wrong eligibility.
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Id, "No VerbState mapping for this action."),
    };

    /// <summary>The terminal state a target map settles to after the action succeeds.</summary>
    public static MapState ResultState(DwAction action) => action.Id switch
    {
        "start" or "resume" or "initial" => MapState.Running,
        "stop" => MapState.Stopped,
        "pause" => MapState.Paused,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action.Id, "No ResultState mapping for this action."),
    };

    public static bool IsTransitional(MapState state) => state is
        MapState.Starting or MapState.Stopping or MapState.Pausing or
        MapState.Resuming or MapState.InitialSyncing;
}
