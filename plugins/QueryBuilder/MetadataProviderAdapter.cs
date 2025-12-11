using FoToolbox.Core.OData;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace QueryBuilderPlugin;

internal sealed class MetadataProviderAdapter : IMetadataProvider
{
    private readonly ODataMetadataProvider _inner;

    public MetadataProviderAdapter(string cachePath)
    {
        var cache = new ODataMetadataCache($"Data Source={cachePath};Foreign Keys=true");
        _inner = new ODataMetadataProvider(new HttpClient(), cache);
    }

    public Task<ODataMetadata> GetMetadataAsync(string envId, string baseUrl, CancellationToken cancellationToken = default) =>
        _inner.GetMetadataAsync(envId, baseUrl, cancellationToken);
}
