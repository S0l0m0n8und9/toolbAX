using FoToolbox.Core.Export;
using FoToolbox.Core.OData;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FoToolbox.Tests;

public class CsvExporterTests
{
    private sealed class RetentionProbeClient : IODataClient
    {
        public const int RowsPerPage = 25;
        private const int PageCount = 20;
        private readonly List<WeakReference> _rows = new();
        public int LiveRowsBeforeSourceCompleted { get; private set; }

        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            CancellationToken cancellationToken = default) => new ProbeEnumerable(this, cancellationToken);

        private sealed class ProbeEnumerable(RetentionProbeClient owner, CancellationToken cancellationToken)
            : IAsyncEnumerable<ODataPage>, IAsyncEnumerator<ODataPage>
        {
            private int _step;
            public ODataPage Current { get; private set; } = null!;
            public IAsyncEnumerator<ODataPage> GetAsyncEnumerator(CancellationToken ct = default) => this;
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;

            public ValueTask<bool> MoveNextAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                _step++;
                if (_step <= PageCount)
                {
                    Current = owner.CreateTrackedPage(_step);
                    return ValueTask.FromResult(true);
                }
                if (_step == PageCount + 1)
                {
                    Current = new ODataPage(Array.Empty<IReadOnlyDictionary<string, object?>>(), "inspection");
                    return ValueTask.FromResult(true);
                }
                if (_step == PageCount + 2)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    owner.LiveRowsBeforeSourceCompleted = owner._rows.Count(row => row.IsAlive);
                    Current = new ODataPage(Array.Empty<IReadOnlyDictionary<string, object?>>(), null);
                    return ValueTask.FromResult(true);
                }

