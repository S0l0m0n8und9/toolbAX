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
    private readonly Func<EnvProfile?> _activeEnv;

    // #210: entity-set name → logical name, so the snapshot-total upgrade below costs one EntityDefinitions
    // GET per entity set rather than one per count run.
    //
    // Keyed by ENVIRONMENT identity as well as entity set, because this reader is built once and outlives
    // an environment switch (the client resolves the active environment at call time). The set → logical
    // mapping is per-environment table metadata, so a single-keyed cache would let environment A's answer
    // serve environment B — and eviction-on-rejection below cannot save that case: a logical name that also
    // EXISTS in B is accepted and returns the WRONG TABLE'S total, silently. Environment identity is the
    // only thing that makes the entry unambiguous.
    //
    // Ceiling: one short string pair per (environment, entity set) actually counted — the legs of the maps
    // the user inspects, in the environments they visit — so tens of entries at most; nothing evicts on
    // size because nothing can grow it past that.
    private readonly Dictionary<string, string> _logicalNames = new(StringComparer.Ordinal);

    /// <param name="activeEnv">
    /// The active environment at call time — the same accessor the <see cref="IDataverseClient"/> resolves
    /// against, so the logical-name cache is keyed by the environment a lookup was actually answered for.
    /// Defaults to "no environment", which is a single consistent bucket for hosts that don't have one.
    /// </param>
    public CoreDualWriteMapReader(IDataverseClient dataverse, Func<EnvProfile?>? activeEnv = null)
    {
        _dataverse = dataverse;
        _activeEnv = activeEnv ?? (() => null);
    }

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
        // Resolved once, before any await: the active environment can move mid-flight, and the entry this
        // call reads has to be the entry it evicts — not one belonging to whatever became active since.
        var cacheKey = CacheKey(entitySet);
        if (await ResolveLogicalNameAsync(cacheKey, entitySet, ct).ConfigureAwait(false) is not { } logicalName)
        {
            return null;
        }

        var response = await _dataverse
            .GetAsync(DualWriteMapParser.TotalRecordCountPath(logicalName), ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            // Still worth evicting even though the key now carries the environment: within ONE environment a
            // table can be renamed or removed under a cached name, and repeating a dead request for the life
            // of the app is no better than re-asking once.
            _logicalNames.Remove(cacheKey);
            return null;
        }

        return DualWriteMapParser.ParseTotalRecordCount(response.Body, logicalName);
    }

    // The cache key: the active environment's identity, then the entity set. Compared Ordinal because an
    // environment id is an opaque profile id that must match exactly — the same comparison
    // DualWriteMapViewModel's env-change guard uses. A differently-cased entity set therefore gets its own
    // entry, which is equally correct, just not shared.
    private string CacheKey(string entitySet) => $"{_activeEnv()?.Id}|{entitySet}";

    // The logical name behind an entity-set name, cached per environment (see _logicalNames). A lookup that
    // resolves nothing is NOT cached: it may be an entity set this environment doesn't have, and a negative
    // answer is the one most likely to be wrong for the next environment.
    private async Task<string?> ResolveLogicalNameAsync(string cacheKey, string entitySet, CancellationToken ct)
    {
        if (_logicalNames.TryGetValue(cacheKey, out var cached))
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

        _logicalNames[cacheKey] = logicalName;
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
