using FoToolbox.Core.OData;
using System.Threading;
using System.Threading.Tasks;

namespace QueryBuilderPlugin;

public interface IMetadataProvider
{
    Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, CancellationToken cancellationToken = default);
}