                Current = null!;
                return ValueTask.FromResult(false);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private ODataPage CreateTrackedPage(int page)
        {
            var rows = new List<IReadOnlyDictionary<string, object?>>(RowsPerPage);
            for (var index = 0; index < RowsPerPage; index++)
            {
                var row = new Dictionary<string, object?> { ["A"] = $"{page}:{index}" };
                _rows.Add(new WeakReference(row));
                rows.Add(row);
            }
            return new ODataPage(rows, "next");
        }
    }

    private sealed class FakeClient : IODataClient
    {
        private readonly IReadOnlyList<ODataPage> _pages;
        public FakeClient(params ODataPage[] pages) => _pages = pages;

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            foreach (var page in _pages)
            {
                yield return page;
            }
        }
    }

    private sealed class DispatchProbeClient : IODataClient
    {
        public int Dispatches { get; private set; }

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Dispatches++;
            await Task.CompletedTask;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = "produced" }
            }, null);
        }
    }

    private sealed class SourceFaultClient : IODataClient
    {
        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = "spooled" }
            }, "next");
            throw new InvalidOperationException("source failed");
        }
    }

    private sealed class CancellationIgnoringClient : IODataClient
    {
        public bool SecondPageRequested { get; private set; }

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = "first" }
            }, "next");
            SecondPageRequested = true;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = "ignored" }
            }, null);
        }
    }

    private sealed class GatedCancellationIgnoringClient : IODataClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = "late" }
            }, null);
        }
    }

    private sealed class MutableCell(string text)
    {
        public string Text { get; set; } = text;
        public int RenderCount { get; private set; }
        public override string ToString()
        {
            RenderCount++;
            return Text;
        }
    }

    private sealed class MutatingClient(MutableCell cell) : IODataClient
    {
        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield return new ODataPage(new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["A"] = cell }
            }, "next");
            cell.Text = "after-page";
            yield return new ODataPage(Array.Empty<IReadOnlyDictionary<string, object?>>(), null);
        }
    }

    private sealed class TestSpoolDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"toolbax-h11-{Guid.NewGuid():N}");

        public TestSpoolDirectory() => Directory.CreateDirectory(Path);

        public void AssertEmpty() => Assert.Empty(Directory.EnumerateFileSystemEntries(Path));

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: false);
        }
    }

    private class FaultingWriteStream(Exception failure) : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw failure;
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(failure);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(failure);
        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class CancelOnWriteStream(CancellationTokenSource cancellation) : Stream
    {
        public bool Disposed { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => CancelAndThrow();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromException(CancelAndCreate());
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(CancelAndCreate());

        private void CancelAndThrow() => throw CancelAndCreate();
        private OperationCanceledException CancelAndCreate()
        {
            cancellation.Cancel();
            return new OperationCanceledException(cancellation.Token);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task Escapes_Commas_Quotes_And_Newlines()
    {
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "A", "plain" }, { "B", "comma,value" }, { "C", "quote\"value" }, { "D", "multi\nline" } }
        }, null);
        var client = new FakeClient(page);
        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(client, new QueryRequest("http://test"), ms);

        var csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"comma,value\"", csv);
        Assert.Contains("\"quote\"\"value\"", csv);
        Assert.Contains("\"multi\nline\"", csv);
    }

    [Fact]
    public async Task Respects_Cancellation_After_First_Page()
    {
        var page1 = new ODataPage(new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { { "A", "1" } } }, "next");
        var page2 = new ODataPage(new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { { "A", "2" } } }, null);
        var client = new FakeClient(page1, page2);
        await using var ms = new MemoryStream();
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CsvExporter.ExportAsync(client, new QueryRequest("http://test"), ms, _ => cts.Cancel(), cts.Token));

        // Cancelling now leaves an empty file rather than a partial one: the header can only be written
        // once every page has been read (it is the union of all rows' keys), so a cancelled export has no
        // header to write the buffered rows under.
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(ms.ToArray()).TrimStart('\uFEFF'));
    }

    [Fact]
    public async Task Exports_DataTable()
    {
        var table = new DataTable();
        table.Columns.Add("A");
        var row = table.NewRow();
        row["A"] = "value";
        table.Rows.Add(row);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportTableAsync(table, ms);
        var csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("A", csv);
        Assert.Contains("value", csv);
    }

    [Fact]
    public async Task Header_Includes_Columns_Missing_From_The_First_Row()
    {
        // Dataverse/F&O omit null properties, so row 1 is not a reliable column list: any column it
        // happens to lack was previously dropped from the header and from every row.
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "A", "a1" } },
            new Dictionary<string, object?> { { "A", "a2" }, { "B", "b2" } }
        }, null);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(new FakeClient(page), new QueryRequest("http://test"), ms);

        var lines = ReadLines(ms);
        Assert.Equal("A,B", lines[0]);
        Assert.Equal("a1,", lines[1]);
        Assert.Equal("a2,b2", lines[2]);
    }

    [Fact]
    public async Task Header_Includes_Columns_From_Later_Pages_When_The_First_Page_Is_Empty()
    {
        // An empty first page (a filtered/paged query can return one) used to produce an empty header
        // and therefore an empty cell for every value on every later page.
        var page1 = new ODataPage(new List<IReadOnlyDictionary<string, object?>>(), "next");
        var page2 = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "A", "a1" }, { "B", "b1" } }
        }, null);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(new FakeClient(page1, page2), new QueryRequest("http://test"), ms);

        var lines = ReadLines(ms);
        Assert.Equal("A,B", lines[0]);
        Assert.Equal("a1,b1", lines[1]);
    }

    [Fact]
    public async Task Header_Preserves_First_Seen_Column_Order_Across_Pages()
    {
        var page1 = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "B", "b1" }, { "A", "a1" } }
        }, "next");
        var page2 = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "C", "c2" }, { "A", "a2" } }
        }, null);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(new FakeClient(page1, page2), new QueryRequest("http://test"), ms);

        var lines = ReadLines(ms);
        Assert.Equal("B,A,C", lines[0]);
        Assert.Equal("b1,a1,", lines[1]);
        Assert.Equal(",a2,c2", lines[2]);
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("=cmd|'/c calc'!A1", "\"'=cmd|'/c calc'!A1\"")]
    public async Task Neutralises_Formula_Injection_In_Cells(string value, string expectedCell)
    {
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "A", value } }
        }, null);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(new FakeClient(page), new QueryRequest("http://test"), ms);

        Assert.Equal(expectedCell, ReadLines(ms)[1]);
    }

    [Fact]
    public async Task Neutralises_Formula_Injection_In_Headers_And_DataTable_Cells()
    {
        var table = new DataTable();
        table.Columns.Add("=BadHeader");
        var row = table.NewRow();
        row["=BadHeader"] = "-2+3";
        table.Rows.Add(row);

        await using var ms = new MemoryStream();
        await CsvExporter.ExportTableAsync(table, ms);

        var lines = ReadLines(ms);
        Assert.Equal("\"'=BadHeader\"", lines[0]);
        Assert.Equal("\"'-2+3\"", lines[1]);
    }

    private static string[] ReadLines(MemoryStream ms) =>
        new StreamReader(new MemoryStream(ms.ToArray()), Encoding.UTF8)
            .ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

    [Fact]
    public async Task Reports_Cumulative_Progress()
    {
        var page1 = new ODataPage(new List<IReadOnlyDictionary<string, object?>> { new Dictionary<string, object?> { { "A", "1" } } }, "next");
        var page2 = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { { "A", "2" } },
            new Dictionary<string, object?> { { "A", "3" } }
        }, null);

        var client = new FakeClient(page1, page2);
        var progress = new List<int>();

        await using var ms = new MemoryStream();
        await CsvExporter.ExportAsync(client, new QueryRequest("http://test"), ms, progress.Add);

        Assert.Equal(new[] { 1, 3 }, progress);
    }

    [Fact]
    public async Task Retains_no_more_than_one_input_page_before_the_lazy_source_finishes()
    {
        var client = new RetentionProbeClient();
        await using var output = new MemoryStream();

        await CsvExporter.ExportAsync(client, new QueryRequest("http://test"), output);

        Assert.InRange(client.LiveRowsBeforeSourceCompleted, 0, RetentionProbeClient.RowsPerPage);
    }

    [Fact]
    public async Task Renders_each_cell_once_when_the_page_arrives()
    {
        var cell = new MutableCell("at-page");
        await using var output = new MemoryStream();

        await CsvExporter.ExportAsync(new MutatingClient(cell), new QueryRequest("http://test"), output);

        Assert.Equal("at-page", ReadLines(output)[1]);
        Assert.Equal(1, cell.RenderCount);
    }

    [Fact]
    public async Task Matches_case_variant_columns_across_pages_and_preserves_first_spelling()
    {
        var first = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Name"] = "first" }
        }, "next");
        var second = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["name"] = "second" }
        }, null);
        await using var output = new MemoryStream();

        await CsvExporter.ExportAsync(new FakeClient(first, second), new QueryRequest("http://test"), output);

        Assert.Equal(new[] { "Name", "first", "second" }, ReadLines(output));
    }

    [Fact]
    public async Task Preserves_null_unicode_and_formula_guarding_through_the_spool()
    {
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["Empty"] = null, ["Unicode"] = "雪", ["Formula"] = "=1+1" }
        }, null);
        await using var output = new MemoryStream();

        await CsvExporter.ExportAsync(new FakeClient(page), new QueryRequest("http://test"), output);

        Assert.Equal(",雪,\"'=1+1\"", ReadLines(output)[1]);
    }

    [Fact]
    public async Task Readonly_output_is_rejected_before_producer_dispatch_and_remains_caller_owned()
    {
        var client = new DispatchProbeClient();
        var output = new MemoryStream(Encoding.UTF8.GetBytes("existing"), writable: false);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CsvExporter.ExportAsync(
            client, new QueryRequest("http://test"), output));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(0, client.Dispatches);
        Assert.True(output.CanRead);
        Assert.False(output.CanWrite);
        Assert.Equal("existing", Encoding.UTF8.GetString(output.ToArray()));
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Null_output_is_rejected_before_producer_dispatch()
    {
        var client = new DispatchProbeClient();

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() => CsvExporter.ExportAsync(
            client, new QueryRequest("http://test"), null!));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(0, client.Dispatches);
    }

    [Fact]
    public async Task Null_client_and_request_are_rejected_by_the_public_API()
    {
        await using var output = new MemoryStream();

        var clientException = await Assert.ThrowsAsync<ArgumentNullException>(() => CsvExporter.ExportAsync(
            null!, new QueryRequest("http://test"), output));
        Assert.Equal("client", clientException.ParamName);

        var client = new DispatchProbeClient();
        var requestException = await Assert.ThrowsAsync<ArgumentNullException>(() => CsvExporter.ExportAsync(
            client, null!, output));
        Assert.Equal("request", requestException.ParamName);
        Assert.Equal(0, client.Dispatches);
    }

    [Fact]
    public async Task Invalid_output_is_rejected_before_any_spool_creation_attempt()
    {
        var client = new DispatchProbeClient();
        var output = new MemoryStream(Array.Empty<byte>(), writable: false);
        var missingSpoolDirectory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"toolbax-h11-missing-{Guid.NewGuid():N}", "spool");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => CsvExporter.ExportAsyncForTest(
            client, new QueryRequest("http://test"), output, missingSpoolDirectory));

        Assert.Equal("output", exception.ParamName);
        Assert.Equal(0, client.Dispatches);
        Assert.False(Directory.Exists(missingSpoolDirectory));
        Assert.True(output.CanRead);
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Source_failure_leaves_prefilled_output_unchanged_and_removes_its_spool()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        await using var output = new MemoryStream(Encoding.UTF8.GetBytes("existing-output"), writable: true);
        output.Position = output.Length;
        var before = output.ToArray();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CsvExporter.ExportAsyncForTest(
            new SourceFaultClient(), new QueryRequest("http://test"), output, spoolDirectory.Path));

        Assert.Equal("source failed", error.Message);
        Assert.Equal(before, output.ToArray());
        spoolDirectory.AssertEmpty();
    }

    [Fact]
    public async Task Cancellation_ignoring_source_stops_before_next_page_and_leaves_output_unchanged()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        using var cancellation = new CancellationTokenSource();
        var client = new CancellationIgnoringClient();
        await using var output = new MemoryStream(Encoding.UTF8.GetBytes("existing-output"), writable: true);
        output.Position = output.Length;
        var before = output.ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CsvExporter.ExportAsyncForTest(
            client, new QueryRequest("http://test"), output, spoolDirectory.Path,
            _ => cancellation.Cancel(), cancellation.Token));

        Assert.False(client.SecondPageRequested);
        Assert.Equal(before, output.ToArray());
        spoolDirectory.AssertEmpty();
    }

    [Fact]
    public async Task Successful_export_removes_its_spool_and_leaves_caller_output_open()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        var output = new MemoryStream();
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["A"] = "1" }
        }, null);

        await CsvExporter.ExportAsyncForTest(new FakeClient(page), new QueryRequest("http://test"), output,
            spoolDirectory.Path);

        output.WriteByte(0x7f);
        Assert.True(output.CanWrite);
        spoolDirectory.AssertEmpty();
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Output_failure_removes_spool_without_disposing_caller_stream()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        var output = new FaultingWriteStream(new IOException("output failed"));
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["A"] = "1" }
        }, null);

        await Assert.ThrowsAsync<IOException>(() => CsvExporter.ExportAsyncForTest(
            new FakeClient(page), new QueryRequest("http://test"), output, spoolDirectory.Path));

        Assert.False(output.Disposed);
        spoolDirectory.AssertEmpty();
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Output_cancellation_removes_spool_without_disposing_caller_stream()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        using var cancellation = new CancellationTokenSource();
        var output = new CancelOnWriteStream(cancellation);
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["A"] = "1" }
        }, null);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CsvExporter.ExportAsyncForTest(
            new FakeClient(page), new QueryRequest("http://test"), output, spoolDirectory.Path,
            cancellationToken: cancellation.Token));

        Assert.False(output.Disposed);
        spoolDirectory.AssertEmpty();
        await output.DisposeAsync();
    }

    [Fact]
    public async Task Cancellation_ignoring_inflight_source_cannot_publish_its_late_page()
    {
        using var spoolDirectory = new TestSpoolDirectory();
        using var cancellation = new CancellationTokenSource();
        var client = new GatedCancellationIgnoringClient();
        await using var output = new MemoryStream(Encoding.UTF8.GetBytes("existing-output"), writable: true);
        output.Position = output.Length;
        var before = output.ToArray();

        var export = CsvExporter.ExportAsyncForTest(client, new QueryRequest("http://test"), output,
            spoolDirectory.Path, cancellationToken: cancellation.Token);
        await client.Entered.Task;
        cancellation.Cancel();
        client.Release.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => export);
        Assert.Equal(before, output.ToArray());
        spoolDirectory.AssertEmpty();
    }

    [Fact]
    public async Task Keeps_utf8_bom_and_platform_newlines()
    {
        var page = new ODataPage(new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["A"] = "1" }
        }, null);
        await using var output = new MemoryStream();

        await CsvExporter.ExportAsync(new FakeClient(page), new QueryRequest("http://test"), output);

        var bytes = output.ToArray();
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.EndsWith(Environment.NewLine, Encoding.UTF8.GetString(bytes));
    }
}
