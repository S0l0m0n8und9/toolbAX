using System;
using System.Linq;
using System.Threading;
using FoToolbox.Core.DualWrite;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Contract test for <see cref="FakeDualWriteCompareService"/> (#168 parking lot). The class doc claims
/// the seed exercises every <see cref="DualWriteComparisonVerdict"/>, but nothing enforced it: #178 added
/// <see cref="DualWriteComparisonVerdict.Ambiguous"/> and the seed was never updated, so design mode and
/// every default-fake test silently lost coverage of the "cannot compare" row. This pins the claim so the
/// next verdict addition fails here instead of drifting the same way.
/// </summary>
public class FakeDualWriteCompareServiceTests
{
    [Fact]
    public async System.Threading.Tasks.Task Seed_has_at_least_one_row_for_every_verdict()
    {
        var env = new EnvProfile("e", "Env", "https://x", "t", "USMF", "Tier", EnvStatus.Connected);
        var rows = await new FakeDualWriteCompareService().CompareAsync(env, env, CancellationToken.None);

        var seededVerdicts = rows.Select(r => r.Verdict).ToHashSet();

        foreach (var verdict in Enum.GetValues<DualWriteComparisonVerdict>())
        {
            Assert.Contains(verdict, seededVerdicts);
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task Every_ambiguous_row_carries_a_non_empty_note()
    {
        // Ambiguous rows exist specifically to report WHY a row could not be compared (#178); a note-less
        // one would render "cannot compare" with nothing for the reader to hover.
        var env = new EnvProfile("e", "Env", "https://x", "t", "USMF", "Tier", EnvStatus.Connected);
        var rows = await new FakeDualWriteCompareService().CompareAsync(env, env, CancellationToken.None);

        Assert.All(
            rows.Where(r => r.Verdict == DualWriteComparisonVerdict.Ambiguous),
            r => Assert.False(string.IsNullOrWhiteSpace(r.Note)));
    }
}
