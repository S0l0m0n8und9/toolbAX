using FoToolbox.Core.OData;
using Xunit;

namespace FoToolbox.Tests;

public class QuerySpecFilterTests
{
    [Fact]
    public void Renders_Filter_Ast()
    {
        var ast = new FilterGroup("and", new FilterNode[]
        {
            new FilterCondition("Name", "eq", "'Alice'"),
            new FilterGroup("or", new FilterNode[]
            {
                new FilterCondition("AccountNumber", "eq", "'A0001'"),
                new FilterCondition("AccountNumber", "eq", "'A0002'")
            })
        });

        var spec = new QuerySpec("Customers", Where: ast);
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Contains("$filter=%28Name%20eq%20%27Alice%27%20and%20%28AccountNumber%20eq%20%27A0001%27%20or%20AccountNumber%20eq%20%27A0002%27%29%29", req.Url);
    }

    [Fact]
    public void Company_Filter_Appends_To_Ast_When_CrossCompany_Off()
    {
        var ast = new FilterCondition("Name", "eq", "'Alice'");
        var spec = new QuerySpec("Customers", CrossCompany: false, Company: "USMF", Where: ast);
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Contains("$filter=%28dataAreaId%20eq%20%27USMF%27%29%20and%20%28Name%20eq%20%27Alice%27%29", req.Url);
    }

    [Fact]
    public void Renders_Function_Filter_Ast()
    {
        var ast = new FilterCondition("Name", "contains", "'foo'");
        var spec = new QuerySpec("Customers", Where: ast);
        var req = QueryBuilder.Build("https://contoso.operations.dynamics.com", spec);

        Assert.Contains("$filter=contains%28Name%2C%27foo%27%29", req.Url);
    }
}
