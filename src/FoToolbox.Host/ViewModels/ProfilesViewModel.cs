using FoToolbox.Core.Auth;
using FoToolbox.Core.Models;
using FoToolbox.Core.Profiles;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
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
    public ICommand AcquireBearerTokenCommand { get; }

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
        AcquireBearerTokenCommand = new AsyncCommand(AcquireBearerTokenAsync);
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
                var tokenProvider = new MsalTokenProvider(authorityBase, (_, _) => Task.FromResult(credential));
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

    private async Task AcquireBearerTokenAsync()
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var sp = Selected.Principal.ToModel(env.Id);

        if (sp.AuthMode != AuthMode.BearerToken)
        {
            Status = "Switch Auth mode to BearerToken to retrieve a bearer token.";
            return;
        }

        if (string.IsNullOrWhiteSpace(env.BaseUrl) ||
            string.IsNullOrWhiteSpace(env.TenantId))
        {
            Status = "Base URL and Tenant ID are required to retrieve a bearer token.";
            return;
        }

        try
        {
            Status = "Acquiring bearer token via Azure CLI (az)...";

             var baseUrl = NormalizeBaseUrl(env.BaseUrl);
             var scope = $"{baseUrl}/.default";
             var token = await GetAzCliAccessTokenAsync(env.TenantId, scope, CancellationToken.None);

            var normalizedToken = NormalizeBearerToken(token);
            PendingBearerToken = normalizedToken;
            await SaveAsync();

            // SaveAsync clears PendingBearerToken after persisting; use the normalized local token instead.
            if (TryGetJwtExpiryUtc(normalizedToken, out var expiryUtc))
            {
                Status = $"Bearer token acquired and saved. Expires {expiryUtc.UtcDateTime:u}.";
            }
            else
            {
                Status = "Bearer token acquired and saved.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bearer token retrieval failed for {Env}", env.Name);
            Status = $"Bearer token retrieval failed: {FormatForStatus(ex.Message)}";
        }
    }

    private static async Task<string> GetAzCliAccessTokenAsync(string tenantId, string scope, CancellationToken cancellationToken)
    {
        // Use PowerShell to invoke az, because az is typically a .cmd shim on Windows and may not start
        // reliably via ProcessStartInfo without shell semantics.
        //
        // We request a token for the given scope; if the CLI doesn't support --scope, fall back to --resource.
        //
        // Use tsv output (instead of JSON) to avoid parse issues when az prints warnings/preamble text.
        var tenantPs = EscapeForSingleQuotedPsString(tenantId);
        var scopePs = EscapeForSingleQuotedPsString(scope);

        var script = $@"
$ErrorActionPreference = 'Stop'
$tenant = '{tenantPs}'
$scope = '{scopePs}'

function Invoke-Az([string[]] $azArgs) {{
  $out = & az @azArgs 2>&1
  if ($LASTEXITCODE -ne 0) {{
    throw ($out | Out-String)
  }}
  return ($out | Out-String)
}}

try {{
  Invoke-Az @('account','get-access-token','--only-show-errors','--tenant',$tenant,'--scope',$scope,'--query','accessToken','--output','tsv')
}} catch {{
  $resource = $scope
  if ($resource.EndsWith('/.default')) {{
    $resource = $resource.Substring(0, $resource.Length - '/.default'.Length)
  }}
  Invoke-Az @('account','get-access-token','--only-show-errors','--tenant',$tenant,'--resource',$resource,'--query','accessToken','--output','tsv')
}}
".Trim();

        var output = await RunPowerShellEncodedAsync(script, cancellationToken);
        var token = output.Trim();

        // az sometimes still emits warnings even with --only-show-errors (depending on config/version).
        // If that happens, try to salvage the last non-empty line (token is a single line in tsv mode).
        if (token.Contains('\n') || token.Contains('\r'))
        {
            var lines = token
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();
            token = lines.Length == 0 ? string.Empty : lines[^1];
        }

        // Basic sanity: access token should look like a JWT (header.payload.signature) for AAD v2.
        if (string.IsNullOrWhiteSpace(token) || token.Count(c => c == '.') < 2)
        {
            throw new InvalidOperationException($"Azure CLI did not return a usable access token. Output:\n{RedactSecrets(output.Trim())}");
        }

        return token;
    }

    private static async Task<string> RunPowerShellEncodedAsync(string script, CancellationToken cancellationToken)
    {
        var bytes = Encoding.Unicode.GetBytes(script); // UTF-16LE for Windows PowerShell
        var encoded = Convert.ToBase64String(bytes);

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start PowerShell.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (proc.ExitCode != 0)
        {
            var msg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            msg = msg?.Trim();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(msg)
                ? $"PowerShell exited with code {proc.ExitCode}."
                : msg);
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }

    private static string EscapeForSingleQuotedPsString(string value) =>
        (value ?? string.Empty).Replace("'", "''");

    private static readonly Regex JwtLikeRegex = new(
        @"eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+",
        RegexOptions.Compiled);

    private static readonly Regex LongDotSeparatedTokenRegex = new(
        @"[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}",
        RegexOptions.Compiled);

    private static string RedactSecrets(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // AAD access tokens are JWT-like; redact those if they ever show up in error output.
        var redacted = JwtLikeRegex.Replace(text, "[REDACTED_JWT]");

        // Secondary pass: redact any suspiciously long three-part dot-separated blobs.
        redacted = LongDotSeparatedTokenRegex.Replace(redacted, "[REDACTED_TOKEN]");

        return redacted;
    }

    private static string FormatForStatus(string? message, int maxChars = 500)
    {
        var text = RedactSecrets(message ?? string.Empty).Trim();
        if (text.Length <= maxChars) return text;
        return text.Substring(0, maxChars) + "...";
    }

    private async Task<string> ResolveBearerTokenForTestAsync(ServicePrincipal sp)
    {
        if (!string.IsNullOrWhiteSpace(PendingBearerToken))
        {
            var normalized = NormalizeBearerToken(PendingBearerToken);
            if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException($"Bearer token expired at {expiryUtc:u}. Retrieve a fresh token.");
            }
            return normalized;
        }

        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var payload = await _vault.ReadSecretAsync<BearerTokenPayload>(sp.SecretRef);
            if (!string.IsNullOrWhiteSpace(payload?.AccessToken))
            {
                var normalized = NormalizeBearerToken(payload.AccessToken);
                if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
                {
                    throw new InvalidOperationException($"Stored bearer token expired at {expiryUtc:u}. Retrieve a fresh token.");
                }
                return normalized;
            }
        }

        var token = Environment.GetEnvironmentVariable("FOTB_BEARER_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            var normalized = NormalizeBearerToken(token);
            if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException($"FOTB_BEARER_TOKEN expired at {expiryUtc:u}. Set a fresh token.");
            }
            return normalized;
        }

        throw new InvalidOperationException("No bearer token found. Paste a token (Profiles → BearerToken) and Save, or set FOTB_BEARER_TOKEN.");
    }

    private async Task<FoToolbox.Core.Auth.ClientCredential> ResolveCredentialForTestAsync(ServicePrincipal sp)
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

        throw new InvalidOperationException("No client secret configured for this profile. Set it in Profiles and Save, or set FOTB_CLIENT_SECRET.");
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
        if (string.IsNullOrWhiteSpace(jwt)) return false;
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

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
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
