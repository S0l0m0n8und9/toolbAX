using FoToolbox.Core.OData;
using Xunit;

namespace FoToolbox.Tests;

public class ODataQueryBuilderTests
{
    [Fact]
    public void CrossCompany_On_Appends_Param()
    {
        var spec = new QuerySpec(Entity: "Customers", CrossCompany: true, Select: new[] { "AccountNumber" });
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Equal("https://contoso.operations.dynamics.com/data/Customers?$select=AccountNumber&cross-company=true", req.Url);
    }

    [Fact]
    public void CrossCompany_Off_Adds_Company_Filter()
    {
        var spec = new QuerySpec(Entity: "Customers", CrossCompany: false, Company: "USMF", Select: new[] { "AccountNumber" });
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Equal("https://contoso.operations.dynamics.com/data/Customers?$select=AccountNumber&$filter=dataAreaId%20eq%20%27USMF%27", req.Url);
    }

    [Fact]
    public void CrossCompany_Off_Appends_Company_When_Filter_Present()
    {
        var spec = new QuerySpec(Entity: "Customers", CrossCompany: false, Company: "USMF", Select: new[] { "AccountNumber" }, Filter: "AccountNumber eq 'A0001'");
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Equal("https://contoso.operations.dynamics.com/data/Customers?$select=AccountNumber&$filter=%28dataAreaId%20eq%20%27USMF%27%29%20and%20%28AccountNumber%20eq%20%27A0001%27%29", req.Url);
    }
}
