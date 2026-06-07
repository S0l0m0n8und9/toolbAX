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

