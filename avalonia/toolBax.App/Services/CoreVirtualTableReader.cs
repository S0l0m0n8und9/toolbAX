using System.Threading;
using System.Threading.Tasks;
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

    public CoreVirtualTableReader(IDataverseClient dataverse) => _dataverse = dataverse;

    public async Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default)
    {
        var path = $"EntityDefinitions?$select={VirtualTableMetadataParser.SelectColumns}";
        var response = await _dataverse.GetAsync(path, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return VirtualTableLoadResult.Fail($"Couldn't load table metadata ({response.StatusLine}).");
        }

        return VirtualTableLoadResult.Ok(VirtualTableMetadataParser.Parse(response.Body));
    }
}
