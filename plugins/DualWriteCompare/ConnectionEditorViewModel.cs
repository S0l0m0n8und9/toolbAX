using FoToolbox.Core.DualWrite;
using FoToolbox.Core.DualWrite.Auth;
using FoToolbox.SDK.Commands;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DualWriteComparePlugin;

/// <summary>
/// Editable connection fields for one side of a comparison, with interactive sign-in. The bearer
/// token is never entered in the UI — it is captured by the sign-in flow (which also resolves the
/// gateway base URL) and stored DPAPI-protected, keyed independently per side (#24).
/// </summary>
public sealed class ConnectionEditorViewModel : INotifyPropertyChanged
{
    private readonly DualWriteConnectionStore _store;
    private readonly Func<string, bool, Task<DualWriteSignInResult?>> _signInFlow;

    private string _gatewayBaseUrl = string.Empty;
    private string _foIdentifier = string.Empty;
    private string _summary = "Not configured.";

    internal ConnectionEditorViewModel(
        string key,
        string title,
        DualWriteConnectionStore store,
        Func<string, bool, Task<DualWriteSignInResult?>> signInFlow,
        Action<Exception> onError)
    {
        Key = key;
        Title = title;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _signInFlow = signInFlow ?? throw new ArgumentNullException(nameof(signInFlow));

        SignInCommand = new AsyncRelayCommand(ct => SignInAsync(clearCachedAccount: false, ct), onError);
        SwitchAccountCommand = new AsyncRelayCommand(ct => SignInAsync(clearCachedAccount: true, ct), onError);
        ClearTokenCommand = new AsyncRelayCommand(ClearTokenAsync, onError);
    }

    public string Key { get; }
    public string Title { get; }

    /// <summary>Interactive sign-in: captures the token and auto-discovers the gateway.</summary>
    public AsyncRelayCommand SignInCommand { get; }

    /// <summary>Forget the cached browser session and sign in again as a different account.</summary>
    public AsyncRelayCommand SwitchAccountCommand { get; }

    /// <summary>Remove the stored token/session for this side (keeps gateway URL + identifier).</summary>
    public AsyncRelayCommand ClearTokenCommand { get; }

    public string GatewayBaseUrl
    {
        get => _gatewayBaseUrl;
        set { if (_gatewayBaseUrl != value) { _gatewayBaseUrl = value; OnPropertyChanged(); } }
    }

    public string FoIdentifier
    {
        get => _foIdentifier;
        set { if (_foIdentifier != value) { _foIdentifier = value; OnPropertyChanged(); } }
    }

    public string Summary
    {
        get => _summary;
        set { if (_summary != value) { _summary = value; OnPropertyChanged(); } }
    }

    private async Task SignInAsync(bool clearCachedAccount, CancellationToken ct)
    {
        var identifier = FoIdentifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            Summary = "Set the F&O identifier before signing in.";
            return;
        }

        Summary = clearCachedAccount ? "Opening interactive sign-in (switch account)..." : "Opening interactive sign-in...";
        var result = await _signInFlow(identifier, clearCachedAccount);
        if (result is null)
        {
            Summary = "Sign-in cancelled or no token captured.";
            return;
        }

        var settings = new DualWriteConnectionSettings(Key, result.GatewayBaseUrl, identifier, result.Token.AccessToken)
        {
            RefreshToken = result.Token.RefreshToken,
            AccessTokenExpiryUtc = result.Token.ExpiresUtc
        };
        await _store.SaveAsync(settings, ct);
        GatewayBaseUrl = result.GatewayBaseUrl;
        Summary = $"Signed in. Gateway: {result.GatewayBaseUrl}";
    }

    private async Task ClearTokenAsync(CancellationToken ct)
    {
        var existing = await _store.GetAsync(Key, ct);
        await _store.SaveAsync(new DualWriteConnectionSettings(Key, existing.GatewayBaseUrl, existing.FoIdentifier, null), ct);
        Summary = "Sign-in session cleared. Sign in again before comparing.";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
