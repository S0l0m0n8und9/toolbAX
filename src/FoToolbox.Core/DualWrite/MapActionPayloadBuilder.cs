using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Builds the JSON body for the gateway <c>POST Start</c> action. Matches the
/// <c>MapStartStopAction</c> shape from <c>DWLibary/Struct/MapsHelper.cs</c>:
/// a top-level <c>action</c> code plus a <c>details[]</c> array, each detail carrying the
/// template id (<c>tid</c>), connection id (<c>cid</c>), the project id (<c>pid</c>), and a
/// <c>parameters</c> object (<c>skipInitialSync</c> + <c>conflictResolution</c>).
/// Per the MS tool's <c>NullValueHandling.Ignore</c> serialization, the initial-sync action
/// (code 8) omits both <c>pid</c> and <c>parameters</c>. Pure and deterministic for testing.
/// </summary>
public static class MapActionPayloadBuilder
{
    /// <summary>Conflict-resolution master that mirrors the MS default (CE wins).</summary>
    public const string DefaultConflictMaster = "CE";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    public static string Build(DualWriteActionType action, IReadOnlyList<DualWriteMap> maps, string cid)
    {
        if (maps is null)
        {
            throw new ArgumentNullException(nameof(maps));
        }

        if (maps.Count == 0)
        {
            throw new ArgumentException("At least one map is required.", nameof(maps));
        }

        if (string.IsNullOrWhiteSpace(cid))
        {
            throw new ArgumentException("Connection id (cid) is required.", nameof(cid));
        }

        var details = maps.Select(map =>
        {
            // The gateway dereferences tid (and, for non-initial-sync actions, pid) without
            // null-guarding, so an empty value serialized here surfaces as an opaque
            // "500 NullReferenceException". Reject it client-side with a map-named error (#25).
            var tid = string.IsNullOrWhiteSpace(map.ActiveTemplate?.Id) ? map.Id : map.ActiveTemplate!.Id;
            if (string.IsNullOrWhiteSpace(tid))
            {
                throw new ArgumentException(
                    $"Dual-write map '{DescribeMap(map)}' has no template id (tid); cannot build a {action.ToDisplayName()} request.",
                    nameof(maps));
            }

            var detail = new Dictionary<string, object?>
            {
                ["tid"] = tid,
                ["cid"] = cid
            };

            // Initial sync addresses the template directly and omits the project id and the
            // parameters object, matching the MS tool's action-8 payload (NullValueHandling.Ignore).
            if (action != DualWriteActionType.InitialSync)
            {
                if (string.IsNullOrWhiteSpace(map.ProjectId))
                {
                    throw new ArgumentException(
                        $"Dual-write map '{DescribeMap(map)}' has no project id (pid); cannot build a {action.ToDisplayName()} request.",
                        nameof(maps));
                }

                detail["pid"] = map.ProjectId;
                detail["parameters"] = new Dictionary<string, object?>
                {
                    // Start brings a map online without re-running initial sync (use the
                    // explicit Initial Sync action for that); other transitions don't sync.
                    ["skipInitialSync"] = action == DualWriteActionType.Start,
                    ["conflictResolution"] = new Dictionary<string, object?>
                    {
                        ["option"] = "1",
                        ["master"] = DefaultConflictMaster
                    }
                };
            }

            return detail;
        }).ToList();

        var payload = new Dictionary<string, object?>
        {
            ["action"] = action.ToActionCode(),
            ["details"] = details
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    /// <summary>A human-friendly identifier for a map, for use in validation error messages.</summary>
    private static string DescribeMap(DualWriteMap map) =>
        !string.IsNullOrWhiteSpace(map.DisplayName) ? map.DisplayName
        : !string.IsNullOrWhiteSpace(map.Name) ? map.Name
        : !string.IsNullOrWhiteSpace(map.Id) ? map.Id
        : "(unknown)";
}
