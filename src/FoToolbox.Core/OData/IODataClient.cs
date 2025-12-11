using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.OData;

/// <summary>
/// Minimal OData client contract exposed to host and plugins.
/// </summary>
public interface IODataClient
{
    IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Simple request object; will expand as the OData builder matures.
/// </summary>
public sealed record QueryRequest(string Url);

/// <summary>
/// Paged OData response slice.
/// </summary>
public sealed record ODataPage(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, string? NextLink);

/// <summary>
/// Helper to create an empty async stream.
/// </summary>
public static class ODataClientExtensions
{
    public static async IAsyncEnumerable<ODataPage> EmptyPages([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
