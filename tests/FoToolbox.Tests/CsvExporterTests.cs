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

        var csv = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("1", csv);
        Assert.DoesNotContain("2", csv);
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
}
