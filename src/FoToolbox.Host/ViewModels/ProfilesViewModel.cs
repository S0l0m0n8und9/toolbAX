using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FoToolbox.Host.ViewModels;

internal sealed class ProfilesViewModel : INotifyPropertyChanged
{
    private readonly ProfileStore _store;
    private readonly ProfileService _profiles;
    private readonly SecretVaultService _vault;
    private readonly ILogger _logger;
    private readonly Action<FoEnvironment, ServicePrincipal> _applyProfile;

    private ProfileItem? _selected;
    private ServicePrincipalEditor? _selectedPrincipal;
    private string _status = "Load or create a profile to get started.";
    private string? _pendingClientSecret;
    private string? _pendingBearerToken;
    private string? _activeEnvId;

    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public Array AuthModeValues { get; } = Enum.GetValues(typeof(AuthMode));

    public ProfileItem? Selected
    {
        get => _selected;
        set
        {
            if (!ReferenceEquals(_selected, value))
            {
                if (_selectedPrincipal is not null)
                {
                    _selectedPrincipal.PropertyChanged -= OnSelectedPrincipalChanged;
                }

                _selected = value;
                _selectedPrincipal = value?.Principal;
                if (_selectedPrincipal is not null)
                {
                    _selectedPrincipal.PropertyChanged += OnSelectedPrincipalChanged;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(StoredCredentialStatus));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    public string Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PendingClientSecret
    {
        get => _pendingClientSecret;
        set
        {
            if (_pendingClientSecret != value)
            {
                _pendingClientSecret = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PendingBearerToken
    {
        get => _pendingBearerToken;
        set
        {
            if (_pendingBearerToken != value)
            {
                _pendingBearerToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string StoredCredentialStatus
    {
        get
        {
            if (Selected is null) return string.Empty;
            return Selected.Principal.AuthMode switch
            {
                AuthMode.BearerToken => Selected.Principal.SecretRef is null or ""
                    ? "No stored bearer token."
                    : "Bearer token stored (DPAPI).",
                AuthMode.ClientSecret => Selected.Principal.SecretRef is null or ""
                    ? "No stored client secret."
                    : "Client secret stored (DPAPI).",
                AuthMode.Certificate => string.IsNullOrWhiteSpace(Selected.Principal.CertThumbprint)
                    ? "No certificate thumbprint."
                    : "Certificate thumbprint set.",
                _ => "No stored credential."
            };
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SetActiveCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfilesViewModel(string dbPath, ILogger logger, Action<FoEnvironment, ServicePrincipal> applyProfile)
    {
        _store = new ProfileStore(dbPath);
        _profiles = new ProfileService(_store);
        _vault = new SecretVaultService(_store.ConnectionString);
        _logger = logger;
        _applyProfile = applyProfile;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        AddProfileCommand = new AsyncCommand(AddAsync);
        DeleteProfileCommand = new AsyncCommand(DeleteAsync);
        SaveCommand = new AsyncCommand(SaveAsync);
        SetActiveCommand = new AsyncCommand(SetActiveAsync);
        TestConnectionCommand = new AsyncCommand(TestConnectionAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _profiles.EnsureCreatedAsync();

            Profiles.Clear();
            var envs = await _profiles.GetEnvironmentsAsync();
            _activeEnvId = await _profiles.GetDefaultEnvironmentIdAsync();

            foreach (var env in envs)
            {
                var sps = await _profiles.GetServicePrincipalsAsync(env.Id);
                var sp = sps.FirstOrDefault()
                         ?? new ServicePrincipal(Guid.NewGuid().ToString("N"), env.Id, string.Empty, AuthMode.ClientSecret, null, null);
                Profiles.Add(new ProfileItem(new EnvironmentEditor(env), new ServicePrincipalEditor(sp)));
            }

            Selected = Profiles.FirstOrDefault(p => p.Environment.Id == _activeEnvId) ?? Profiles.FirstOrDefault();

            if (Profiles.Count == 0)
            {
                Status = "No profiles yet. Click Add to create one.";
            }
            else if (Selected is not null)
            {
                Status = "Edit details, Save, then Set active.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles.");
            Status = $"Failed to load profiles: {ex.Message}";
        }
    }

    private async Task AddAsync()
    {
        var envId = Guid.NewGuid().ToString("N");
        var spId = Guid.NewGuid().ToString("N");
        var env = new FoEnvironment(envId, "New environment", string.Empty, string.Empty, null);
        var sp = new ServicePrincipal(spId, envId, string.Empty, AuthMode.ClientSecret, null, null);
        var profile = new ProfileItem(new EnvironmentEditor(env), new ServicePrincipalEditor(sp));
        Profiles.Add(profile);
        Selected = profile;
        Status = "New profile added. Fill in details and click Save.";
        await Task.CompletedTask;
    }

    private async Task DeleteAsync()
    {
        if (Selected is null) return;
        var envId = Selected.Environment.Id;

        try
        {
            await _profiles.DeleteEnvironmentAsync(envId);
            Profiles.Remove(Selected);

            // If we deleted the active profile, pick a new one.
            if (string.Equals(_activeEnvId, envId, StringComparison.OrdinalIgnoreCase))
            {
                var next = Profiles.FirstOrDefault();
                if (next is not null)
                {
                    await _profiles.SetDefaultEnvironmentAsync(next.Environment.Id);
                    _activeEnvId = next.Environment.Id;
                }
                else
                {
                    _activeEnvId = null;
                }
            }

            Selected = Profiles.FirstOrDefault(p => p.Environment.Id == _activeEnvId) ?? Profiles.FirstOrDefault();
            Status = "Profile deleted.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete profile {EnvId}", envId);
            Status = $"Delete failed: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var sp = Selected.Principal.ToModel(env.Id);

        try
        {
            await _profiles.UpsertEnvironmentAsync(env);

            if (sp.AuthMode == AuthMode.ClientSecret)
            {
                sp = sp with { CertThumbprint = null };
                if (!string.IsNullOrWhiteSpace(PendingClientSecret))
                {
                    var secretRef = await _vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = PendingClientSecret });
                    sp = sp with { SecretRef = secretRef };
                    Selected.Principal.SecretRef = secretRef;
                    PendingClientSecret = null;
                    OnPropertyChanged(nameof(StoredCredentialStatus));
                }
                else if (!string.IsNullOrWhiteSpace(sp.SecretRef))
                {
                    var payload = await _vault.ReadSecretAsync<ClientSecretPayload>(sp.SecretRef);
                    if (string.IsNullOrWhiteSpace(payload?.Value))
                    {
                        sp = sp with { SecretRef = null };
                        Selected.Principal.SecretRef = null;
                        OnPropertyChanged(nameof(StoredCredentialStatus));
                    }
                }
            }
            else if (sp.AuthMode == AuthMode.BearerToken)
            {
                sp = sp with { CertThumbprint = null };
                if (!string.IsNullOrWhiteSpace(PendingBearerToken))
                {
                    var token = NormalizeBearerToken(PendingBearerToken);
                    var expiresUtc = TryGetJwtExpiryUtc(token, out var expiryUtc) ? expiryUtc.UtcDateTime.ToString("o") : null;
                    var secretRef = await _vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = token, ExpiresUtc = expiresUtc });
                    sp = sp with { SecretRef = secretRef };
                    Selected.Principal.SecretRef = secretRef;
                    PendingBearerToken = null;
                    OnPropertyChanged(nameof(StoredCredentialStatus));
                }
                else if (!string.IsNullOrWhiteSpace(sp.SecretRef))
                {
                    var payload = await _vault.ReadSecretAsync<BearerTokenPayload>(sp.SecretRef);
                    if (string.IsNullOrWhiteSpace(payload?.AccessToken))
                    {
                        sp = sp with { SecretRef = null };
                        Selected.Principal.SecretRef = null;
                        OnPropertyChanged(nameof(StoredCredentialStatus));
                    }
                }
            }

            await _profiles.UpsertServicePrincipalAsync(sp);

            if (string.IsNullOrWhiteSpace(_activeEnvId))
            {
                await _profiles.SetDefaultEnvironmentAsync(env.Id);
                _activeEnvId = env.Id;
            }

            Status = "Saved.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save profile {EnvId}", env.Id);
            Status = $"Save failed: {ex.Message}";
        }
    }

    private async Task SetActiveAsync()
    {
        if (Selected is null) return;

        await SaveAsync();

        var env = Selected.Environment.ToModel();
        var sp = Selected.Principal.ToModel(env.Id);

        try
        {
            await _profiles.SetDefaultEnvironmentAsync(env.Id);
            _activeEnvId = env.Id;

            _applyProfile(env, sp);
            Status = "Active profile updated.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set active profile {EnvId}", env.Id);
            Status = $"Set active failed: {ex.Message}";
        }
    }

    private async Task TestConnectionAsync()
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var sp = Selected.Principal.ToModel(env.Id);

        if (string.IsNullOrWhiteSpace(env.BaseUrl))
        {
            Status = "Base URL is required to test a connection.";
            return;
        }

        try
        {
            Status = "Testing connection...";

            string token;
            if (sp.AuthMode == AuthMode.BearerToken)
            {
                token = await ResolveBearerTokenForTestAsync(sp);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(env.TenantId) || string.IsNullOrWhiteSpace(sp.ClientId))
                {
                    Status = "Tenant ID and Client ID are required to test this auth mode.";
                    return;
                }

                var authorityBase = "https://login.microsoftonline.com";
                var credential = await ResolveCredentialForTestAsync(sp);
                var tokenProvider = new MsalTokenProvider(authorityBase, _ => credential);
                var auth = new AuthService(tokenProvider);
                token = await auth.AcquireTokenAsync(env, sp, CancellationToken.None);
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var baseUrl = NormalizeBaseUrl(env.BaseUrl);
            var resp = await http.GetAsync($"{baseUrl}/data", CancellationToken.None);
            if (resp.IsSuccessStatusCode)
            {
                Status = "Connection OK.";
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                Status = $"Connection failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed for {Env}", env.Name);
            Status = $"Connection test failed: {ex.Message}";
        }
    }

    private async Task<string> ResolveBearerTokenForTestAsync(ServicePrincipal sp)
    {
        if (!string.IsNullOrWhiteSpace(PendingBearerToken))
        {
            return NormalizeBearerToken(PendingBearerToken);
        }

        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var payload = await _vault.ReadSecretAsync<BearerTokenPayload>(sp.SecretRef);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                return payload.AccessToken;
            }
        }

        var token = Environment.GetEnvironmentVariable("FOTB_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            return NormalizeBearerToken(token);
        }

        throw new InvalidOperationException("No bearer token found. Paste a token (Profiles → BearerToken) and Save, or set FOTB_BEARER_TOKEN.");
    }

    private async Task<ClientCredential> ResolveCredentialForTestAsync(ServicePrincipal sp)
    {
        if (!string.IsNullOrWhiteSpace(PendingClientSecret))
        {
            return new ClientSecretCredential(PendingClientSecret);
        }

        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var payload = await _vault.ReadSecretAsync<ClientSecretPayload>(sp.SecretRef);
            if (!string.IsNullOrWhiteSpace(payload?.Value))
            {
                return new ClientSecretCredential(payload.Value);
            }
        }

        var secret = Environment.GetEnvironmentVariable("FOTB_CLIENT_SECRET");
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return new ClientSecretCredential(secret);
        }

        return new ClientSecretCredential("dummy");
    }

