using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Fidelity tests for <see cref="FakeODataClient"/> (#167). The fake used to ignore <c>$top</c>, the
/// caller's <see cref="CancellationToken"/> and paging entirely, so every screen-level test that cared
/// about those had to bring its own stub — and the standard fake stayed a client no real service
/// resembles. These pin the honest behaviour AND the unchanged default, since design mode and most
/// existing tests depend on the seeded single-page response.
/// </summary>
public class FakeODataClientTests
{
    // A property, not a static field: the ambient token is per-test, and caching the first test's token
    // in a static initialiser would hand every later test an already-cancelled one.
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<string> Accounts(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(row => row.GetProperty("CustomerAccount").GetString()!)
            .ToList();
    }

    private static string? NextLink(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("@odata.nextLink", out var link) ? link.GetString() : null;
    }

    // ── The default (design mode) is untouched ────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unconstrained_get_still_returns_every_seeded_row_in_one_page()
    {
        var response = await new FakeODataClient().SendAsync("GET", "/data/CustomersV3", null, Ct);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(new[] { "US-001", "US-002", "US-003" }, Accounts(response.Body).ToArray());
        Assert.Null(NextLink(response.Body));
    }

    [Theory]
    [InlineData("POST", "{\"a\":1}", 201)]
    [InlineData("PATCH", "{\"a\":1}", 204)]
    [InlineData("DELETE", null, 204)]
    [InlineData("PUT", null, 405)]
    public async Task The_per_verb_responses_are_unchanged(string verb, string? body, int expected)
    {
        var response = await new FakeODataClient().SendAsync(verb, "/data/CustomersV3", body, Ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PATCH")]
    public async Task A_write_with_no_body_is_still_a_bad_request(string verb)
    {
        var response = await new FakeODataClient().SendAsync(verb, "/data/CustomersV3", "   ", Ct);

        Assert.Equal(400, response.StatusCode);
    }

    // ── $top ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Top_caps_the_rows_returned()
    {
        var response = await new FakeODataClient().SendAsync("GET", "/data/CustomersV3?$top=2", null, Ct);

        Assert.Equal(new[] { "US-001", "US-002" }, Accounts(response.Body).ToArray());
    }

    [Fact]
    public async Task Top_of_zero_returns_no_rows()
    {
        var response = await new FakeODataClient().SendAsync("GET", "/data/CustomersV3?$top=0", null, Ct);

        Assert.Empty(Accounts(response.Body));
    }

    [Fact]
    public async Task A_top_larger_than_the_seed_returns_what_there_is()
    {
        var response = await new FakeODataClient().SendAsync("GET", "/data/CustomersV3?$top=99", null, Ct);

        Assert.Equal(3, Accounts(response.Body).Count);
    }

    [Theory]
    [InlineData("$top=abc")]   // non-numeric — a server would reject it, not truncate to 0
    [InlineData("$top=-1")]
    [InlineData("$select=CustomerAccount")]
    public async Task A_top_that_is_absent_or_unusable_does_not_constrain_the_rows(string query)
    {
        var response = await new FakeODataClient().SendAsync("GET", $"/data/CustomersV3?{query}", null, Ct);

        Assert.Equal(3, Accounts(response.Body).Count);
    }

    [Fact]
    public async Task Top_is_read_from_a_multi_option_query()
    {
        var response = await new FakeODataClient()
            .SendAsync("GET", "/data/CustomersV3?$select=CustomerAccount&$top=1&cross-company=true", null, Ct);

        Assert.Single(Accounts(response.Body));
    }

    // ── Cancellation ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    public async Task An_already_cancelled_token_is_observed(string verb)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FakeODataClient().SendAsync(verb, "/data/CustomersV3", "{}", cts.Token));
    }

    // ── Opt-in server-driven paging ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Paging_serves_one_page_at_a_time_with_an_absolute_next_link()
    {
        var client = new FakeODataClient(pageSize: 2);

        var page1 = await client.SendAsync("GET", "/data/CustomersV3", null, Ct);

        Assert.Equal(new[] { "US-001", "US-002" }, Accounts(page1.Body).ToArray());
        var link = NextLink(page1.Body);
        Assert.NotNull(link);
        Assert.True(Uri.TryCreate(link, UriKind.Absolute, out _), $"nextLink must be absolute: {link}");

        // Followed verbatim, as CoreODataClient does with a server-supplied nextLink.
        var page2 = await client.SendAsync("GET", link!, null, Ct);

        Assert.Equal(new[] { "US-003" }, Accounts(page2.Body).ToArray());
        Assert.Null(NextLink(page2.Body));   // last page carries no link
    }

    [Fact]
    public async Task Paging_walks_every_row_exactly_once()
    {
        var client = new FakeODataClient(pageSize: 1);
        var seen = new List<string>();
        string? link = "/data/CustomersV3";
        var pages = 0;

        while (link is not null)
        {
            var response = await client.SendAsync("GET", link, null, Ct);
            seen.AddRange(Accounts(response.Body));
            link = NextLink(response.Body);
            pages++;
        }

        Assert.Equal(3, pages);
        Assert.Equal(new[] { "US-001", "US-002", "US-003" }, seen.ToArray());
    }

    [Fact]
    public async Task Paging_never_serves_past_the_requested_top()
    {
        // $top must survive the hop to the next page, or page 2 would re-derive the total from the full
        // seed and hand back rows the caller excluded.
        var client = new FakeODataClient(pageSize: 1);

        var page1 = await client.SendAsync("GET", "/data/CustomersV3?$top=2", null, Ct);
        Assert.Equal(new[] { "US-001" }, Accounts(page1.Body).ToArray());

        var page2 = await client.SendAsync("GET", NextLink(page1.Body)!, null, Ct);

        Assert.Equal(new[] { "US-002" }, Accounts(page2.Body).ToArray());
        Assert.Null(NextLink(page2.Body));   // the third seeded row is outside $top
    }

    [Fact]
    public void A_page_size_of_zero_or_less_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeODataClient(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FakeODataClient(-1));
    }

    // ── Through the real Query Builder, so the fake exercises the production paging path ──────────────

    [Fact]
    public async Task Load_more_pages_through_the_standard_fake()
    {
        var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(pageSize: 2));
        vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

        await vm.RunCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.ResultRows.Count);
        Assert.True(vm.HasMore);

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.ResultRows.Count);
        Assert.False(vm.HasMore);
    }
}
