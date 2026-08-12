using System.Collections.Generic;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// The built-in tool catalogue shown on the Plugins home (control-map §1). These are native in-app
/// screens — not separately-versioned or separately-signed plugins — so each card carries only honest
/// metadata: name, category, description, accelerator, and whether the tool performs live operations.
/// Each card's <see cref="PluginCard.Id"/> is the shell tool id so the grid navigates to that tool.
/// </summary>
public sealed class BuiltInToolCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginCard> Plugins { get; } = new[]
    {
        new PluginCard("query", "Query Builder", "Data",
            "Compose $select / $filter / $expand against OData with live preview and CSV export.", "Q"),
        new PluginCard("ops", "Dual-Write Operations", "Integration",
            "Drive the Dual-Write Management gateway: start, stop, pause, resume and initial-sync maps with live status.",
            "O", OperatesLive: true),
        new PluginCard("mapbrowser", "Dual-Write Map Browser", "Integration",
            "Inspect F&O ↔ Dataverse entity maps, field bindings, value maps, and sync state.", "D"),
        new PluginCard("compare", "Dual-Write Compare", "Integration",
            "Diff dual-write maps and row counts across two environments.", "C"),
        new PluginCard("metadata", "Table/Entity Browser", "Data",
            "Explore $metadata: entity sets, navigation properties, enums, keys.", "M"),
        // Distinct from the Map Browser above: virtual tables surface F&O data live in Dataverse, they
        // don't copy it. Read-only — the tables themselves are generated in the maker portal (#23).
        new PluginCard("virtualtables", "Virtual Tables (CE → F&O)", "Integration",
            "Inspect the Finance & Operations–backed virtual tables in Dataverse: logical and external names, data source, provider, and managed state.",
            "V"),
        new PluginCard("post", "OData POST Builder", "Data",
            "Hand-craft and replay POST/PATCH requests with body validation.", "P"),
        new PluginCard("profiles", "Profiles", "System",
            "Environments, service principals, interactive sign-in and DPAPI-encrypted secrets.", "E"),
    };
}
