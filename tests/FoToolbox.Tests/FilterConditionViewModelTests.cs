using QueryBuilderPlugin;
using Xunit;
using FoToolbox.Core.OData;

namespace FoToolbox.Tests;

public class FilterConditionViewModelTests
{
    [Theory]
    [InlineData("10", "10")]
    [InlineData(" 10 ", "10")]
    [InlineData("true", "true")]
    [InlineData("FALSE", "false")]
    [InlineData("null", "null")]
    [InlineData("'NZMF'", "'NZMF'")]
    [InlineData("NZMF", "'NZMF'")]
    public void FormatValue_Uses_Typed_Literals_For_Eq(string input, string expected)
    {
        var vm = new FilterConditionViewModel
        {
            Field = "dataAreaId",
            Operator = "eq",
            Value = input
        };

        var ast = vm.ToAst();
        var cond = Assert.IsType<FilterCondition>(ast);
        Assert.Equal(expected, cond.Value);
    }

    [Fact]
    public void FormatValue_Quotes_Function_Arguments()
    {
        var vm = new FilterConditionViewModel
        {
            Field = "Name",
            Operator = "contains",
            Value = "foo"
        };

        var ast = vm.ToAst();
        var cond = Assert.IsType<FilterCondition>(ast);
        Assert.Equal("'foo'", cond.Value);
    }
}
