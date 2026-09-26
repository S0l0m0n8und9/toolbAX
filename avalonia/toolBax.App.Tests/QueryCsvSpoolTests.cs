using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class QueryCsvSpoolTests
{
    private sealed class TriggeredColumns(Action trigger, Exception? failure = null) : IReadOnlyList<string>
    {
        public int Count => 1;
        public string this[int index] => index == 0 ? "A" : throw new ArgumentOutOfRangeException(nameof(index));
        public IEnumerator<string> GetEnumerator() => Enumerate().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        private IEnumerable<string> Enumerate()
        {
            trigger();
            if (failure is not null) throw failure;
            yield return "A";
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"toolbax-query-spool-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void AssertEmpty() => Assert.Empty(Directory.EnumerateFileSystemEntries(Path));
        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: false);
        }
    }

    [Fact]
    public async Task Staged_bytes_equal_QueryCsv_Build_for_late_ordinal_columns_and_special_values()
    {
        using var directory = new TempDirectory();
        var firstColumns = new[] { "A", "a", "Formula", "Null", "Dash", "Unicode" };
        var finalColumns = new[] { "A", "a", "Formula", "Null", "Dash", "Unicode", "B" };
        var rows = new[]
        {
            new QueryResultRow(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["A"] = "upper", ["a"] = "lower", ["Formula"] = "=1+1", ["Null"] = null,
                ["Dash"] = "—", ["Unicode"] = "雪"
            }),
            new QueryResultRow(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["A"] = "second", ["B"] = "late"
            })
        };

        await using (var spool = QueryCsvSpool.Create(directory.Path))
        {
            await spool.AppendAsync(rows[0], firstColumns, TestContext.Current.CancellationToken);
            await spool.AppendAsync(rows[1], finalColumns, TestContext.Current.CancellationToken);
            var staged = await spool.CompleteAsync(finalColumns, TestContext.Current.CancellationToken);
            using var actual = new MemoryStream();
            await staged.CopyToAsync(actual, TestContext.Current.CancellationToken);

            var expectedText = QueryCsv.Build(finalColumns, rows);
            var expected = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(expectedText)).ToArray();
            Assert.Equal(expected, actual.ToArray());
            Assert.True(staged.CanRead);
        }

        directory.AssertEmpty();
    }

    [Fact]
    public async Task Row_spool_retention_is_bounded_to_the_current_input_page()
    {
        using var directory = new TempDirectory();
        var weak = new List<WeakReference>();
        await using (var spool = QueryCsvSpool.Create(directory.Path))
        {
            for (var page = 0; page < 20; page++)
            {
                for (var index = 0; index < 25; index++)
                {
                    var row = CreateTrackedRow(page, index, weak);
                    await spool.AppendAsync(row, new[] { "A" }, TestContext.Current.CancellationToken);
                }
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.InRange(weak.Count(reference => reference.IsAlive), 0, 25);
        }
        directory.AssertEmpty();
    }

    [Fact]
    public async Task Cancelled_staging_removes_owned_row_and_csv_temps()
    {
        using var directory = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        await using (var spool = QueryCsvSpool.Create(directory.Path))
        {
            await spool.AppendAsync(new QueryResultRow(new Dictionary<string, string?> { ["A"] = "1" }),
                new[] { "A" }, TestContext.Current.CancellationToken);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                spool.CompleteAsync(new[] { "A" }, cancellation.Token));
        }
        directory.AssertEmpty();
    }

    [Fact]
    public async Task Cancellation_after_csv_temp_creation_removes_both_owned_files()
    {
        using var directory = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        await using (var spool = QueryCsvSpool.Create(directory.Path))
        {
            await spool.AppendAsync(new QueryResultRow(new Dictionary<string, string?> { ["A"] = "1" }),
                new[] { "A" }, TestContext.Current.CancellationToken);
            var columns = new TriggeredColumns(cancellation.Cancel);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                spool.CompleteAsync(columns, cancellation.Token));
        }
        directory.AssertEmpty();
    }

    [Fact]
    public async Task Header_fault_after_csv_temp_creation_removes_both_owned_files()
    {
        using var directory = new TempDirectory();
        await using (var spool = QueryCsvSpool.Create(directory.Path))
        {
            await spool.AppendAsync(new QueryResultRow(new Dictionary<string, string?> { ["A"] = "1" }),
                new[] { "A" }, TestContext.Current.CancellationToken);
            var columns = new TriggeredColumns(() => { }, new InvalidOperationException("header failed"));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                spool.CompleteAsync(columns, TestContext.Current.CancellationToken));
            Assert.Equal("header failed", error.Message);
        }
        directory.AssertEmpty();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static QueryResultRow CreateTrackedRow(int page, int index, ICollection<WeakReference> weak)
    {
        var row = new QueryResultRow(new Dictionary<string, string?> { ["A"] = $"{page}:{index}" });
        weak.Add(new WeakReference(row));
        return row;
    }
}
