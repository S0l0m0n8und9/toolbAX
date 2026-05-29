using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Produces a JSON snapshot of an environment's dual-write configuration (maps, active
/// versions, available template versions, state). Mirrors the MS tool's "export
/// configuration" feature, which is a client-side composition of read calls rather than a
/// gateway endpoint. Pure: the timestamp is passed in for deterministic testing.
/// </summary>
public static class DualWriteConfigExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static string ExportJson(DualWriteEnvironment environment, IReadOnlyList<DualWriteMap> maps, DateTimeOffset timestamp)
    {
        if (environment is null)
        {
            throw new ArgumentNullException(nameof(environment));
        }

        if (maps is null)
        {
            throw new ArgumentNullException(nameof(maps));
        }

        var model = new
        {
            exportedUtc = timestamp.ToUniversalTime().ToString("o"),
            environment = new
            {
                identifier = environment.Identifier,
                cid = environment.Cid,
                cname = environment.Cname
            },
            mapCount = maps.Count,
            maps = maps
                .OrderBy(m => string.IsNullOrWhiteSpace(m.DisplayName) ? m.Name : m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(m => new
                {
                    name = m.Name,
                    displayName = m.DisplayName,
                    projectId = m.ProjectId,
                    state = m.State,
                    activeVersion = m.CurrentVersion,
                    activeAuthor = m.CurrentAuthor,
                    templates = m.Templates
                        .Select(t => new { version = t.Version, author = t.Author, id = t.Id })
                        .ToList()
                })
                .ToList()
        };

        return JsonSerializer.Serialize(model, SerializerOptions);
    }
}
