using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IDualWriteMapReader"/>: pages through Dataverse via the <see cref="IDataverseClient"/>
/// (following each <c>@odata.nextLink</c>) and reshapes records with <see cref="DualWriteMapParser"/>.
/// A non-2xx response on any page stops the load and is returned as an error (rather than thrown) so the
/// Map Browser can surface it. When a solution is given, the maps are constrained to that solution's
/// dual-write-map components.
/// </summary>
public sealed class CoreDualWriteMapReader : IDualWriteMapReader
{
    private readonly IDataverseClient _dataverse;

    // #210: entity-set name → logical name, so the snapshot-total upgrade below costs one EntityDefinitions
    // GET per entity set rather than one per count run.
    //
    // Ceiling: one short string pair per distinct CE entity set the user actually counts — the legs of the
    // maps they inspect — so tens of entries at most; nothing evicts on size because nothing can grow it
    // past the environment's dual-write map catalogue. The reader outlives an environment switch (the
    // client resolves the active environment at call time), so an entry CAN go stale; a cached name the
    // function call then rejects is dropped in TrySnapshotTotalAsync so the next run re-resolves, and until
    // then the row simply shows today's capped count.
    private readonly Dictionary<string, string> _logicalNames = new(StringComparer.OrdinalIgnoreCase);

    public CoreDualWriteMapReader(IDataverseClient dataverse) => _dataverse = dataverse;

    public async Task<DwMapLoadResult> GetMapsAsync(string? solutionUniqueName = null, CancellationToken ct = default)
    {
        HashSet<Guid>? solutionComponentIds = null;
        if (!string.IsNullOrWhiteSpace(solutionUniqueName))
        {
            var componentIds = new List<Guid>();
            var componentError = await PageAllAsync(
                DualWriteMapParser.SolutionComponentsPath(solutionUniqueName),
                body =>
                {
                    var page = DualWriteMapParser.ParseComponentIdPage(body);
                    return (page.ObjectIds, page.NextLink);
                },
                componentIds, "solution components", ct).ConfigureAwait(false);

            if (componentError is not null)
            {
                return DwMapLoadResult.Fail(componentError);
            }

            solutionComponentIds = componentIds.ToHashSet();
        }

        var maps = new List<DwMapRecord>();
        var error = await PageAllAsync(
            DualWriteMapParser.MapsPath(),
            body =>
            {
                var page = DualWriteMapParser.ParsePage(body);
                return (page.Records, page.NextLink);
            },
            maps, "dual-write maps", ct).ConfigureAwait(false);

        if (error is not null)
        {
            return DwMapLoadResult.Fail(error);
        }

        if (solutionComponentIds is not null)
        {
            maps = maps
                .Where(m => Guid.TryParse(m.Id, out var id) && solutionComponentIds.Contains(id))
                .ToList();
        }

        return DwMapLoadResult.Ok(maps);
    }

