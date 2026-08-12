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
/// production implementation). A thin windowing/IO adapter — the picker plumbing is exercised through
/// the app, not unit tests (view models use <see cref="FakeFileSaveService"/>), but the two decisions
/// that are not windowing — which file type the picker offers, and the bytes written — are factored
/// into <see cref="BuildOptions"/> / <see cref="WriteTextAsync"/> so they can be asserted without a
/// TopLevel. No-ops if storage is unavailable.
/// </summary>
public sealed class StorageFileSaveService : IFileSaveService
{
    private readonly TopLevel _topLevel;

    public StorageFileSaveService(TopLevel topLevel) => _topLevel = topLevel;

    /// <summary>
    /// UTF-8 <em>with</em> a byte-order mark, matching <c>FoToolbox.Core.Export.CsvExporter</c>
    /// (<c>src/FoToolbox.Core/Export/CsvExporter.cs:20</c>), which already emits one deliberately:
    /// Excel on Windows decodes a BOM-less UTF-8 <c>.csv</c> as ANSI, so every non-ASCII character
    /// becomes mojibake — an exported em-dash arrived as <c>â€"</c> in every null cell (#168).
    /// </summary>
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// The save-picker options for <paramref name="fileType"/>: the extension the dialog defaults to and
    /// the single type choice it filters on. Static + public so the mapping is assertable — building it
    /// is the part of a save that can be wrong without any windowing being involved.
    /// </summary>
    public static FilePickerSaveOptions BuildOptions(string suggestedFileName, SaveFileType fileType) =>
        new()
        {
            SuggestedFileName = suggestedFileName,
            DefaultExtension = fileType.Extension,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new(fileType.DisplayName) { Patterns = new[] { fileType.Pattern } },
            },
        };

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="stream"/> as UTF-8 with a BOM (see
    /// <see cref="Utf8WithBom"/>). Leaves the stream open — its owner disposes it.
    /// </summary>
    /// <remarks>
    /// Deliberately takes no <see cref="CancellationToken"/>: callers only reach here past the point of
    /// no return (the destination is already truncated), where finishing the write is strictly better
    /// than honouring a late cancel with a torn file.
    /// </remarks>
    public static async Task WriteTextAsync(Stream stream, string content)
    {
        await using var writer = new StreamWriter(stream, Utf8WithBom, leaveOpen: true);
        await writer.WriteAsync(content.AsMemory(), CancellationToken.None);
    }

    public async Task<string?> SaveTextAsync(string suggestedFileName, string content, SaveFileType fileType,
        CancellationToken ct = default)
    {
        var storage = _topLevel.StorageProvider;
        if (storage is null || !storage.CanSave)
        {
            return null;
        }

        var file = await storage.SaveFilePickerAsync(BuildOptions(suggestedFileName, fileType));

        if (file is null)
        {
            return null;
        }

        // Cancellation MUST be observed before OpenWriteAsync: that call truncates the target, so checking
        // afterwards would leave the user's existing file emptied by an export that never wrote a byte.
        // This is the last point at which cancelling is free — the content is already fully in hand, so
        // there is no long-running work left to abandon.
        ct.ThrowIfCancellationRequested();
        await using var stream = await file.OpenWriteAsync();
        await WriteTextAsync(stream, content);
        return file.Path.IsAbsoluteUri ? file.Path.LocalPath : file.Name;
    }
}
