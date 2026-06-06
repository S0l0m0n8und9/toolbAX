using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// <see cref="IFileSaveService"/> backed by the window's Avalonia <see cref="IStorageProvider"/> (the
/// production implementation). A thin windowing/IO adapter — its behaviour is exercised through the app,
/// not unit tests (view models use <see cref="FakeFileSaveService"/>). No-ops if storage is unavailable.
/// </summary>
public sealed class StorageFileSaveService : IFileSaveService
{
    private readonly TopLevel _topLevel;

    public StorageFileSaveService(TopLevel topLevel) => _topLevel = topLevel;

    public async Task<string?> SaveTextAsync(string suggestedFileName, string content, CancellationToken ct = default)
    {
        var storage = _topLevel.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "md",
            FileTypeChoices = new List<FilePickerFileType>
            {
                new("Markdown") { Patterns = new[] { "*.md" } },
            },
        });

        if (file is null)
        {
            return null;
        }

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        ct.ThrowIfCancellationRequested();
        await writer.WriteAsync(content.AsMemory(), ct);
        return file.Path.IsAbsoluteUri ? file.Path.LocalPath : file.Name;
    }
}
