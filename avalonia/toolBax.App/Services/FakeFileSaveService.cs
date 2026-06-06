using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IFileSaveService"/> for design-mode + tests: records the last save and returns
/// a configurable result path (<c>null</c> models a cancelled dialog). Writes nothing to disk.
/// </summary>
public sealed class FakeFileSaveService : IFileSaveService
{
    private readonly string? _resultPath;

    public FakeFileSaveService(string? resultPath = null) => _resultPath = resultPath;

    public string? LastSuggestedName { get; private set; }
    public string? LastContent { get; private set; }

    public Task<string?> SaveTextAsync(string suggestedFileName, string content, CancellationToken ct = default)
    {
        LastSuggestedName = suggestedFileName;
        LastContent = content;
        return Task.FromResult(_resultPath);
    }
}
