using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using Xunit;

namespace ToolBax.App.Tests;

public class PostBuilderViewModelTests
{
    private static PostBuilderViewModel MakeVm() => new(new FakeODataClient());

    [Fact]
    public async Task Post_returns_201_and_echoes_the_body()
    {
        var vm = MakeVm();
        vm.Method = "POST";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("201", vm.StatusText);
        Assert.Contains("CustomerAccount", vm.ResponseBody);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Delete_returns_204()
    {
        var vm = MakeVm();
        vm.Method = "DELETE";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("204", vm.StatusText);
    }

    [Fact]
    public async Task Empty_post_body_is_rejected_with_400()
    {
        var vm = MakeVm();
        vm.Method = "POST";
        vm.RequestBody = "   ";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("400", vm.StatusText);
    }
}
