using System;
using System.Collections.Generic;
using System.Linq;

namespace FoToolbox.Core.DualWrite;

/// <summary>One key definition on a CE schema (e.g. the integration key "USERKEYS").</summary>
public sealed record DualWriteSchemaKey(string Name, string? DisplayName, IReadOnlyList<string> Fields);

/// <summary>A CE entity schema within a connection-set environment.</summary>
public sealed record DualWriteSchema(string Name, IReadOnlyList<DualWriteSchemaKey> Keys);

/// <summary>One environment (CE or FO) within a dual-write connection set.</summary>
public sealed record DualWriteConnectionSetEnvironment(
    string Name,
    string DisplayName,
    string PowerAppsEnvironment,
    bool IsDevInstance,
    string TargetType,
    string DirectUrl,
    IReadOnlyList<DualWriteSchema> Schemas)
{
    /// <summary>True for the CE side (targetType "CRM" or containing "CDS"), per the MS tool.</summary>
    public bool IsCe =>
        string.Equals(TargetType, "CRM", StringComparison.OrdinalIgnoreCase) ||
        TargetType.Contains("CDS", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the F&amp;O side (targetType "AX").</summary>
    public bool IsFo => string.Equals(TargetType, "AX", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Parsed dual-write connection set (the <c>GET api/ConnectionSet/{cname}</c> response).
/// Models only what reset-links and integration-key inspection need. Mirrors
/// <c>DWLibary/Struct/DWConnectionSet.cs</c>.
/// </summary>
public sealed record DualWriteConnectionSet(
    string Name,
    IReadOnlyList<DualWriteConnectionSetEnvironment> Environments,
    IReadOnlyList<string> LegalEntities)
{
    /// <summary>The CE environment (targetType CRM/CDS), or null if absent.</summary>
    public DualWriteConnectionSetEnvironment? CeEnvironment => Environments.FirstOrDefault(e => e.IsCe);

    /// <summary>The F&amp;O environment (targetType AX), or null if absent.</summary>
    public DualWriteConnectionSetEnvironment? FoEnvironment => Environments.FirstOrDefault(e => e.IsFo);

    /// <summary>
    /// The integration key for a CE entity, matching the MS tool's logic: the CE schema with
    /// the given entity name, preferring a key named "USERKEYS", else "USERKEY".
    /// </summary>
    public DualWriteSchemaKey? GetIntegrationKey(string ceEntityName)
    {
        var schema = CeEnvironment?.Schemas
            .FirstOrDefault(s => string.Equals(s.Name, ceEntityName, StringComparison.OrdinalIgnoreCase));
        if (schema is null)
        {
            return null;
        }

        return schema.Keys.FirstOrDefault(k => string.Equals(k.Name, "USERKEYS", StringComparison.OrdinalIgnoreCase))
            ?? schema.Keys.FirstOrDefault(k => string.Equals(k.Name, "USERKEY", StringComparison.OrdinalIgnoreCase));
    }
}
