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
        // A count that hit the platform ceiling is flagged, not reported as a total — Dataverse caps
        // @odata.count at 5,000 for a standard table, so "5,000" may really be any number ≥ 5,000.
        return count is null
            ? DwCountResult.Fail($"Dataverse returned no count for '{entitySet}'.")
            : DwCountResult.Ok(count.Count, count.IsCappedAt(DualWriteMapParser.DataverseStandardCountCap));
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
