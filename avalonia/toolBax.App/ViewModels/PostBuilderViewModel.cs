using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Services;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// POST Builder (control-map §7): compose a POST/PATCH/DELETE against an OData path and view the
/// response. The send goes through <see cref="IODataClient"/>; cross-company appends the query option.
/// </summary>
public partial class PostBuilderViewModel : ObservableObject
{
    private readonly IODataClient _client;
    private readonly IClipboardService _clipboard;

    public IReadOnlyList<string> Methods { get; } = new[] { "POST", "PATCH", "DELETE" };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private string _method = "POST";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private string _path = "/data/CustomersV3";

    /// <summary>Apply the write across all legal entities (<c>cross-company=true</c>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequestUrl))]
    private bool _crossCompany;

    [ObservableProperty]
    private string _requestBody = "{\n  \"dataAreaId\": \"USMF\",\n  \"CustomerAccount\": \"US-9001\"\n}";

    [ObservableProperty]
    private string _responseBody = string.Empty;

    [ObservableProperty]
    private string _statusText = "No response yet.";

    /// <summary>True only when the last send returned a 2xx — gates the success badge.</summary>
    [ObservableProperty]
    private bool _sendSucceeded;

    /// <summary>The last send's "{code} {reason}".</summary>
    [ObservableProperty]
    private string _statusBadge = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public PostBuilderViewModel(IODataClient client, IClipboardService? clipboard = null)
    {
        _client = client;
        _clipboard = clipboard ?? new FakeClipboardService();
    }

    /// <summary>The full "{METHOD} {path}" line that will be sent (with cross-company applied).</summary>
    public string RequestUrl => $"{Method} {EffectivePath()}";

    // Appends cross-company to the user's path (respecting an existing query string).
    private string EffectivePath()
    {
        var path = Path.Trim();
        if (CrossCompany && path.Length > 0)
        {
            path += path.Contains('?', StringComparison.Ordinal) ? "&cross-company=true" : "?cross-company=true";
        }

        return path;
    }

    // IncludeCancelCommand: surfaces SendCancelCommand and lets the generated AsyncRelayCommand carry
    // the token's lifecycle, so an in-flight send can be cancelled on navigate-away/shutdown.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Send(CancellationToken ct)
    {
        IsBusy = true;
        StatusText = "Sending…";
        try
        {
            var body = string.Equals(Method, "DELETE", StringComparison.OrdinalIgnoreCase) ? null : RequestBody;
            var response = await _client.SendAsync(Method, EffectivePath(), body, ct);
            StatusText = response.StatusLine;
            StatusBadge = $"{response.StatusCode} {response.ReasonPhrase}";
            SendSucceeded = response.IsSuccess;
            ResponseBody = response.Body;
        }
        catch (Exception ex)
        {
            StatusText = "Request failed.";
            SendSucceeded = false;
            StatusBadge = string.Empty;
            ResponseBody = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task CopyUrl() => _clipboard.SetTextAsync(EffectivePath());

    [RelayCommand]
    private Task CopyPayload() => _clipboard.SetTextAsync(RequestBody);
}