    public async Task<DwCountResult> GetCeRowCountAsync(string entitySet, string? odataFilter, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entitySet))
        {
            return DwCountResult.Fail("No Dataverse entity is set for this leg.");
        }

        var response = await _dataverse.GetAsync(DualWriteMapParser.CountPath(entitySet, odataFilter), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return DwCountResult.Fail(DescribeFailure(response, $"the row count for '{entitySet}'"));
        }

        var count = DualWriteMapParser.ParseCount(response.Body);
        if (count is null)
        {
            return DwCountResult.Fail($"Dataverse returned no count for '{entitySet}'.");
        }

        // A count that hit the platform ceiling is flagged, not reported as a total — Dataverse caps
        // @odata.count at 5,000 for a standard table, so "5,000" may really be any number ≥ 5,000.
        var capped = count.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap);

        // #210: a capped count on an UNFILTERED leg can be upgraded to a real total via
        // RetrieveTotalRecordCount, which has no ceiling — but accepts no filter, so a filtered leg keeps
        // its floor. The upgrade is strictly optional: every way it can go wrong returns null and the leg
        // falls back to the capped count it would have shown anyway, so this is never a new failure mode.
        if (capped && string.IsNullOrWhiteSpace(odataFilter) &&
            await TrySnapshotTotalAsync(entitySet, ct).ConfigureAwait(false) is { } total)
        {
            return DwCountResult.FromSnapshot(total);
        }

        return DwCountResult.Ok(count.Count, capped);
    }

    // The platform's snapshot total for an entity set, or null when it can't be had (no logical name, a
    // failed request, a body that carries no total for this table). Deliberately quiet: CoreDataverseClient
    // already mirrors any non-2xx to the session log per the RequestTrace conventions, and a miss here is a
    // non-event for the user — the count they asked for is still displayed.
    private async Task<long?> TrySnapshotTotalAsync(string entitySet, CancellationToken ct)
    {
        if (await ResolveLogicalNameAsync(entitySet, ct).ConfigureAwait(false) is not { } logicalName)
        {
            return null;
        }

        var response = await _dataverse
            .GetAsync(DualWriteMapParser.TotalRecordCountPath(logicalName), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            // A cached logical name the function rejects is what an environment switch looks like from here
            // (the set → logical mapping is per-environment table metadata): drop it so the next run
            // re-resolves instead of repeating a request that can no longer work.
            _logicalNames.Remove(entitySet);
            return null;
        }

        return DualWriteMapParser.ParseTotalRecordCount(response.Body, logicalName);
    }

    // The logical name behind an entity-set name, cached (see _logicalNames). A lookup that resolves
    // nothing is NOT cached: it may be an entity set this environment doesn't have, and the answer changes
    // when the active environment does.
    private async Task<string?> ResolveLogicalNameAsync(string entitySet, CancellationToken ct)
    {
        if (_logicalNames.TryGetValue(entitySet, out var cached))
        {
            return cached;
        }

        var response = await _dataverse
            .GetAsync(DualWriteMapParser.EntityLogicalNamePath(entitySet), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return null;
        }

        var logicalName = DualWriteMapParser.ParseEntityLogicalName(response.Body);
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            return null;
        }

        _logicalNames[entitySet] = logicalName;
        return logicalName;
    }

    public async Task<DwSolutionLoadResult> GetSolutionsAsync(CancellationToken ct = default)
    {
        var solutions = new List<DwSolution>();
        var error = await PageAllAsync(
            DualWriteMapParser.SolutionsPath(),
            body =>
            {
                var page = DualWriteMapParser.ParseSolutionPage(body);
                return (page.Solutions, page.NextLink);
            },
            solutions, "solutions", ct).ConfigureAwait(false);

        return error is not null ? DwSolutionLoadResult.Fail(error) : DwSolutionLoadResult.Ok(solutions);
    }

    // Pages a Dataverse query into <paramref name="sink"/>, following each nextLink. Returns null on
    // success or a banner-ready error string on the first non-2xx response.
    private async Task<string?> PageAllAsync<T>(
        string firstPath,
        Func<string, (IReadOnlyList<T> Items, string? NextLink)> parse,
        List<T> sink,
        string subject,
        CancellationToken ct)
    {
        string? pathOrUrl = firstPath;
        while (pathOrUrl is not null)
        {
            var response = await _dataverse.GetAsync(pathOrUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                return DescribeFailure(response, subject);
            }

            var (items, nextLink) = parse(response.Body);
            sink.AddRange(items);
            pathOrUrl = nextLink; // absolute URL; the client uses it verbatim
        }

        return null;
    }

    private static string DescribeFailure(ODataResponse response, string subject)
    {
        var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase)
            ? $"HTTP {response.StatusCode}"
            : response.ReasonPhrase;
        var body = response.Body.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return $"Couldn't load {subject} — {reason}.";
        }

        // Keep the banner readable; the full Dataverse error JSON can be long.
        if (body.Length > 500)
        {
            body = body[..500] + "…";
        }

        return $"Couldn't load {subject} — {reason}: {body}";
    }
}
