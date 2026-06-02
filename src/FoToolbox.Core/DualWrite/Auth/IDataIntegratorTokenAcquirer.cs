using System.Threading;
using System.Threading.Tasks;

namespace FoToolbox.Core.DualWrite.Auth;

/// <summary>Acquires an IntegratorApp delegated token via ROPC. Abstracted for testing.</summary>
public interface IDataIntegratorTokenAcquirer
{
    Task<DualWriteToken> AcquireAsync(string authority, string clientId, string scope, string username, string password, CancellationToken ct);
}
