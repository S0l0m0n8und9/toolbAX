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
    private readonly Action<ProfileBundle> _applyProfile;

    private ProfileItem? _selected;
    private ServicePrincipalEditor? _selectedFoPrincipal;
    private ServicePrincipalEditor? _selectedCePrincipal;
    private string _status = "Load or create a profile to get started.";
    private string? _pendingFoClientSecret;
    private string? _pendingFoBearerToken;
    private string? _pendingCeClientSecret;
    private string? _pendingCeBearerToken;
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
                if (_selectedFoPrincipal is not null)
                {
                    _selectedFoPrincipal.PropertyChanged -= OnSelectedPrincipalChanged;
                }

                if (_selectedCePrincipal is not null)
                {
                    _selectedCePrincipal.PropertyChanged -= OnSelectedPrincipalChanged;
                }

                _selected = value;
                _selectedFoPrincipal = value?.FoPrincipal;
                _selectedCePrincipal = value?.DataversePrincipal;
                if (_selectedFoPrincipal is not null)
                {
                    _selectedFoPrincipal.PropertyChanged += OnSelectedPrincipalChanged;
                }
                if (_selectedCePrincipal is not null)
                {
                    _selectedCePrincipal.PropertyChanged += OnSelectedPrincipalChanged;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(FoStoredCredentialStatus));
                OnPropertyChanged(nameof(CeStoredCredentialStatus));
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

    public string? PendingFoClientSecret
    {
        get => _pendingFoClientSecret;
        set
        {
            if (_pendingFoClientSecret != value)
            {
                _pendingFoClientSecret = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PendingFoBearerToken
    {
        get => _pendingFoBearerToken;
        set
        {
            if (_pendingFoBearerToken != value)
            {
                _pendingFoBearerToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PendingCeClientSecret
    {
        get => _pendingCeClientSecret;
        set
        {
            if (_pendingCeClientSecret != value)
            {
                _pendingCeClientSecret = value;
                OnPropertyChanged();
            }
        }
    }

    public string? PendingCeBearerToken
    {
        get => _pendingCeBearerToken;
        set
        {
            if (_pendingCeBearerToken != value)
            {
                _pendingCeBearerToken = value;
                OnPropertyChanged();
            }
        }
    }

    public string FoStoredCredentialStatus
    {
        get => BuildStoredCredentialStatus(Selected?.FoPrincipal);
    }

    public string CeStoredCredentialStatus => BuildStoredCredentialStatus(Selected?.DataversePrincipal);

    public ICommand RefreshCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SetActiveCommand { get; }
    public ICommand TestFoConnectionCommand { get; }
    public ICommand TestCeConnectionCommand { get; }
    public ICommand AcquireFoBearerTokenCommand { get; }
    public ICommand AcquireCeBearerTokenCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProfilesViewModel(string dbPath, ILogger logger, Action<ProfileBundle> applyProfile)
    {
        _store = new ProfileStore(dbPath);
        _profiles = new ProfileService(_store);
        _vault = new SecretVaultService(_store.ConnectionString);
        _logger = logger;
        _applyProfile = applyProfile;

        RefreshCommand = new AsyncCommand(RefreshAsync);
        AddProfileCommand = new AsyncCommand(AddAsync);
        DeleteProfileCommand = new AsyncCommand(DeleteAsync);
        SaveCommand = new AsyncCommand(async () => { await SaveAsync(promptForPluginRefresh: true); });
        SetActiveCommand = new AsyncCommand(SetActiveAsync);
        TestFoConnectionCommand = new AsyncCommand(TestFoConnectionAsync);
        TestCeConnectionCommand = new AsyncCommand(TestCeConnectionAsync);
        AcquireFoBearerTokenCommand = new AsyncCommand(AcquireFoBearerTokenAsync);
        AcquireCeBearerTokenCommand = new AsyncCommand(AcquireCeBearerTokenAsync);
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
                var bundle = await _profiles.GetBundleAsync(env.Id);
                if (bundle is null)
                {
                    continue;
                }

                Profiles.Add(new ProfileItem(
                    new EnvironmentEditor(bundle.FoEnvironment),
                    new DataverseEnvironmentEditor(bundle.DataverseEnvironment),
                    new ServicePrincipalEditor(bundle.FoPrincipal),
                    new ServicePrincipalEditor(bundle.DataversePrincipal)));
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
        var env = new FoEnvironment(envId, "New environment", string.Empty, string.Empty, null);
        var ceEnv = new DataverseEnvironment(envId, string.Empty, string.Empty);
        var foSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Fo);
        var ceSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.ClientSecret, null, null, AuthTarget.Dataverse);
        var profile = new ProfileItem(new EnvironmentEditor(env), new DataverseEnvironmentEditor(ceEnv), new ServicePrincipalEditor(foSp), new ServicePrincipalEditor(ceSp));
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

    private async Task<bool> SaveAsync(bool promptForPluginRefresh)
    {
        if (Selected is null) return false;

        var env = Selected.Environment.ToModel();
        if (string.IsNullOrWhiteSpace(env.Name) ||
            string.IsNullOrWhiteSpace(env.BaseUrl) ||
            string.IsNullOrWhiteSpace(env.TenantId))
        {
            Status = "FO name, base URL, and tenant ID are required.";
            return false;
        }

        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        var foSp = Selected.FoPrincipal.ToModel(env.Id, AuthTarget.Fo);
        var ceSp = Selected.DataversePrincipal.ToModel(env.Id, AuthTarget.Dataverse);

        try
        {
            await _profiles.UpsertEnvironmentAsync(env);
            await _profiles.UpsertDataverseEnvironmentAsync(ceEnv);

            foSp = await PersistCredentialsForPrincipalAsync(
                foSp,
                PendingFoClientSecret,
                PendingFoBearerToken,
                secretRef => Selected.FoPrincipal.SecretRef = secretRef,
                () => PendingFoClientSecret = null,
                () => PendingFoBearerToken = null,
                nameof(FoStoredCredentialStatus));

            ceSp = await PersistCredentialsForPrincipalAsync(
                ceSp,
                PendingCeClientSecret,
                PendingCeBearerToken,
                secretRef => Selected.DataversePrincipal.SecretRef = secretRef,
                () => PendingCeClientSecret = null,
                () => PendingCeBearerToken = null,
                nameof(CeStoredCredentialStatus));

            await _profiles.UpsertServicePrincipalAsync(foSp);
            await _profiles.UpsertServicePrincipalAsync(ceSp);

            if (string.IsNullOrWhiteSpace(_activeEnvId))
            {
                await _profiles.SetDefaultEnvironmentAsync(env.Id);
                _activeEnvId = env.Id;
            }

            if (promptForPluginRefresh && IsSelectedProfileActive(env.Id))
            {
                if (ConfirmRefreshOtherPlugins())
                {
                    _applyProfile(new ProfileBundle(env, foSp, ceEnv, ceSp));
                    Status = "Saved. Other plugins are refreshing.";
                }
                else
                {
                    Status = "Saved. Other plugins were not refreshed.";
                }
            }
            else
            {
                Status = "Saved.";
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save profile {EnvId}", env.Id);
            Status = $"Save failed: {ex.Message}";
            return false;
        }
    }

    private async Task SetActiveAsync()
    {
        if (Selected is null) return;

        var saveSucceeded = await SaveAsync(promptForPluginRefresh: false);
        if (!saveSucceeded || Selected is null)
        {
            return;
        }

        var env = Selected.Environment.ToModel();
        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        var foSp = Selected.FoPrincipal.ToModel(env.Id, AuthTarget.Fo);
        var ceSp = Selected.DataversePrincipal.ToModel(env.Id, AuthTarget.Dataverse);

        try
        {
            await _profiles.SetDefaultEnvironmentAsync(env.Id);
            _activeEnvId = env.Id;

            if (ConfirmRefreshOtherPlugins())
            {
                _applyProfile(new ProfileBundle(env, foSp, ceEnv, ceSp));
                Status = "Active profile updated. Other plugins are refreshing.";
            }
            else
            {
                Status = "Active profile updated. Other plugins were not refreshed.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set active profile {EnvId}", env.Id);
            Status = $"Set active failed: {ex.Message}";
        }
    }

    private async Task TestFoConnectionAsync()
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var sp = Selected.FoPrincipal.ToModel(env.Id, AuthTarget.Fo);

        if (string.IsNullOrWhiteSpace(env.BaseUrl))
        {
            Status = "FO base URL is required to test a connection.";
            return;
        }

        try
        {
            Status = "Testing FO connection...";
            var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingFoBearerToken, PendingFoClientSecret, "FOTB_BEARER_TOKEN", "FOTB_CLIENT_SECRET", AuthTarget.Fo);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var baseUrl = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl);
            var resp = await http.GetAsync($"{baseUrl}/data", CancellationToken.None);
            if (resp.IsSuccessStatusCode)
            {
                Status = "FO connection OK.";
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                Status = $"FO connection failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FO connection test failed for {Env}", env.Name);
            Status = $"FO connection test failed: {ex.Message}";
        }
    }

    private async Task TestCeConnectionAsync()
    {
        if (Selected is null) return;

        var env = Selected.DataverseEnvironment.ToModel(Selected.Environment.Id);
        var sp = Selected.DataversePrincipal.ToModel(env.ProfileId, AuthTarget.Dataverse);

        if (string.IsNullOrWhiteSpace(env.BaseUrl) || string.IsNullOrWhiteSpace(env.TenantId))
        {
            Status = "CE base URL and tenant ID are required to test a connection.";
            return;
        }

        try
        {
            Status = "Testing CE connection...";
            var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingCeBearerToken, PendingCeClientSecret, "FOTB_CE_BEARER_TOKEN", "FOTB_CE_CLIENT_SECRET", AuthTarget.Dataverse);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(env.BaseUrl);
            var resp = await http.GetAsync($"{apiBase}/WhoAmI", CancellationToken.None);
            if (resp.IsSuccessStatusCode)
            {
                Status = "CE connection OK.";
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                Status = $"CE connection failed: {(int)resp.StatusCode} {resp.ReasonPhrase}\n{body}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CE connection test failed for {Env}", env.ProfileId);
            Status = $"CE connection test failed: {ex.Message}";
        }
    }

    private Task AcquireFoBearerTokenAsync() => AcquireBearerTokenAsync(AuthTarget.Fo);

    private Task AcquireCeBearerTokenAsync() => AcquireBearerTokenAsync(AuthTarget.Dataverse);

    private async Task AcquireBearerTokenAsync(AuthTarget target)
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        var targetEnvBaseUrl = target == AuthTarget.Fo ? env.BaseUrl : ceEnv.BaseUrl;
        var targetTenantId = target == AuthTarget.Fo ? env.TenantId : ceEnv.TenantId;
        var targetSp = target == AuthTarget.Fo
            ? Selected.FoPrincipal.ToModel(env.Id, AuthTarget.Fo)
            : Selected.DataversePrincipal.ToModel(env.Id, AuthTarget.Dataverse);

        if (targetSp.AuthMode != AuthMode.BearerToken)
        {
            Status = $"Switch {(target == AuthTarget.Fo ? "FO" : "CE")} Auth mode to BearerToken to retrieve a bearer token.";
            return;
        }

        if (string.IsNullOrWhiteSpace(targetEnvBaseUrl) || string.IsNullOrWhiteSpace(targetTenantId))
        {
            Status = $"{(target == AuthTarget.Fo ? "FO" : "CE")} base URL and tenant ID are required to retrieve a bearer token.";
            return;
        }

        try
        {
            Status = $"Acquiring {(target == AuthTarget.Fo ? "FO" : "CE")} bearer token via Azure CLI (az)...";
            var resourceBaseUrl = target == AuthTarget.Fo
                ? ResourceUrlNormalizer.NormalizeFoBaseUrl(targetEnvBaseUrl)
                : ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(targetEnvBaseUrl);

            var scope = $"{resourceBaseUrl}/.default";
            var token = await GetAzCliAccessTokenAsync(targetTenantId, scope, CancellationToken.None);
            var normalizedToken = NormalizeBearerToken(token);

            if (target == AuthTarget.Fo) PendingFoBearerToken = normalizedToken;
            else PendingCeBearerToken = normalizedToken;

            var saveSucceeded = await SaveAsync(promptForPluginRefresh: false);
            if (!saveSucceeded)
            {
                return;
            }

            string tokenStatus;
            if (TryGetJwtExpiryUtc(normalizedToken, out var expiryUtc))
            {
                tokenStatus = $"{(target == AuthTarget.Fo ? "FO" : "CE")} bearer token acquired and saved. Expires {expiryUtc.UtcDateTime:u}.";
            }
            else
            {
                tokenStatus = $"{(target == AuthTarget.Fo ? "FO" : "CE")} bearer token acquired and saved.";
            }

            if (IsSelectedProfileActive(env.Id))
            {
                if (ConfirmRefreshOtherPlugins())
                {
                    var foSp = Selected.FoPrincipal.ToModel(env.Id, AuthTarget.Fo);
                    var savedCeSp = Selected.DataversePrincipal.ToModel(env.Id, AuthTarget.Dataverse);
                    _applyProfile(new ProfileBundle(env, foSp, ceEnv, savedCeSp));
                    Status = $"{tokenStatus} Other plugins are refreshing.";
                }
                else
                {
                    Status = $"{tokenStatus} Other plugins were not refreshed.";
                }
            }
            else
            {
                Status = tokenStatus;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Target} bearer token retrieval failed for {Env}", target.ToString(), env.Name);
            Status = $"{(target == AuthTarget.Fo ? "FO" : "CE")} bearer token retrieval failed: {FormatForStatus(ex.Message)}";
        }
    }

    private async Task<string> AcquireTokenForTestAsync(
        string baseUrl,
        string tenantId,
        ServicePrincipal sp,
        string? pendingBearerToken,
        string? pendingClientSecret,
        string bearerTokenEnvVar,
        string clientSecretEnvVar,
        AuthTarget target)
    {
        if (sp.AuthMode == AuthMode.BearerToken)
        {
            return await ResolveBearerTokenForTestAsync(sp, pendingBearerToken, bearerTokenEnvVar);
        }

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sp.ClientId))
        {
            throw new InvalidOperationException("Tenant ID and Client ID are required to test this auth mode.");
        }

        var authorityBase = "https://login.microsoftonline.com";
        var credential = await ResolveCredentialForTestAsync(sp, pendingClientSecret, clientSecretEnvVar);
        var tokenProvider = new MsalTokenProvider(authorityBase, (_, _) => Task.FromResult(credential));
        var auth = new AuthService(tokenProvider);
        var resourceBase = target == AuthTarget.Fo
            ? ResourceUrlNormalizer.NormalizeFoBaseUrl(baseUrl)
            : ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(baseUrl);
        return await auth.AcquireTokenAsync(resourceBase, tenantId, sp, CancellationToken.None);
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

    private async Task<string> ResolveBearerTokenForTestAsync(ServicePrincipal sp, string? pendingToken, string envVarName)
    {
        if (!string.IsNullOrWhiteSpace(pendingToken))
        {
            var normalized = NormalizeBearerToken(pendingToken);
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

        var token = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(token))
        {
            var normalized = NormalizeBearerToken(token);
            if (TryGetJwtExpiryUtc(normalized, out var expiryUtc) && expiryUtc <= DateTimeOffset.UtcNow)
            {
                throw new InvalidOperationException($"{envVarName} expired at {expiryUtc:u}. Set a fresh token.");
            }
            return normalized;
        }

        throw new InvalidOperationException($"No bearer token found. Paste a token and Save, or set {envVarName}.");
    }

    private async Task<FoToolbox.Core.Auth.ClientCredential> ResolveCredentialForTestAsync(ServicePrincipal sp, string? pendingClientSecret, string envVarName)
    {
        if (!string.IsNullOrWhiteSpace(pendingClientSecret))
        {
            return new ClientSecretCredential(pendingClientSecret);
        }

        if (!string.IsNullOrWhiteSpace(sp.SecretRef))
        {
            var payload = await _vault.ReadSecretAsync<ClientSecretPayload>(sp.SecretRef);
            if (!string.IsNullOrWhiteSpace(payload?.Value))
            {
                return new ClientSecretCredential(payload.Value);
            }
        }

        var secret = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            return new ClientSecretCredential(secret);
        }

        throw new InvalidOperationException($"No client secret configured for this profile. Set it in Profiles and Save, or set {envVarName}.");
    }

    private async Task<ServicePrincipal> PersistCredentialsForPrincipalAsync(
        ServicePrincipal principal,
        string? pendingClientSecret,
        string? pendingBearerToken,
        Action<string?> updateSecretRef,
        Action clearClientSecret,
        Action clearBearerToken,
        string statusPropertyName)
    {
        if (principal.AuthMode == AuthMode.ClientSecret)
        {
            principal = principal with { CertThumbprint = null };
            if (!string.IsNullOrWhiteSpace(pendingClientSecret))
            {
                var secretRef = await _vault.StoreSecretAsync("ClientSecret", new ClientSecretPayload { Value = pendingClientSecret });
                principal = principal with { SecretRef = secretRef };
                updateSecretRef(secretRef);
                clearClientSecret();
                OnPropertyChanged(statusPropertyName);
            }
            else if (!string.IsNullOrWhiteSpace(principal.SecretRef))
            {
                var payload = await _vault.ReadSecretAsync<ClientSecretPayload>(principal.SecretRef);
                if (string.IsNullOrWhiteSpace(payload?.Value))
                {
                    principal = principal with { SecretRef = null };
                    updateSecretRef(null);
                    OnPropertyChanged(statusPropertyName);
                }
            }
        }
        else if (principal.AuthMode == AuthMode.BearerToken)
        {
            principal = principal with { CertThumbprint = null };
            if (!string.IsNullOrWhiteSpace(pendingBearerToken))
            {
                var token = NormalizeBearerToken(pendingBearerToken);
                var expiresUtc = TryGetJwtExpiryUtc(token, out var expiryUtc) ? expiryUtc.UtcDateTime.ToString("o") : null;
                var secretRef = await _vault.StoreSecretAsync("BearerToken", new BearerTokenPayload { AccessToken = token, ExpiresUtc = expiresUtc });
                principal = principal with { SecretRef = secretRef };
                updateSecretRef(secretRef);
                clearBearerToken();
                OnPropertyChanged(statusPropertyName);
            }
            else if (!string.IsNullOrWhiteSpace(principal.SecretRef))
            {
                var payload = await _vault.ReadSecretAsync<BearerTokenPayload>(principal.SecretRef);
                if (string.IsNullOrWhiteSpace(payload?.AccessToken))
                {
                    principal = principal with { SecretRef = null };
                    updateSecretRef(null);
                    OnPropertyChanged(statusPropertyName);
                }
            }
        }

        return principal;
    }

    private static string BuildStoredCredentialStatus(ServicePrincipalEditor? principal)
    {
        if (principal is null) return string.Empty;
        return principal.AuthMode switch
        {
            AuthMode.BearerToken => principal.SecretRef is null or ""
                ? "No stored bearer token."
                : "Bearer token stored (DPAPI).",
            AuthMode.ClientSecret => principal.SecretRef is null or ""
                ? "No stored client secret."
                : "Client secret stored (DPAPI).",
            AuthMode.Certificate => string.IsNullOrWhiteSpace(principal.CertThumbprint)
                ? "No certificate thumbprint."
                : "Certificate thumbprint set.",
            _ => "No stored credential."
        };
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

    private bool IsSelectedProfileActive(string envId)
    {
        return !string.IsNullOrWhiteSpace(_activeEnvId) &&
               string.Equals(_activeEnvId, envId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ConfirmRefreshOtherPlugins()
    {
        var result = MessageBoxResult.No;
        RunOnUi(() =>
        {
            result = MessageBox.Show(
                "This update changes the active profile context. Refresh other plugins now?",
                "FOtoolbox",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);
        });

        return result == MessageBoxResult.Yes;
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
            OnPropertyChanged(nameof(FoStoredCredentialStatus));
            OnPropertyChanged(nameof(CeStoredCredentialStatus));
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
    public ProfileItem(EnvironmentEditor environment, DataverseEnvironmentEditor dataverseEnvironment, ServicePrincipalEditor foPrincipal, ServicePrincipalEditor dataversePrincipal)
    {
        Environment = environment;
        DataverseEnvironment = dataverseEnvironment;
        FoPrincipal = foPrincipal;
        DataversePrincipal = dataversePrincipal;
    }

    public EnvironmentEditor Environment { get; }
    public DataverseEnvironmentEditor DataverseEnvironment { get; }
    public ServicePrincipalEditor FoPrincipal { get; }
    public ServicePrincipalEditor DataversePrincipal { get; }
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

internal sealed class DataverseEnvironmentEditor : INotifyPropertyChanged
{
    private string _baseUrl;
    private string _tenantId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public DataverseEnvironmentEditor(DataverseEnvironment env)
    {
        ProfileId = env.ProfileId;
        _baseUrl = env.BaseUrl;
        _tenantId = env.TenantId;
    }

    public string ProfileId { get; }

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

    public DataverseEnvironment ToModel(string profileId) =>
        new(profileId, BaseUrl.Trim(), TenantId.Trim());

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
        Target = sp.Target;
    }

    public string Id { get; }
    public AuthTarget Target { get; }

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

    public ServicePrincipal ToModel(string envId, AuthTarget defaultTarget) =>
        new(
            Id,
            envId,
            ClientId.Trim(),
            AuthMode,
            string.IsNullOrWhiteSpace(SecretRef) ? null : SecretRef.Trim(),
            string.IsNullOrWhiteSpace(CertThumbprint) ? null : CertThumbprint.Trim(),
            Target == default ? defaultTarget : Target);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
