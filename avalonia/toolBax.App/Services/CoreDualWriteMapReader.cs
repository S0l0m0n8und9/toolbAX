using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
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
    // Keyed by ENVIRONMENT IDENTITY — profile id AND normalized endpoint, see EnvIdentity — as well as
    // entity set, because this reader is built once and outlives an environment switch (the client resolves
    // the active environment at call time). The set → logical mapping is per-organisation table metadata, so
    // a set-only key would let environment A's answer serve environment B, and eviction-on-rejection below
    // cannot save that case: a logical name that also EXISTS in B is accepted and returns the WRONG TABLE'S
    // total, silently. The endpoint belongs in the key for the same reason the id does — a profile can be
    // repointed at a different organisation without its id changing (the #151 recipe).
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

        // PIN the whole operation to one environment INSTANCE, captured before any await, and address every
        // request below to THAT instance's endpoint. Re-checking an environment *id* afterwards cannot make a
        // response trustworthy: an A→B→A switch spanning the fetches leaves the id equal to what was captured
        // while the requests were answered by B, and a profile edited in place keeps its id while pointing at
        // a different organisation. Both die to the same move — an absolute URL is adjudicated by
        // CoreDataverseClient's origin guard against whichever environment is active when it goes out, so a
        // request either reaches the pinned environment or is refused. The response then provably belongs to
        // the pinned instance, and IsStillCurrent below decides only whether it may still be cached/shown.
        //
        // Pinned from HERE rather than from the upgrade alone: otherwise a switch landing between this count
        // and the upgrade would pair environment A's count with environment B's total in one result.
        var pinned = _activeEnv();
        var apiBase = PinnedApiBase(pinned);

        var response = await _dataverse
            .GetAsync(Pinned(apiBase, DualWriteMapParser.CountPath(entitySet, odataFilter)), ct).ConfigureAwait(false);
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
        if (capped && string.IsNullOrWhiteSpace(odataFilter) && apiBase is not null &&
            await TrySnapshotTotalAsync(pinned, apiBase, entitySet, ct).ConfigureAwait(false) is { } total)
        {
            return DwCountResult.FromSnapshot(total);
        }

        return DwCountResult.Ok(count.Count, capped);
    }

    // A request addressed to the pinned environment's Dataverse endpoint. Falls back to the relative path
    // only when the captured environment has no Dataverse URL at all: the client answers that with its own
    // "No Dataverse URL" message, and fabricating an absolute URL from nothing would replace that clear
    // diagnosis with an origin refusal.
    private static string Pinned(string? apiBase, string path) =>
        apiBase is null ? path : $"{apiBase}/{path}";

    // The captured environment's Dataverse Web API base as an ABSOLUTE url, or null when it has no endpoint.
    // Mirrors CoreDataverseClient.BuildUri including its scheme repair, so a pinned url is exactly the base
    // that client would have resolved — and therefore passes its origin guard while, and only while, the
    // pinned environment is the active one.
    private static string? PinnedApiBase(EnvProfile? env)
    {
        if (string.IsNullOrWhiteSpace(env?.DataverseUrl))
        {
            return null;
        }

        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(env.DataverseUrl);
        return apiBase.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? apiBase : $"https://{apiBase}";
    }

    // The platform's snapshot total for an entity set, or null when it can't be had (no logical name, a
    // failed request, a body that carries no total for this table). Deliberately quiet: CoreDataverseClient
    // already mirrors any non-2xx to the session log per the RequestTrace conventions, and a miss here is a
    // non-event for the user — the count they asked for is still displayed.
    private async Task<long?> TrySnapshotTotalAsync(
        EnvProfile? pinned, string apiBase, string entitySet, CancellationToken ct)
    {
        var cacheKey = CacheKey(pinned, entitySet);
        if (await ResolveLogicalNameAsync(pinned, apiBase, cacheKey, entitySet, ct).ConfigureAwait(false)
            is not { } logicalName)
        {
            return null;
        }

        var response = await _dataverse
            .GetAsync($"{apiBase}/{DualWriteMapParser.TotalRecordCountPath(logicalName)}", ct).ConfigureAwait(false);

        // Pinning already guarantees this total came from the pinned environment (or never came at all).
        // What remains is whether it may still be CACHED and SHOWN: an answer for an environment the user has
        // since left is not one to store or display. Deliberately ahead of the eviction below — after a
        // switch the response is a verdict on a request the guard refused, so it says nothing about the entry
        // cached for the pinned environment, and acting on it would throw away correct metadata.
        if (!IsStillCurrent(pinned))
        {
            return null;
        }

        if (!response.IsSuccess)
        {
            // Within ONE environment a table can be renamed or removed under a cached name, and repeating a
            // dead request for the life of the app is no better than re-asking once.
            _logicalNames.Remove(cacheKey);
            return null;
        }

        return DualWriteMapParser.ParseTotalRecordCount(response.Body, logicalName);
    }

    // True while the active environment is still the one this operation pinned. With the requests pinned the
    // DATA is trustworthy either way, so this is purely the commit gate for caching/displaying it — the same
    // role CoreMetadataService.IsStillCurrent (#170) plays for its catalogue writes.
    private bool IsStillCurrent(EnvProfile? pinned) =>
        string.Equals(EnvIdentity(_activeEnv()), EnvIdentity(pinned), StringComparison.Ordinal);

    /// <summary>
    /// The identity a cached answer belongs to: profile id AND normalized Dataverse endpoint. Same recipe as
    /// <c>CatalogService.CacheKey</c> (#151) — lower-invariant, scheme-defaulted, no trailing slash — and for
    /// the same reason: a profile is editable in place, so the id can survive while the URL is repointed at a
    /// different organisation, and an id-only key would then answer the new endpoint with the old one's
    /// metadata. Compared Ordinal, after normalization has removed the differences that don't matter.
    /// </summary>
    private static string EnvIdentity(EnvProfile? env)
    {
        var url = (env?.DataverseUrl ?? string.Empty).Trim().ToLowerInvariant();
        if (url.Length > 0 && !url.StartsWith("http", StringComparison.Ordinal))
        {
            url = "https://" + url;
        }

        return $"{env?.Id}|{url.TrimEnd('/')}";
    }

    // The cache key: the environment identity a lookup was answered for, then the entity set. Derived from
    // the SAME captured instance the requests are addressed to, so the key and the endpoint cannot disagree.
    private static string CacheKey(EnvProfile? env, string entitySet) => $"{EnvIdentity(env)}|{entitySet}";

    // The logical name behind an entity-set name, cached per environment (see _logicalNames). A lookup that
    // resolves nothing is NOT cached: it may be an entity set this environment doesn't have, and a negative
    // answer is the one most likely to be wrong for the next environment.
    private async Task<string?> ResolveLogicalNameAsync(
        EnvProfile? pinned, string apiBase, string cacheKey, string entitySet, CancellationToken ct)
    {
        if (_logicalNames.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var response = await _dataverse
            .GetAsync($"{apiBase}/{DualWriteMapParser.EntityLogicalNamePath(entitySet)}", ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return null;
        }

        var logicalName = DualWriteMapParser.ParseEntityLogicalName(response.Body);
        if (string.IsNullOrWhiteSpace(logicalName))
        {
            return null;
        }

        // The name came from the pinned endpoint by construction; this only withholds the cache write when
        // the user has since moved on, so an entry can't be created for an environment nobody is looking at.
        if (!IsStillCurrent(pinned))
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
