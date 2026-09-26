using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FoToolbox.Core.Auth;
using FoToolbox.Core.Net;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Real <see cref="IVirtualTableReader"/>: GETs Dataverse <c>EntityDefinitions</c> metadata via the
/// <see cref="IDataverseClient"/> and classifies the virtual tables with
/// <see cref="VirtualTableMetadataParser"/>. A non-2xx response is returned as an error (not thrown) so
/// the screen can show a banner.
/// </summary>
public sealed class CoreVirtualTableReader : IVirtualTableReader
{
    private readonly IDataverseClient _dataverse;
    private readonly Func<EnvProfile?> _activeEnv;

    public CoreVirtualTableReader(IDataverseClient dataverse, Func<EnvProfile?>? activeEnv = null)
    {
        _dataverse = dataverse;
        _activeEnv = activeEnv ?? (() => null);
    }

    public async Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var pinned = _activeEnv();
        var identity = EnvironmentIdentity.TryCreate(pinned);
        var apiBase = PinnedApiBase(pinned);
        string? path = Pinned(apiBase, $"EntityDefinitions?$select={VirtualTableMetadataParser.SelectColumns}");
        var visits = new PageVisitTracker();
        var records = new List<VirtualTableInfo>();
        try
        {
            while (path is not null)
            {
                ct.ThrowIfCancellationRequested();
                if (identity is not null && !identity.IsCurrent(_activeEnv()))
                    return VirtualTableLoadResult.Fail("Couldn't load table metadata — the active environment changed.");
                if (!visits.TryVisit(path, baseAddress: null, out _))
                    return VirtualTableLoadResult.Fail(
                        "Couldn't load table metadata: paging stopped because the service repeated a request target. Results are incomplete.");

                var response = await _dataverse.GetAsync(path, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (identity is not null && !identity.IsCurrent(_activeEnv()))
                    return VirtualTableLoadResult.Fail("Couldn't load table metadata — the active environment changed.");
                if (!response.IsSuccess)
                    return VirtualTableLoadResult.Fail($"Couldn't load table metadata ({response.StatusLine}).");

                var page = VirtualTableMetadataParser.ParsePage(response.Body);
                ct.ThrowIfCancellationRequested();
                records.AddRange(page.Tables);
                path = page.NextLink is null ? null : Pinned(apiBase, page.NextLink);
            }

            ct.ThrowIfCancellationRequested();
            return VirtualTableLoadResult.Ok(records);
        }
        catch (MetadataResponseFormatException ex)
        {
            ct.ThrowIfCancellationRequested();
            return VirtualTableLoadResult.Fail($"Couldn't load table metadata: {ex.Message}");
        }
        catch
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
    }

    private static string Pinned(string? apiBase, string path) =>
        path.StartsWith("http", StringComparison.OrdinalIgnoreCase) || apiBase is null
            ? path
            : $"{apiBase}/{path.TrimStart('/')}";

    private static string? PinnedApiBase(EnvProfile? env)
    {
        if (string.IsNullOrWhiteSpace(env?.DataverseUrl)) return null;
        var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(env.DataverseUrl);
        return apiBase.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? apiBase : $"https://{apiBase}";
    }
}
