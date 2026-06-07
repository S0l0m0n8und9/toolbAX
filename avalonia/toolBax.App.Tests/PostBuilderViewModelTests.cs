using System.Threading;
using System.Threading.Tasks;
using ToolBax.App.Services;
using ToolBax.App.ViewModels;
using ToolBax.Core.Services;
using Xunit;

namespace ToolBax.App.Tests;

public class PostBuilderViewModelTests
{
    private static PostBuilderViewModel MakeVm() => new(new FakeODataClient());

    private sealed class RecordingODataClient : IODataClient
    {
        public string? LastPath { get; private set; }
        public Task<ODataResponse> SendAsync(string method, string path, string? body, CancellationToken ct = default)
        {
            LastPath = path;
            return Task.FromResult(new ODataResponse(204, "No Content", string.Empty, 3));
        }
    }

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

    [Fact]
    public async Task Patch_returns_204()
    {
        var vm = MakeVm();
        vm.Method = "PATCH";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Contains("204", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void Cross_company_appends_to_the_request_url()
    {
        var vm = MakeVm();
        vm.Method = "PATCH";
        vm.Path = "/data/CustomersV3(...)";

        Assert.DoesNotContain("cross-company", vm.RequestUrl);

        vm.CrossCompany = true;

        Assert.Equal("PATCH /data/CustomersV3(...)?cross-company=true", vm.RequestUrl);
    }

    [Fact]
    public async Task Send_uses_the_cross_company_effective_path()
    {
        var recorder = new RecordingODataClient();
        var vm = new PostBuilderViewModel(recorder) { Method = "DELETE", Path = "/data/E(1)", CrossCompany = true };

        await vm.SendCommand.ExecuteAsync(null);

        Assert.Equal("/data/E(1)?cross-company=true", recorder.LastPath);
    }

    [Fact]
    public async Task Send_sets_the_success_badge()
    {
        var vm = MakeVm();
        vm.Method = "POST";

        await vm.SendCommand.ExecuteAsync(null);

        Assert.True(vm.SendSucceeded);
        Assert.Contains("201", vm.StatusBadge);
    }

    [Fact]
    public async Task Copy_url_and_payload_write_to_the_clipboard()
    {
        var clipboard = new FakeClipboardService();
        var vm = new PostBuilderViewModel(new FakeODataClient(), clipboard) { Path = "/data/E", CrossCompany = true };

        await vm.CopyUrlCommand.ExecuteAsync(null);
        Assert.Equal("/data/E?cross-company=true", clipboard.LastText);

        await vm.CopyPayloadCommand.ExecuteAsync(null);
        Assert.Equal(vm.RequestBody, clipboard.LastText);
    }
}
