using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// POST Builder (control-map §7): compose a POST/PATCH/DELETE against an OData path and view the
/// response. The send goes through <see cref="IODataClient"/>.
/// </summary>
public partial class PostBuilderViewModel : ObservableObject
{
    private readonly IODataClient _client;

    public IReadOnlyList<string> Methods { get; } = new[] { "POST", "PATCH", "DELETE" };

    [ObservableProperty]
    private string _method = "POST";

    [ObservableProperty]
    private string _path = "/data/CustomersV3";

    [ObservableProperty]
    private string _requestBody = "{\n  \"dataAreaId\": \"USMF\",\n  \"CustomerAccount\": \"US-9001\"\n}";

    [ObservableProperty]
    private string _responseBody = string.Empty;

    [ObservableProperty]
    private string _statusText = "No response yet.";

    [ObservableProperty]
    private bool _isBusy;

    public PostBuilderViewModel(IODataClient client) => _client = client;

    // IncludeCancelCommand: surfaces SendCancelCommand and lets the generated AsyncRelayCommand carry
    // the token's lifecycle, so an in-flight send can be cancelled on navigate-away/shutdown once a
    // live IODataClient replaces the fake.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Send(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Sending…";
        try
        {
            var body = string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase) ? null : RequestBody;
            var response = await _client.SendAsync(Method, Path, body, ct);
            StatusText = response.StatusLine;
            ResponseBody = response.Body;
        }
        catch (Exception ex)
        {
            StatusText = "Request failed.";
            ResponseBody = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
