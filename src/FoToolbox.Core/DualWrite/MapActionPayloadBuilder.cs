using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Builds the JSON body for the gateway <c>POST Start</c> action. Mirrors the
/// <c>MapStartStopAction</c> shape from <c>DWLibary/Engines/DWMapEngine.cs</c>:
/// a top-level <c>action</c> code plus a <c>details[]</c> array, each detail carrying the
/// template id (<c>tid</c>), connection id (<c>cid</c>), and — except for initial sync — the
/// project id (<c>pid</c>). Pure and deterministic for unit testing.
/// </summary>
public static class MapActionPayloadBuilder
{
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
            var detail = new Dictionary<string, object?>
            {
                ["tid"] = string.IsNullOrWhiteSpace(map.ActiveTemplate?.Id) ? map.Id : map.ActiveTemplate!.Id,
                ["cid"] = cid
            };

            // Initial sync addresses the template directly and omits the project id,
            // matching the MS tool's action-8 payload.
            if (action != DualWriteActionType.InitialSync)
            {
                detail["pid"] = map.ProjectId;
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
}
