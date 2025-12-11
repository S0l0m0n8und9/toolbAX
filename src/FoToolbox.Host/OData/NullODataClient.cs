using FoToolbox.Core.OData;
using System.Collections.Generic;
using System.Threading;

namespace FoToolbox.Host.OData;

/// <summary>
/// Placeholder OData client until real implementation lands.
/// </summary>
internal sealed class NullODataClient : IODataClient
{
    public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default)
    {
        return ODataClientExtensions.EmptyPages(cancellationToken);
    }
}
