using System.Linq;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Verifies <see cref="DualWriteMapParser"/>'s solution + solution-component parsing (the Map Browser's
/// "filter by solution/publisher" read path). Pure logic, no UI/network → runs on Linux CI.
/// </summary>
public class DualWriteSolutionParserTests
{
    private const string SolutionsResponse = """
    {
      "@odata.nextLink": "https://x.crm.dynamics.com/api/data/v9.2/solutions?$skiptoken=p2",
      "value": [
        {
          "solutionid": "55555555-5555-5555-5555-555555555555",
          "uniquename": "cust_master",
          "friendlyname": "Customer Master",
          "version": "1.0.0.3",
          "_publisherid_value": "99999999-9999-9999-9999-999999999999",
          "_publisherid_value@OData.Community.Display.V1.FormattedValue": "Contoso (fallback)",
          "publisherid": { "uniquename": "contoso", "friendlyname": "Contoso Ltd" }
        },
        {
          "solutionid": "66666666-6666-6666-6666-666666666666",
          "uniquename": "default_no_publisher",
          "friendlyname": "",
          "version": "",
          "publisherid": null
        }
      ]
    }
    """;

    [Fact]
    public void SolutionsPath_targets_solutions_with_publisher_expand_and_orderby()
    {
        var path = DualWriteMapParser.SolutionsPath();

        Assert.StartsWith("solutions?", path);
        Assert.Contains("$select=", path);
        Assert.Contains("uniquename", path);
        Assert.Contains("$expand=publisherid", path);
        Assert.Contains("$orderby=uniquename", path);
    }

    [Fact]
    public void ParseSolutionPage_reads_solution_and_expanded_publisher()
    {
        var page = DualWriteMapParser.ParseSolutionPage(SolutionsResponse);
        var sol = page.Solutions.Single(s => s.UniqueName == "cust_master");

        Assert.Equal("55555555-5555-5555-5555-555555555555", sol.Id);
        Assert.Equal("Customer Master", sol.FriendlyName);
        Assert.Equal("1.0.0.3", sol.Version);
        Assert.Equal("contoso", sol.PublisherUniqueName);
        Assert.Equal("Contoso Ltd", sol.PublisherDisplayName); // friendly name beats the formatted-value fallback
        Assert.Equal("Customer Master [cust_master] v1.0.0.3", sol.Display);
    }

    [Fact]
    public void ParseSolutionPage_handles_a_missing_publisher()
    {
        var page = DualWriteMapParser.ParseSolutionPage(SolutionsResponse);
        var sol = page.Solutions.Single(s => s.UniqueName == "default_no_publisher");

        Assert.Equal(string.Empty, sol.PublisherUniqueName);
        Assert.Equal("default_no_publisher", sol.Display); // no friendly name → unique name, no version suffix
    }

    [Fact]
    public void ParseSolutionPage_returns_the_next_link()
    {
        var page = DualWriteMapParser.ParseSolutionPage(SolutionsResponse);
        Assert.Equal("https://x.crm.dynamics.com/api/data/v9.2/solutions?$skiptoken=p2", page.NextLink);
    }

    [Fact]
    public void ParseSolutionPage_tolerates_garbage()
    {
        Assert.Empty(DualWriteMapParser.ParseSolutionPage(null).Solutions);
        Assert.Empty(DualWriteMapParser.ParseSolutionPage("nonsense").Solutions);
        Assert.Null(DualWriteMapParser.ParseSolutionPage("{}").NextLink);
    }

    [Fact]
    public void SolutionComponentsPath_filters_by_component_type_500_and_solution_unique_name()
    {
        var path = DualWriteMapParser.SolutionComponentsPath("my_solution");

        Assert.StartsWith("solutioncomponents?", path);
        Assert.Contains("objectid", path);
        // The filter is URL-encoded; decode to assert its content.
        var decoded = System.Uri.UnescapeDataString(path);
        Assert.Contains("componenttype eq 500", decoded);
        Assert.Contains("solutionid/uniquename eq 'my_solution'", decoded);
    }

    [Fact]
    public void SolutionComponentsPath_escapes_single_quotes_in_the_solution_name()
    {
        var path = DualWriteMapParser.SolutionComponentsPath("o'brien");
        var decoded = System.Uri.UnescapeDataString(path);

        Assert.Contains("'o''brien'", decoded); // OData doubles embedded single quotes
    }

    [Fact]
    public void ParseComponentIdPage_reads_object_ids_and_next_link()
    {
        const string json = """
        {
          "@odata.nextLink": "https://x/api/data/v9.2/solutioncomponents?$skiptoken=p2",
          "value": [
            { "objectid": "11111111-1111-1111-1111-111111111111" },
            { "objectid": "22222222-2222-2222-2222-222222222222" },
            { "objectid": "not-a-guid" }
          ]
        }
        """;

        var page = DualWriteMapParser.ParseComponentIdPage(json);

        Assert.Equal(2, page.ObjectIds.Count); // the non-guid is skipped
        Assert.Contains(System.Guid.Parse("11111111-1111-1111-1111-111111111111"), page.ObjectIds);
        Assert.Equal("https://x/api/data/v9.2/solutioncomponents?$skiptoken=p2", page.NextLink);
    }
}
