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
    private ProfilesTab _selectedTab = ProfilesTab.FoEnvironment;

    public ObservableCollection<ProfileItem> Profiles { get; } = new();
    public Array AuthModeValues { get; } = Enum.GetValues(typeof(AuthMode));

    /// <summary>
    /// Interactive (delegated user) token acquirer used by the "Sign in with Microsoft" route.
    /// Defaults to the real MSAL provider; tests substitute a fake.
    /// Setting this property resets <see cref="Broker"/> so the next access rebuilds with the new provider.
    /// </summary>
    private IInteractiveTokenProvider _interactiveTokenProvider = new MsalInteractiveTokenProvider();
    internal IInteractiveTokenProvider InteractiveTokenProvider
    {
        get => _interactiveTokenProvider;
        set { _interactiveTokenProvider = value; _broker = null; }
    }

    private AuthBroker? _broker;
    /// <summary>
    /// Lazily built from <see cref="InteractiveTokenProvider"/>; rebuilt whenever that property is set.
    /// Assign directly to inject a fully configured broker (e.g. in tests that need a complete fake).
    /// </summary>
    internal AuthBroker Broker
    {
        get => _broker ??= new AuthBroker(_vault, InteractiveTokenProvider);
        set => _broker = value;
    }

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

    public ProfilesTab SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (_selectedTab == value) return;
            _selectedTab = value;
            OnPropertyChanged();
        }
    }

    public string? ActiveEnvironmentId => _activeEnvId;

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
    public ICommand SetActiveProfileByItemCommand { get; }
    public ICommand TestFoConnectionCommand { get; }
    public ICommand TestCeConnectionCommand { get; }
    public ICommand AcquireFoBearerTokenCommand { get; }
    public ICommand AcquireCeBearerTokenCommand { get; }
    public ICommand AcquireFoTokenInteractiveCommand { get; }
    public ICommand AcquireCeTokenInteractiveCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<ConnectionTestedEventArgs>? ConnectionTested;

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
        SetActiveProfileByItemCommand = new RelayProfileCommand(async item =>
        {
            if (item is null) return;
            Selected = item;
            await SetActiveAsync();
        });
        TestFoConnectionCommand = new AsyncCommand(TestFoConnectionAsync);
        TestCeConnectionCommand = new AsyncCommand(TestCeConnectionAsync);
        AcquireFoBearerTokenCommand = new AsyncCommand(AcquireFoBearerTokenAsync);
        AcquireCeBearerTokenCommand = new AsyncCommand(AcquireCeBearerTokenAsync);
        AcquireFoTokenInteractiveCommand = new AsyncCommand(AcquireFoTokenInteractiveAsync);
        AcquireCeTokenInteractiveCommand = new AsyncCommand(AcquireCeTokenInteractiveAsync);
    }

    public async Task RefreshAsync()
    {
        try
        {
            await _profiles.EnsureCreatedAsync();

            Profiles.Clear();
            var envs = await _profiles.GetEnvironmentsAsync();
            _activeEnvId = await _profiles.GetDefaultEnvironmentIdAsync();
            OnPropertyChanged(nameof(ActiveEnvironmentId));

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
        var foSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.Interactive, null, null, AuthTarget.Fo);
        var ceSp = new ServicePrincipal(Guid.NewGuid().ToString("N"), envId, string.Empty, AuthMode.Interactive, null, null, AuthTarget.Dataverse);
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
                    OnPropertyChanged(nameof(ActiveEnvironmentId));
                }
                else
                {
                    _activeEnvId = null;
                    OnPropertyChanged(nameof(ActiveEnvironmentId));
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

    internal async Task<bool> SaveAsync(bool promptForPluginRefresh)
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

        if (!IsTenantIdValid(env.TenantId, out var foTenantValidationMessage))
        {
            Status = foTenantValidationMessage;
            return false;
        }

        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        if (!string.IsNullOrWhiteSpace(ceEnv.BaseUrl) && string.IsNullOrWhiteSpace(ceEnv.TenantId))
        {
            Status = "CE tenant ID is required when a Dataverse base URL is configured.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ceEnv.TenantId) && !IsTenantIdValid(ceEnv.TenantId, out var ceTenantValidationMessage))
        {
            Status = ceTenantValidationMessage.Replace("Tenant ID", "CE tenant ID", StringComparison.Ordinal);
            return false;
        }

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
            Selected.FoPrincipal.CertThumbprint = foSp.CertThumbprint;

            ceSp = await PersistCredentialsForPrincipalAsync(
                ceSp,
                PendingCeClientSecret,
                PendingCeBearerToken,
                secretRef => Selected.DataversePrincipal.SecretRef = secretRef,
                () => PendingCeClientSecret = null,
                () => PendingCeBearerToken = null,
                nameof(CeStoredCredentialStatus));
            Selected.DataversePrincipal.CertThumbprint = ceSp.CertThumbprint;

            await _profiles.UpsertServicePrincipalAsync(foSp);
            await _profiles.UpsertServicePrincipalAsync(ceSp);

            if (string.IsNullOrWhiteSpace(_activeEnvId))
            {
                await _profiles.SetDefaultEnvironmentAsync(env.Id);
                _activeEnvId = env.Id;
                OnPropertyChanged(nameof(ActiveEnvironmentId));
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
            OnPropertyChanged(nameof(ActiveEnvironmentId));

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

        var envId = env.Id;
        var success = false;
        string? detail = null;
        try
        {
            Status = "Testing FO connection...";
            var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingFoBearerToken, PendingFoClientSecret, AuthTarget.Fo);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var baseUrl = ResourceUrlNormalizer.NormalizeFoBaseUrl(env.BaseUrl);
            var resp = await http.GetAsync($"{baseUrl}/data", CancellationToken.None);
            if (resp.IsSuccessStatusCode)
            {
                Status = "FO connection OK.";
                success = true;
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                detail = $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
                Status = $"FO connection failed: {detail}\n{body}";
            }
        }
        catch (OperationCanceledException)
        {
            detail = "Sign-in timed out after 5 minutes.";
            Status = $"FO connection test failed: {detail}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FO connection test failed for {Env}", env.Name);
            detail = ex.Message;
            Status = $"FO connection test failed: {ex.Message}";
        }
        finally
        {
            ConnectionTested?.Invoke(this, new ConnectionTestedEventArgs
            {
                EnvironmentId = envId,
                Scope = ConnectionScope.FinanceAndOperations,
                Success = success,
                TestedAt = DateTimeOffset.UtcNow,
                Detail = detail,
            });
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

        var envId = env.ProfileId;
        var success = false;
        string? detail = null;
        try
        {
            Status = "Testing CE connection...";
            var token = await AcquireTokenForTestAsync(env.BaseUrl, env.TenantId, sp, PendingCeBearerToken, PendingCeClientSecret, AuthTarget.Dataverse);

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var apiBase = ResourceUrlNormalizer.BuildDataverseApiBaseUrl(env.BaseUrl);
            var resp = await http.GetAsync($"{apiBase}/WhoAmI", CancellationToken.None);
            if (resp.IsSuccessStatusCode)
            {
                Status = "CE connection OK.";
                success = true;
            }
            else
            {
                var body = await resp.Content.ReadAsStringAsync(CancellationToken.None);
                detail = $"{(int)resp.StatusCode} {resp.ReasonPhrase}";
                Status = $"CE connection failed: {detail}\n{body}";
            }
        }
        catch (OperationCanceledException)
        {
            detail = "Sign-in timed out after 5 minutes.";
            Status = $"CE connection test failed: {detail}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CE connection test failed for {Env}", env.ProfileId);
            detail = ex.Message;
            Status = $"CE connection test failed: {ex.Message}";
        }
        finally
        {
            ConnectionTested?.Invoke(this, new ConnectionTestedEventArgs
            {
                EnvironmentId = envId,
                Scope = ConnectionScope.Dataverse,
                Success = success,
                TestedAt = DateTimeOffset.UtcNow,
                Detail = detail,
            });
        }
    }

    internal Task AcquireFoTokenInteractiveAsync() => AcquireTokenInteractiveAsync(AuthTarget.Fo);

    internal Task AcquireCeTokenInteractiveAsync() => AcquireTokenInteractiveAsync(AuthTarget.Dataverse);

    private Task AcquireFoBearerTokenAsync() => AcquireBearerTokenAsync(AuthTarget.Fo);

    private Task AcquireCeBearerTokenAsync() => AcquireBearerTokenAsync(AuthTarget.Dataverse);

    public Task BeginInteractiveReauthAsync(string serviceName)
    {
        if (Selected is null)
        {
            Status = "Select a profile before starting re-authentication.";
            return Task.CompletedTask;
        }

        var target = string.Equals(serviceName, "Dataverse", StringComparison.OrdinalIgnoreCase)
            ? AuthTarget.Dataverse
            : AuthTarget.Fo;

        var authMode = target == AuthTarget.Fo
            ? Selected.FoPrincipal.AuthMode
            : Selected.DataversePrincipal.AuthMode;

        if (authMode == AuthMode.Interactive)
        {
            return AcquireTokenInteractiveAsync(target);
        }

        if (authMode != AuthMode.BearerToken)
        {
            var credentialLabel = authMode == AuthMode.ClientSecret ? "client secret" : "certificate settings";
            Status = $"{serviceName} re-authentication requires updated {credentialLabel} for this profile. Save the credential change, then Set active to refresh plugins.";
            return Task.CompletedTask;
        }

        return AcquireBearerTokenAsync(target);
    }

    private static string Side(AuthTarget target) => target == AuthTarget.Fo ? "FO" : "CE";

    private static string NormalizeResourceBaseUrl(AuthTarget target, string baseUrl) => target == AuthTarget.Fo
        ? ResourceUrlNormalizer.NormalizeFoBaseUrl(baseUrl)
        : ResourceUrlNormalizer.NormalizeDataverseResourceBaseUrl(baseUrl);

    /// <summary>
    /// Validates the prerequisites shared by both token-acquisition routes (az CLI and interactive
    /// MSAL). Sets <see cref="Status"/> and returns false when a prerequisite is missing.
    /// </summary>
    private bool TryGetBearerTokenAcquisitionInputs(
        AuthTarget target,
        FoEnvironment env,
        DataverseEnvironment ceEnv,
        bool requireClientId,
        out string baseUrl,
        out string tenantId,
        out string clientId)
    {
        var principal = target == AuthTarget.Fo ? Selected!.FoPrincipal : Selected!.DataversePrincipal;
        baseUrl = target == AuthTarget.Fo ? env.BaseUrl : ceEnv.BaseUrl;
        tenantId = target == AuthTarget.Fo ? env.TenantId : ceEnv.TenantId;
        clientId = principal.ClientId ?? string.Empty;

        if (principal.AuthMode != AuthMode.BearerToken && principal.AuthMode != AuthMode.Interactive)
        {
            Status = $"Switch {Side(target)} Auth mode to BearerToken to retrieve a bearer token.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(tenantId))
        {
            Status = $"{Side(target)} base URL and tenant ID are required to retrieve a bearer token.";
            return false;
        }

        if (requireClientId && string.IsNullOrWhiteSpace(clientId))
        {
            Status = $"{Side(target)} Client ID is required for interactive sign-in.";
            return false;
        }

        return true;
    }

    private async Task AcquireBearerTokenAsync(AuthTarget target)
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        if (!TryGetBearerTokenAcquisitionInputs(target, env, ceEnv, requireClientId: false, out var baseUrl, out var tenantId, out _))
        {
            return;
        }

        try
        {
            Status = $"Acquiring {Side(target)} bearer token via Azure CLI (az)...";
            var scope = $"{NormalizeResourceBaseUrl(target, baseUrl)}/.default";
            var token = await GetAzCliAccessTokenAsync(tenantId, scope, CancellationToken.None);
            await StoreAcquiredBearerTokenAsync(target, env, ceEnv, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Target} bearer token retrieval failed for {Env}", target.ToString(), env.Name);
            Status = $"{Side(target)} bearer token retrieval failed: {FormatForStatus(ex.Message)}";
        }
    }

    private async Task AcquireTokenInteractiveAsync(AuthTarget target)
    {
        if (Selected is null) return;

        var env = Selected.Environment.ToModel();
        var ceEnv = Selected.DataverseEnvironment.ToModel(env.Id);
        if (!TryGetBearerTokenAcquisitionInputs(target, env, ceEnv, requireClientId: true, out var baseUrl, out var tenantId, out var clientId))
        {
            return;
        }

        try
        {
            Status = $"Opening Microsoft sign-in for {Side(target)} in your browser...";
            var resourceBaseUrl = NormalizeResourceBaseUrl(target, baseUrl);
            // 5-minute timeout honours the broker's liveness contract: an abandoned browser window
            // must not hold the interactive gate indefinitely and wedge the Sign-in button.
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var result = await InteractiveTokenProvider.AcquireTokenAsync(
                new InteractiveTokenRequest(clientId, tenantId, resourceBaseUrl),
                cts.Token);
            await StoreAcquiredBearerTokenAsync(target, env, ceEnv, result.AccessToken);
        }
        catch (OperationCanceledException)
        {
            Status = $"{Side(target)} sign-in timed out after 5 minutes.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Target} interactive sign-in failed for {Env}", target.ToString(), env.Name);
            // Surface actionable guidance for the common app-registration misconfigurations
            // (public client / http://localhost redirect), falling back to the raw message.
            Status = InteractiveSignInError.Describe(ex) ?? $"{Side(target)} sign-in failed: {FormatForStatus(ex.Message)}";
        }
    }

    /// <summary>
    /// Shared tail for both token-acquisition routes: normalise, persist to the DPAPI vault via
    /// <see cref="SaveAsync"/>, report expiry, and refresh dependent plugins when the profile is active.
    /// For Interactive-mode principals the token is NOT stashed in <see cref="PendingFoBearerToken"/> /
    /// <see cref="PendingCeBearerToken"/> and no "bearer token saved" message is emitted.
    /// </summary>
    private async Task StoreAcquiredBearerTokenAsync(AuthTarget target, FoEnvironment env, DataverseEnvironment ceEnv, string rawToken)
    {
        var normalizedToken = BearerTokenText.Normalize(rawToken);

        var principal = target == AuthTarget.Fo ? Selected!.FoPrincipal : Selected!.DataversePrincipal;
        var isInteractive = principal.AuthMode == AuthMode.Interactive;

        if (!isInteractive)
        {
            // BearerToken mode: stash the pending token so SaveAsync will vault it.
            if (target == AuthTarget.Fo) PendingFoBearerToken = normalizedToken;
            else PendingCeBearerToken = normalizedToken;
        }

        var saveSucceeded = await SaveAsync(promptForPluginRefresh: false);
        if (!saveSucceeded)
        {
            return;
        }

        string tokenStatus;
        if (isInteractive)
        {
            tokenStatus = JwtInspector.TryGetExpiryUtc(normalizedToken, out var expiryUtc)
                ? $"Signed in. Token expires {expiryUtc.UtcDateTime:u}; renews silently."
                : "Signed in. Token renews silently.";
        }
        else
        {
            tokenStatus = JwtInspector.TryGetExpiryUtc(normalizedToken, out var expiryUtc)
                ? $"{Side(target)} bearer token acquired and saved. Expires {expiryUtc.UtcDateTime:u}."
                : $"{Side(target)} bearer token acquired and saved.";
        }

        if (IsSelectedProfileActive(env.Id))
        {
            if (ConfirmRefreshOtherPlugins())
            {
                var foSp = Selected!.FoPrincipal.ToModel(env.Id, AuthTarget.Fo);
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

    private async Task<string> AcquireTokenForTestAsync(
        string baseUrl,
        string tenantId,
        ServicePrincipal sp,
        string? pendingBearerToken,
        string? pendingClientSecret,
        AuthTarget target)
    {
        if (sp.AuthMode is AuthMode.ClientSecret or AuthMode.Certificate &&
            (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(sp.ClientId)))
        {
            throw new InvalidOperationException("Tenant ID and Client ID are required to test this auth mode.");
        }

        var resourceBase = NormalizeResourceBaseUrl(target, baseUrl);
        var request = new AuthTokenRequest(
            resourceBase,
            tenantId,
            sp,
            ServiceName: target == AuthTarget.Fo ? "Finance and Operations" : "Dataverse",
            PendingClientSecret: pendingClientSecret,
            PendingBearerToken: pendingBearerToken);

        // 5-minute timeout honours the broker's liveness contract: an abandoned browser sign-in
        // during "Test connection" must not hold the interactive gate forever and wedge the Test buttons.
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        return await Broker.AcquireTokenAsync(request, cts.Token);
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
        else if (principal.AuthMode == AuthMode.Interactive)
        {
            // Interactive auth acquires tokens via MSAL at runtime; the vault stores nothing.
            principal = principal with { CertThumbprint = null, SecretRef = null };
            updateSecretRef(null);
            OnPropertyChanged(statusPropertyName);
        }
        else if (principal.AuthMode == AuthMode.BearerToken)
        {
            principal = principal with { CertThumbprint = null };
            if (!string.IsNullOrWhiteSpace(pendingBearerToken))
            {
                var token = BearerTokenText.Normalize(pendingBearerToken);
                var expiresUtc = JwtInspector.TryGetExpiryUtc(token, out var expiryUtc) ? expiryUtc.UtcDateTime.ToString("o") : null;
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
            AuthMode.Interactive => "Signs you in via your browser when a tool first needs access, then renews silently. Requires a public-client app registration with an http://localhost redirect. No secret is stored.",
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

    public bool IsActive(ProfileItem? item) =>
        item is not null &&
        !string.IsNullOrWhiteSpace(_activeEnvId) &&
        string.Equals(_activeEnvId, item.Environment.Id, StringComparison.OrdinalIgnoreCase);

    private bool IsSelectedProfileActive(string envId)
    {
        return !string.IsNullOrWhiteSpace(_activeEnvId) &&
               string.Equals(_activeEnvId, envId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTenantIdValid(string tenantId, out string validationMessage)
    {
        var trimmed = tenantId.Trim();
        if (Guid.TryParse(trimmed, out _))
        {
            validationMessage = string.Empty;
            return true;
        }

        if (trimmed.Contains('.') && Uri.CheckHostName(trimmed) == UriHostNameType.Dns)
        {
            validationMessage = string.Empty;
            return true;
        }

        validationMessage = "Tenant ID must be a GUID or verified domain (for example, contoso.onmicrosoft.com).";
        return false;
    }

    private static bool ConfirmRefreshOtherPlugins()
    {
        // No running WPF application (unit tests / headless contexts): there is no UI to prompt,
        // and a modal MessageBox would block forever. Default to not refreshing — the user can
        // re-apply the profile. This keeps the view-model usable off the UI thread.
        if (Application.Current is null)
        {
            return false;
        }

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

internal sealed class RelayProfileCommand : ICommand
{
    private readonly Func<ProfileItem?, Task> _execute;
    public RelayProfileCommand(Func<ProfileItem?, Task> execute) => _execute = execute;
    public bool CanExecute(object? parameter) => true;
    public async void Execute(object? parameter) => await _execute(parameter as ProfileItem);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