    private static string NormalizeBearerToken(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["Bearer ".Length..];
        }

        var sb = new StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }

        return sb.ToString();
    }

    private static bool TryGetJwtExpiryUtc(string jwt, out DateTimeOffset expiryUtc)
    {
        expiryUtc = default;
        var parts = jwt.Split('.');
        if (parts.Length < 2) return false;

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            if (payloadBytes.Length == 0) return false;
            using var doc = JsonDocument.Parse(payloadBytes);
            if (!doc.RootElement.TryGetProperty("exp", out var expEl)) return false;
            if (!expEl.TryGetInt64(out var seconds)) return false;
            expiryUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        var pad = s.Length % 4;
        if (pad == 2) s += "==";
        else if (pad == 3) s += "=";
        else if (pad != 0) return Array.Empty<byte>();
        return Convert.FromBase64String(s);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var url = baseUrl.Trim();
        url = url.TrimEnd('/');
        if (url.EndsWith("/data", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^5];
        }
        return url;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnSelectedPrincipalChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ServicePrincipalEditor.AuthMode) ||
            e.PropertyName == nameof(ServicePrincipalEditor.SecretRef) ||
            e.PropertyName == nameof(ServicePrincipalEditor.CertThumbprint))
        {
            OnPropertyChanged(nameof(StoredCredentialStatus));
        }
    }

    private sealed class ClientSecretPayload
    {
        public string? Value { get; set; }
    }

    private sealed class BearerTokenPayload
    {
        public string? AccessToken { get; set; }
        public string? ExpiresUtc { get; set; }
    }
}

