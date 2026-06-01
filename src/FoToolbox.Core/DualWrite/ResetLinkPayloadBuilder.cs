using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Builds the body for <c>POST api/ConnectionSet/{cid}/Reset</c>. Matches the MS
/// <c>ResetLinkPayload</c> (<c>DWLibary/Struct/ResetLinkPayload.cs</c>): the CE then FO
/// environment entries, the CE's powerApps environment as <c>powerAppsEnvironmentName</c>,
/// and the chosen legal entities. Note the MS quirk that each environment's <c>id</c> is the
/// powerApps environment id. Pure for testing.
/// </summary>
public static class ResetLinkPayloadBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Build(DualWriteConnectionSet connectionSet, IReadOnlyList<string> legalEntities)
    {
        var environments = new List<object>();
        AddEnvironment(environments, connectionSet.CeEnvironment);
        AddEnvironment(environments, connectionSet.FoEnvironment);

        var payload = new
        {
            powerAppsEnvironmentName = connectionSet.CeEnvironment?.PowerAppsEnvironment ?? string.Empty,
            environments,
            legalEntities
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static void AddEnvironment(List<object> target, DualWriteConnectionSetEnvironment? env)
    {
        if (env is null)
        {
            return;
        }

        target.Add(new
        {
            targetType = env.TargetType,
            name = env.Name,
            displayName = env.DisplayName,
            id = env.PowerAppsEnvironment, // MS quirk: ResetLinkEnvironment.id = powerAppsEnvironment
            isDevInstance = env.IsDevInstance,
            directUrl = env.DirectUrl
        });
    }
}
