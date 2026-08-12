using System.IO;
using System.Linq;
using System.Text;
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
}
