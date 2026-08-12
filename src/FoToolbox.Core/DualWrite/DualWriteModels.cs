using System.Collections.Generic;
using System.Globalization;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Dual-write map lifecycle actions. The integer value is the action code the
/// Dual-write Management gateway expects in the <c>Start</c> request body
/// (reverse-engineered from <c>DWLibary/Engines/DWMapEngine.cs</c>).
/// </summary>
public enum DualWriteActionType
{
    Start = 1,
    Stop = 4,
    Pause = 5,
    Resume = 6,
    InitialSync = 8
}

public static class DualWriteActionTypeExtensions
{
    public static string ToActionCode(this DualWriteActionType action) =>
        ((int)action).ToString(CultureInfo.InvariantCulture);

    /// <summary>A human label for confirmation prompts and status messages.</summary>
    public static string ToDisplayName(this DualWriteActionType action) => action switch
    {
        DualWriteActionType.Start => "Start",
        DualWriteActionType.Stop => "Stop",
        DualWriteActionType.Pause => "Pause",
        DualWriteActionType.Resume => "Resume",
        DualWriteActionType.InitialSync => "Initial sync",
        _ => action.ToString()
    };
}

/// <summary>Linkage record resolved from <c>Environments?targetType=AX&amp;identifier=...</c>.</summary>
public sealed record DualWriteEnvironment(string Cid, string Cname, string Identifier);

/// <summary>One available map template version (author + version).</summary>
public sealed record DualWriteTemplate(string Id, string Version, string Author);

/// <summary>A dual-write map with its active template and available template versions.</summary>
public sealed record DualWriteMap(
    string Id,
    string Name,
    string DisplayName,
    string ProjectId,
    string State,
    DualWriteTemplate? ActiveTemplate,
    IReadOnlyList<DualWriteTemplate> Templates)
{
    public string CurrentVersion => ActiveTemplate?.Version ?? string.Empty;
    public string CurrentAuthor => ActiveTemplate?.Author ?? string.Empty;

    /// <summary>The CE (Dataverse) entity name for this map, used for integration-key apply. May be empty.</summary>
    public string RightEntityName { get; init; } = string.Empty;

    /// <summary>
    /// The lifecycle actions the gateway reports this map's <em>current state</em> accepts
    /// (<c>detail.actions</c>), as raw action codes — the same numbering the <c>Start</c> request body
    /// uses, i.e. <see cref="DualWriteActionType"/> (Start=1, Stop=4, Pause=5, Resume=6, InitialSync=8).
    /// A live capture pairs <c>"state":"4"</c> (Running) with <c>"actions":["4","5"]</c> = Stop + Pause.
    /// <para>
    /// Kept as raw codes rather than parsed enum values so a code this build doesn't recognise stays in
    /// the set instead of vanishing: a set that silently lost its only entry would read as "supports
    /// nothing" and lock the map out of every action.
    /// </para>
    /// <para><c>null</c> or empty means the gateway did not say — <em>unknown</em>, never "none".</para>
    /// </summary>
    public IReadOnlySet<string>? Actions { get; init; }

    /// <summary>
    /// Whether this map currently supports <paramref name="action"/>: <c>true</c>/<c>false</c> when the
    /// gateway reported an action list, and <c>null</c> when it didn't (see <see cref="Actions"/>).
    /// Callers must treat <c>null</c> as "let it through" — older gateways omit the field, and refusing
    /// on absent data would lock a user out of the whole screen.
    /// </summary>
    public bool? Supports(DualWriteActionType action) =>
        Actions is null || Actions.Count == 0 ? null : Actions.Contains(action.ToActionCode());
}

/// <summary>A project field mapping (the unit "refresh tables" operates on).</summary>
public sealed record DualWriteFieldMapping(string Name);

/// <summary>Result of a <c>POST Start</c> action: carries the request id to poll.</summary>
public sealed record DualWriteActionResponse(string RequestId, string? State);

/// <summary>Result of a <c>GET Status/{requestId}</c> poll.</summary>
public sealed record DualWriteRequestStatus(string RequestId, string State, bool IsTerminal, bool IsSuccess, string? Message);
