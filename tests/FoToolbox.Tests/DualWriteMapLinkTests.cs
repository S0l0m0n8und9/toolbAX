using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteMapLinkTests
{
    private const string Id = "11111111-1111-1111-1111-111111111111";

    [Fact]
    public void Builds_record_url_from_dataverse_url_and_map_id()
    {
        var url = DualWriteMapLink.BuildMapRecordUrl("https://contoso.crm.dynamics.com", Id);

        Assert.Equal(
            $"https://contoso.crm.dynamics.com/main.aspx?etn=msdyn_dualwriteentitymap&id={Id}&pagetype=entityrecord",
            url);
    }

    [Theory]
    [InlineData("https://contoso.crm.dynamics.com/")]
    [InlineData("https://contoso.crm.dynamics.com/api/data")]
    [InlineData("https://contoso.crm.dynamics.com/api/data/v9.2")]
    public void Strips_trailing_slash_and_api_data_suffix(string dataverseUrl)
    {
        var url = DualWriteMapLink.BuildMapRecordUrl(dataverseUrl, Id);

        Assert.StartsWith("https://contoso.crm.dynamics.com/main.aspx?", url);
        Assert.DoesNotContain("/api/data", url);
    }

    [Fact]
    public void Defaults_a_bare_host_to_https()
        => Assert.StartsWith("https://contoso.crm.dynamics.com/main.aspx?",
            DualWriteMapLink.BuildMapRecordUrl("contoso.crm.dynamics.com", Id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_when_no_dataverse_url(string? dataverseUrl)
        => Assert.Null(DualWriteMapLink.BuildMapRecordUrl(dataverseUrl, Id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void Null_when_map_id_missing_or_not_a_guid(string? mapId)
        => Assert.Null(DualWriteMapLink.BuildMapRecordUrl("https://contoso.crm.dynamics.com", mapId));
}
