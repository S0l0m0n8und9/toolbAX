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

/// <summary>A dual-write table map (Operations grid row source).</summary>
public sealed record DwMap(
    string TableId,
    string Name,
    string FoEntity,
    string DvEntity,
    DwDirection Direction,
    MapState State,
    string TemplateVersion,
    string Author,
    long Rows24h,
    int Errors24h);

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
        _ => MapState.Running,
    };

    /// <summary>The terminal state a target map settles to after the action succeeds.</summary>
    public static MapState ResultState(DwAction action) => action.Id switch
    {
        "stop" => MapState.Stopped,
        "pause" => MapState.Paused,
        _ => MapState.Running, // start, resume, initial
    };

    public static bool IsTransitional(MapState state) => state is
        MapState.Starting or MapState.Stopping or MapState.Pausing or
        MapState.Resuming or MapState.InitialSyncing;
}
