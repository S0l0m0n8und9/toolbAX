using System;
using System.Collections.Generic;

namespace ToolBax.Core.Models;

/// <summary>What backs a Dataverse virtual table.</summary>
public enum VirtualTableSource
{
    /// <summary>Backed by a finance-and-operations entity (the "mserp_" virtual-entity provider).</summary>
    FinanceAndOperations,

    /// <summary>A virtual table backed by some other (non-F&amp;O) external data provider.</summary>
    Other,
}

/// <summary>
/// A Dataverse virtual table (external entity) as seen in the CE environment's metadata, reshaped for the
/// CE-to-F&amp;O Virtual Tables screen. Read-only: this surfaces what the platform reports, it does not
/// generate or mutate virtual tables. Distinct from a dual-write map (which copies data between systems).
/// </summary>
public sealed record VirtualTableInfo(
    string LogicalName,
    string DisplayName,
    string ExternalName,
    string ExternalCollectionName,
    string DataProviderId,
    string DataSourceId,
    bool IsManaged,
    VirtualTableSource Source)
{
    public bool IsFinanceAndOperations => Source == VirtualTableSource.FinanceAndOperations;

    /// <summary>Master-list label: display name, falling back to the logical name.</summary>
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? LogicalName : DisplayName;

    public string SourceLabel => IsFinanceAndOperations ? "Finance & Operations" : "Other provider";

    public string ManagedLabel => IsManaged ? "Managed" : "Unmanaged";

    /// <summary>The external (source) name, falling back to a dash so the column never renders blank.</summary>
    public string ExternalNameLabel => string.IsNullOrWhiteSpace(ExternalName) ? "—" : ExternalName;
}

/// <summary>Outcome of loading the virtual-table catalogue: the tables, or an error for the banner.</summary>
public sealed record VirtualTableLoadResult(IReadOnlyList<VirtualTableInfo> Tables, string? Error)
{
    public bool IsSuccess => Error is null;

    public static VirtualTableLoadResult Ok(IReadOnlyList<VirtualTableInfo> tables) => new(tables, null);

    public static VirtualTableLoadResult Fail(string error) => new(Array.Empty<VirtualTableInfo>(), error);
}
