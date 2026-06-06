using System.Collections.Generic;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IPluginCatalog"/> seeded from the prototype (data.js PLUGINS). Each card's
/// <see cref="PluginCard.Id"/> is the shell tool id so the home grid navigates directly; "hello" is
/// the unsigned SDK sample (no built-in screen yet).
/// </summary>
public sealed class FakePluginCatalog : IPluginCatalog
{
    public IReadOnlyList<PluginCard> Plugins { get; } = new[]
    {
        new PluginCard("query", "Query Builder", "Data", "1.4.2",
            "Compose $select / $filter / $expand against OData with live preview and CSV export.",
            "Q", Signed: true, Hot: true),
        new PluginCard("ops", "Dual-Write Operations", "Integration", "0.3.0",
            "Drive the Dual-Write Management gateway: start, stop, pause, resume and initial-sync maps with live status.",
            "O", Signed: true, Live: true, Hot: true),
        new PluginCard("mapbrowser", "Dual-Write Map Browser", "Integration", "0.9.1",
            "Inspect F&O ↔ Dataverse entity maps, field bindings, value maps, and sync state.",
            "D", Signed: true, Hot: true),
        new PluginCard("compare", "Dual-Write Compare", "Integration", "0.2.0",
            "Diff dual-write maps and row counts across two environments.",
            "C", Signed: true),
        new PluginCard("metadata", "Table/Entity Browser", "Data", "1.2.0",
            "Explore $metadata: entity sets, navigation properties, enums, keys.",
            "M", Signed: true),
        new PluginCard("post", "OData POST Builder", "Data", "0.7.3",
            "Hand-craft and replay POST/PATCH requests with body validation.",
            "P", Signed: true),
        new PluginCard("profiles", "Profiles", "System", "1.0.0",
            "Environments, service principals, interactive sign-in and DPAPI-encrypted secrets.",
            "E", Signed: true, Builtin: true),
        new PluginCard("hello", "Hello Plugin", "Samples", "0.1.0",
            "SDK sample: minimal plugin showing lifecycle, logging, and capability injection.",
            "H", Signed: false),
    };
}