internal sealed class ProfileItem
{
    public ProfileItem(EnvironmentEditor environment, ServicePrincipalEditor principal)
    {
        Environment = environment;
        Principal = principal;
    }

    public EnvironmentEditor Environment { get; }
    public ServicePrincipalEditor Principal { get; }
}

internal sealed class EnvironmentEditor : INotifyPropertyChanged
{
    private string _name;
    private string _baseUrl;
    private string _tenantId;
    private string? _defaultCompany;

    public event PropertyChangedEventHandler? PropertyChanged;

    public EnvironmentEditor(FoEnvironment env)
    {
        Id = env.Id;
        _name = env.Name;
        _baseUrl = env.BaseUrl;
        _tenantId = env.TenantId;
        _defaultCompany = env.DefaultCompany;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string BaseUrl
    {
        get => _baseUrl;
        set { _baseUrl = value; OnPropertyChanged(); }
    }

    public string TenantId
    {
        get => _tenantId;
        set { _tenantId = value; OnPropertyChanged(); }
    }

    public string? DefaultCompany
    {
        get => _defaultCompany;
        set { _defaultCompany = value; OnPropertyChanged(); }
    }

    public FoEnvironment ToModel() =>
        new(Id, Name.Trim(), BaseUrl.Trim(), TenantId.Trim(), string.IsNullOrWhiteSpace(DefaultCompany) ? null : DefaultCompany.Trim());

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class ServicePrincipalEditor : INotifyPropertyChanged
{
    private string _clientId;
    private AuthMode _authMode;
    private string? _secretRef;
    private string? _certThumbprint;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ServicePrincipalEditor(ServicePrincipal sp)
    {
        Id = sp.Id;
        _clientId = sp.ClientId;
        _authMode = sp.AuthMode;
        _secretRef = sp.SecretRef;
        _certThumbprint = sp.CertThumbprint;
    }

    public string Id { get; }

    public string ClientId
    {
        get => _clientId;
        set { _clientId = value; OnPropertyChanged(); }
    }

    public AuthMode AuthMode
    {
        get => _authMode;
        set { _authMode = value; OnPropertyChanged(); }
    }

    public string? SecretRef
    {
        get => _secretRef;
        set { _secretRef = value; OnPropertyChanged(); }
    }

    public string? CertThumbprint
    {
        get => _certThumbprint;
        set { _certThumbprint = value; OnPropertyChanged(); }
    }

    public ServicePrincipal ToModel(string envId) =>
        new(Id, envId, ClientId.Trim(), AuthMode, string.IsNullOrWhiteSpace(SecretRef) ? null : SecretRef.Trim(), string.IsNullOrWhiteSpace(CertThumbprint) ? null : CertThumbprint.Trim());

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
