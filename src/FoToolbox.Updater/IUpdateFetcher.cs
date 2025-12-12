using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Updater;

public interface IUpdateFetcher
{
    Task<Stream> FetchAsync(Uri uri, CancellationToken cancellationToken = default);
}
