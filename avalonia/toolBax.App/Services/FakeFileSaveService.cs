using System;
using System.IO;
using System.Text;
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
    public int TextSaveCalls { get; private set; }
    public int StreamSaveCalls { get; private set; }

    /// <summary>The file type the last save asked the picker for — a CSV export must not ask for Markdown.</summary>
    public SaveFileType? LastFileType { get; private set; }

    public Task<string?> SaveTextAsync(string suggestedFileName, string content, SaveFileType fileType,
        CancellationToken ct = default)
    {
        TextSaveCalls++;
        LastSuggestedName = suggestedFileName;
        LastContent = content;
        LastFileType = fileType;
        return Task.FromResult(_resultPath);
    }

    public async Task<string?> SaveStreamAsync(string suggestedFileName, Stream content,
        SaveFileType fileType, CancellationToken ct = default)
    {
        StreamSaveCalls++;
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("The source stream must be readable.", nameof(content));
        LastSuggestedName = suggestedFileName;
        LastFileType = fileType;
        if (_resultPath is null) return null;
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        LastContent = await reader.ReadToEndAsync(ct);
        return _resultPath;
    }
}
