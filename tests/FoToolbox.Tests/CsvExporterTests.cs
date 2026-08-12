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
}
