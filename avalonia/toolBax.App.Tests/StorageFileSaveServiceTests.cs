using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// The two decisions in <see cref="StorageFileSaveService"/> that aren't windowing: which file type the
/// picker is asked for, and the bytes that reach disk. Both were wrong for CSV (#168) — the picker
/// offered only <c>*.md</c> (so a Query Builder export saved as <c>CustomersV3.csv.md</c>) and the
/// writer emitted BOM-less UTF-8 (so Excel on Windows decoded it as ANSI).
/// </summary>
public class StorageFileSaveServiceTests
{
    private sealed class CancelOnFirstWriteStream(CancellationTokenSource cancellation) : MemoryStream
    {
        private int _writes;
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Interlocked.Increment(ref _writes) == 1) cancellation.Cancel();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Interlocked.Increment(ref _writes) == 1) cancellation.Cancel();
            base.Write(buffer);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref _writes) == 1) cancellation.Cancel();
            return base.WriteAsync(buffer, CancellationToken.None);
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _writes) == 1) cancellation.Cancel();
            return base.WriteAsync(buffer, offset, count, CancellationToken.None);
        }
    }

    private sealed class PartialThenCancelledStream : MemoryStream
    {
        private bool _failed;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            if (_failed) return ValueTask.FromException(new OperationCanceledException("destination cancelled"));
            _failed = true;
            var partial = buffer[..Math.Max(1, buffer.Length / 2)];
            base.Write(partial.Span);
            return ValueTask.FromException(new OperationCanceledException("destination cancelled"));
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        {
            if (_failed) return Task.FromException(new OperationCanceledException("destination cancelled"));
            _failed = true;
            var partial = Math.Max(1, count / 2);
            base.Write(buffer, offset, partial);
            return Task.FromException(new OperationCanceledException("destination cancelled"));
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_failed) throw new OperationCanceledException("destination cancelled");
            _failed = true;
            base.Write(buffer, offset, Math.Max(1, count / 2));
            throw new OperationCanceledException("destination cancelled");
        }
    }
    [Fact]
    public void A_csv_save_offers_the_csv_type_not_markdown()
    {
        var options = StorageFileSaveService.BuildOptions("CustomersV3.csv", SaveFileType.Csv);

        Assert.Equal("csv", options.DefaultExtension);
        var choice = Assert.Single(options.FileTypeChoices!);
        Assert.Equal("CSV", choice.Name);
        Assert.Equal(new[] { "*.csv" }, choice.Patterns);
        Assert.Equal("CustomersV3.csv", options.SuggestedFileName);
    }

    [Fact]
    public void A_markdown_save_still_offers_the_markdown_type()
    {
        var options = StorageFileSaveService.BuildOptions("customersv3_account.md", SaveFileType.Markdown);

        Assert.Equal("md", options.DefaultExtension);
        var choice = Assert.Single(options.FileTypeChoices!);
        Assert.Equal("Markdown", choice.Name);
        Assert.Equal(new[] { "*.md" }, choice.Patterns);
    }

    [Fact]
    public async Task A_saved_file_starts_with_the_utf8_byte_order_mark()
    {
        using var stream = new MemoryStream();

        await StorageFileSaveService.WriteTextAsync(stream, "Name\r\nAcme — Ltd");

        var bytes = stream.ToArray();
        // Without these three bytes Excel on Windows opens the .csv as ANSI and shows "â€"" for the
        // em-dash (and mojibake for every other non-ASCII character).
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        Assert.Equal("Name\r\nAcme — Ltd", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public async Task The_writer_leaves_the_stream_open_for_its_owner_to_dispose()
    {
        // SaveTextAsync owns the picker's write stream via `await using`; the helper must not close it
        // out from under that (nor from under a caller that wants to keep writing).
        using var stream = new MemoryStream();

        await StorageFileSaveService.WriteTextAsync(stream, "a");

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task Cancelled_picked_stream_never_opens_or_truncates_destination()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("complete staged csv"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var opens = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            StorageFileSaveService.CopyPickedStreamAsync(source, () =>
            {
                opens++;
                return Task.FromResult<Stream>(new MemoryStream());
            }, cancellation.Token));

        Assert.Equal(0, opens);
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Late_cancel_after_open_finishes_copy_and_leaves_source_open()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 100_000));
        using var source = new MemoryStream(bytes);
        using var cancellation = new CancellationTokenSource();
        var destination = new CancelOnFirstWriteStream(cancellation);

        await StorageFileSaveService.CopyPickedStreamAsync(source,
            () => Task.FromResult<Stream>(destination), cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(bytes, destination.ToArray());
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task Unreadable_borrowed_stream_is_rejected_before_destination_open()
    {
        var source = new MemoryStream();
        source.Dispose();
        var opens = 0;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            StorageFileSaveService.CopyPickedStreamAsync(source, () =>
            {
                opens++;
                return Task.FromResult<Stream>(new MemoryStream());
            }, TestContext.Current.CancellationToken));

        Assert.Equal(0, opens);
    }

    [Fact]
    public async Task Post_open_operation_cancellation_is_io_failure_after_partial_copy()
    {
        var bytes = Encoding.UTF8.GetBytes(new string('x', 100_000));
        using var source = new MemoryStream(bytes);
        var destination = new PartialThenCancelledStream();
        var opens = 0;

        var error = await Assert.ThrowsAsync<IOException>(() =>
            StorageFileSaveService.CopyPickedStreamAsync(source, () =>
            {
                opens++;
                return Task.FromResult<Stream>(destination);
            }, TestContext.Current.CancellationToken));

        Assert.Equal(1, opens);
        Assert.IsType<OperationCanceledException>(error.InnerException);
        Assert.InRange(destination.ToArray().Length, 1, bytes.Length - 1);
        Assert.True(source.CanRead);
    }
}
