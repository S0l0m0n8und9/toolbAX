using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DualWriteComparePlugin;

/// <summary>Editable connection fields for one side of a comparison.</summary>
public sealed class ConnectionEditorViewModel : INotifyPropertyChanged
{
    private string _gatewayBaseUrl = string.Empty;
    private string _foIdentifier = string.Empty;
    private string _bearerToken = string.Empty;
    private string _summary = "Not configured.";

    public ConnectionEditorViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }
    public string Title { get; }

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

    public string BearerToken
    {
        get => _bearerToken;
        set { if (_bearerToken != value) { _bearerToken = value; OnPropertyChanged(); } }
    }

    public string Summary
    {
        get => _summary;
        set { if (_summary != value) { _summary = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
