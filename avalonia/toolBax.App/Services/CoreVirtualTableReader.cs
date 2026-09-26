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
        ct.ThrowIfCancellationRequested();
        var path = $"EntityDefinitions?$select={VirtualTableMetadataParser.SelectColumns}";
        ODataResponse response;
        try
        {
            response = await _dataverse.GetAsync(path, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            ct.ThrowIfCancellationRequested();
            throw;
        }
        if (!response.IsSuccess)
        {
            return VirtualTableLoadResult.Fail($"Couldn't load table metadata ({response.StatusLine}).");
        }

        try
        {
            var records = VirtualTableMetadataParser.Parse(response.Body);
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
}
