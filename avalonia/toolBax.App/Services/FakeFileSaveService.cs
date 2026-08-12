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

    /// <summary>The file type the last save asked the picker for — a CSV export must not ask for Markdown.</summary>
    public SaveFileType? LastFileType { get; private set; }

    public Task<string?> SaveTextAsync(string suggestedFileName, string content, SaveFileType fileType,
        CancellationToken ct = default)
    {
        LastSuggestedName = suggestedFileName;
        LastContent = content;
        LastFileType = fileType;
        return Task.FromResult(_resultPath);
    }
}
