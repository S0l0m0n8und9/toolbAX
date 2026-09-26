using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Profiles screen (viewmodels-and-services §B): master list of environments with search, the active
/// selection, supported F&amp;O/Dataverse authentication, portal-only Data Integrator testing, and save.
/// </summary>
public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private readonly IProfileStore _store;
    private readonly ISecretStore _secrets;
    private readonly IAuthService _auth;
    private readonly IDualWriteGatewayTester _gatewayTester;
    private readonly IConnectionTester _connectionTester;
    private readonly Func<EnvProfile, Task<string?>> _requestActivation;
    private readonly Func<EnvProfile, EnvProfile, Task<string?>> _commitActiveIdentitySave;
    private readonly Func<string?> _mutationBlockReason;

    private int _testGeneration;
    private bool _disposed;

    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand TestDataverseConnectionCommand { get; }
    public IAsyncRelayCommand TestGatewayCommand { get; }
    public IRelayCommand TestConnectionCancelCommand => ((ProbeCommand)TestConnectionCommand).CancelCommand;
    public IRelayCommand TestDataverseConnectionCancelCommand => ((ProbeCommand)TestDataverseConnectionCommand).CancelCommand;
    public IRelayCommand TestGatewayCancelCommand => ((ProbeCommand)TestGatewayCommand).CancelCommand;

    [ObservableProperty]
    private string _foTestStatus = string.Empty;

    [ObservableProperty]
    private string _dataverseTestStatus = string.Empty;

    private enum ProbeKind
    {
        Fo,
        Dataverse,
        Gateway
    }

    public ObservableCollection<EnvProfile> Profiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    [NotifyPropertyChangedFor(nameof(SetActiveLabel))]
    [NotifyPropertyChangedFor(nameof(CanSetActive))]
    [NotifyPropertyChangedFor(nameof(HasSecret))]
    [NotifyPropertyChangedFor(nameof(HasDataverseSecret))]
    [NotifyPropertyChangedFor(nameof(HasLegacyDiConfiguration))]
    [NotifyPropertyChangedFor(nameof(CanStoreFoClientSecret))]
    [NotifyPropertyChangedFor(nameof(CanStoreDataverseClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowFoSecretSaveFirstHint))]
    [NotifyPropertyChangedFor(nameof(ShowDataverseSecretSaveFirstHint))]
    private EnvProfile? _selected;

    /// <summary>The Auth-tab client-secret entry. Stored explicitly, never loaded back or used by Test.</summary>
    [ObservableProperty]
    private string _secretInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    [NotifyPropertyChangedFor(nameof(SetActiveLabel))]
    [NotifyPropertyChangedFor(nameof(CanSetActive))]
    private string? _activeId;

    [ObservableProperty]
    private string _status = "Ready.";

    // Separate busy flags so each tab's spinner reflects only its own test (no cross-tab bleed) and a
    // test still in flight can't have its indicator cleared by the other finishing first.
    [ObservableProperty]
    private bool _isTestingFoConnection;

    [ObservableProperty]
    private bool _isTestingDataverseConnection;

    // Editable drafts of the selected profile's FO fields, committed by Save. Kept separate from the
    // immutable EnvProfile record so edits can be made (and discarded by re-selecting).
    [ObservableProperty]
    private string _draftName = string.Empty;

    [ObservableProperty]
    private string _draftUrl = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStoreFoClientSecret))]
    [NotifyPropertyChangedFor(nameof(CanStoreDataverseClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowFoSecretSaveFirstHint))]
    [NotifyPropertyChangedFor(nameof(ShowDataverseSecretSaveFirstHint))]
    private string _draftTenant = string.Empty;

    [ObservableProperty]
    private string _draftLegal = string.Empty;

    /// <summary>Environment type — Production / Non-production — backed by the free-text <c>Tier</c> field
    /// (item (b) of the redesign; the old free-text "Tier" box became this two-option dropdown).</summary>
    [ObservableProperty]
    private string _draftEnvironmentType = EnvProfile.NonProductionType;

    /// <summary>The fixed options for the "Environment type" dropdown.</summary>
    public string[] EnvironmentTypes { get; } = { EnvProfile.ProductionType, EnvProfile.NonProductionType };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataverseWebApi))]
    private string _draftDataverseUrl = string.Empty;

    // Dataverse drafts — a separate app reg from F&O.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDataverseDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(CanStoreDataverseClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowDataverseSecretSaveFirstHint))]
    private string _draftDataverseClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDataverseDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(IsDataverseClientSecretMode))]
    [NotifyPropertyChangedFor(nameof(CanStoreDataverseClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowDataverseSecretSaveFirstHint))]
    private FoAuthMode _draftDataverseAuthMode = FoAuthMode.Interactive;

    /// <summary>The Dataverse client-secret entry. Write-only, like the F&amp;O secret.</summary>
    [ObservableProperty]
    private string _dataverseSecretInput = string.Empty;

    // F&O drafts.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFoDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(CanStoreFoClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowFoSecretSaveFirstHint))]
    private string _draftClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFoDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(IsFoClientSecretMode))]
    [NotifyPropertyChangedFor(nameof(CanStoreFoClientSecret))]
    [NotifyPropertyChangedFor(nameof(ShowFoSecretSaveFirstHint))]
    private FoAuthMode _draftAuthMode = FoAuthMode.Interactive;

    public FoAuthMode[] AuthModes { get; } = { FoAuthMode.Interactive, FoAuthMode.ClientSecret };

    public FoAuthMode? SelectedFoAuthMode
    {
        get => IsSupportedAuthMode(DraftAuthMode) ? DraftAuthMode : null;
        set
        {
            if (value is { } mode && IsSupportedAuthMode(mode)) DraftAuthMode = mode;
        }
    }

    public FoAuthMode? SelectedDataverseAuthMode
    {
        get => IsSupportedAuthMode(DraftDataverseAuthMode) ? DraftDataverseAuthMode : null;
        set
        {
            if (value is { } mode && IsSupportedAuthMode(mode)) DraftDataverseAuthMode = mode;
        }
    }

    public bool HasUnsupportedFoAuthMode => !IsSupportedAuthMode(DraftAuthMode);

    public bool HasUnsupportedDataverseAuthMode => !IsSupportedAuthMode(DraftDataverseAuthMode);

    public bool CanEditFoClientId => !HasUnsupportedFoAuthMode;

    public bool CanEditDataverseClientId => !HasUnsupportedDataverseAuthMode;

    /// <summary>Show the "Microsoft default client ID" note while the F&amp;O auth is Interactive and the
    /// client ID is still the default (it's editable; changing it hides the note).</summary>
    public bool ShowFoDefaultClientIdNote =>
        DraftAuthMode == FoAuthMode.Interactive && DraftClientId == FoAuthModeExtensions.DefaultInteractiveClientId;

    public bool ShowDataverseDefaultClientIdNote =>
        DraftDataverseAuthMode == FoAuthMode.Interactive && DraftDataverseClientId == FoAuthModeExtensions.DefaultInteractiveClientId;

    /// <summary>Client-secret entry only applies to the supported app-only ClientSecret mode.</summary>
    public bool IsFoClientSecretMode => DraftAuthMode == FoAuthMode.ClientSecret;

    public bool IsDataverseClientSecretMode => DraftDataverseAuthMode == FoAuthMode.ClientSecret;

    public bool CanStoreFoClientSecret => SavedClientSecretContextMatches(
        Selected?.AuthMode, Selected?.ClientId, DraftAuthMode, DraftClientId, Selected?.Tenant, DraftTenant);

    public bool CanStoreDataverseClientSecret => SavedClientSecretContextMatches(
        Selected?.DataverseAuthMode, Selected?.DataverseClientId, DraftDataverseAuthMode,
        DraftDataverseClientId, Selected?.Tenant, DraftTenant);

    public bool ShowFoSecretSaveFirstHint => IsFoClientSecretMode && !CanStoreFoClientSecret;

    public bool ShowDataverseSecretSaveFirstHint =>
        IsDataverseClientSecretMode && !CanStoreDataverseClientSecret;

    private static bool SavedClientSecretContextMatches(FoAuthMode? savedMode, string? savedClientId,
        FoAuthMode draftMode, string? draftClientId, string? savedTenant, string? draftTenant)
    {
        var effectiveDraftClient = string.IsNullOrWhiteSpace(draftClientId) ? null : draftClientId;
        return savedMode == FoAuthMode.ClientSecret && draftMode == FoAuthMode.ClientSecret
            && !string.IsNullOrWhiteSpace(savedClientId)
            && string.Equals(effectiveDraftClient, savedClientId, StringComparison.Ordinal)
            && string.Equals(draftTenant, savedTenant, StringComparison.Ordinal);
    }

    [ObservableProperty]
    private bool _isTestingGateway;

    [ObservableProperty]
    private string _diStatus = string.Empty;

    [ObservableProperty]
    private string _legacyDiStatus = string.Empty;

    public ProfilesViewModel(
        IProfileStore store,
        ISecretStore? secrets = null,
        IInteractiveAuthBroker? broker = null,
        IAuthService? auth = null,
        IDualWriteGatewayTester? gatewayTester = null,
        IConnectionTester? connectionTester = null,
        Func<EnvProfile, Task<string?>>? requestActivation = null,
        Func<EnvProfile, EnvProfile, Task<string?>>? commitActiveIdentitySave = null,
        Func<string?>? mutationBlockReason = null)
    {
        _store = store;
        _secrets = secrets ?? new FakeSecretStore();
        _ = broker; // Retained constructor compatibility; portal testing uses IDualWriteGatewayTester.
        _auth = auth ?? new FakeAuthService();
        _gatewayTester = gatewayTester ?? new FakeDualWriteGatewayTester();
        _connectionTester = connectionTester ?? new FakeConnectionTester();
        _requestActivation = requestActivation ?? LocalActivationAsync;
        _commitActiveIdentitySave = commitActiveIdentitySave ?? LocalIdentitySaveAsync;
        _mutationBlockReason = mutationBlockReason ?? (() => null);
        Profiles = new ObservableCollection<EnvProfile>(store.GetAll());

        _activeId = store.ActiveId;
        _selected = Profiles.FirstOrDefault(p => p.Id == _activeId) ?? Profiles.FirstOrDefault();
        LoadDrafts(_selected);
        TestConnectionCommand = new ProbeCommand(ct => RunProbeAsync(ProbeKind.Fo, ct), CanStartProbe,
            busy => IsTestingFoConnection = busy);
        TestDataverseConnectionCommand = new ProbeCommand(ct => RunProbeAsync(ProbeKind.Dataverse, ct), CanStartProbe,
            busy => IsTestingDataverseConnection = busy);
        TestGatewayCommand = new ProbeCommand(ct => RunProbeAsync(ProbeKind.Gateway, ct), CanStartProbe,
            busy => IsTestingGateway = busy);
        PropertyChanged += OnTestContextChanged;
        Profiles.CollectionChanged += OnProfilesChanged;
    }

    private Task<string?> LocalActivationAsync(EnvProfile target)
    {
        try
        {
            _store.ActiveId = target.Id;
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>($"Couldn't switch environment: {ex.Message}");
        }
    }

    private Task<string?> LocalIdentitySaveAsync(EnvProfile before, EnvProfile after)
    {
        try
        {
            _store.Save(after);
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>($"Couldn't save '{after.Name}': {ex.Message}");
        }
    }

    // Selecting Interactive (MFA) defaults a blank client ID to Microsoft's global public client; an
    // already-entered ID is respected (item 4 — the field stays editable).
    partial void OnDraftAuthModeChanged(FoAuthMode value)
    {
        if (value == FoAuthMode.Interactive && string.IsNullOrWhiteSpace(DraftClientId))
        {
            DraftClientId = FoAuthModeExtensions.DefaultInteractiveClientId;
        }
        OnPropertyChanged(nameof(SelectedFoAuthMode));
        OnPropertyChanged(nameof(HasUnsupportedFoAuthMode));
        OnPropertyChanged(nameof(CanEditFoClientId));
    }

    partial void OnDraftDataverseAuthModeChanged(FoAuthMode value)
    {
        if (value == FoAuthMode.Interactive && string.IsNullOrWhiteSpace(DraftDataverseClientId))
        {
            DraftDataverseClientId = FoAuthModeExtensions.DefaultInteractiveClientId;
        }
        OnPropertyChanged(nameof(SelectedDataverseAuthMode));
        OnPropertyChanged(nameof(HasUnsupportedDataverseAuthMode));
        OnPropertyChanged(nameof(CanEditDataverseClientId));
    }

    private static bool IsSupportedAuthMode(FoAuthMode mode) =>
        mode is FoAuthMode.Interactive or FoAuthMode.ClientSecret;

    partial void OnSelectedChanged(EnvProfile? oldValue, EnvProfile? newValue)
    {
        LoadDrafts(newValue);
        SecretInput = string.Empty; // never carry an entry across environments
        DataverseSecretInput = string.Empty;
        DiStatus = string.Empty;
        if (!string.Equals(oldValue?.Id, newValue?.Id, StringComparison.Ordinal))
        {
            LegacyDiStatus = string.Empty;
        }
        OnPropertyChanged(nameof(HasDiSecret));
        OnPropertyChanged(nameof(HasLegacyDiConfiguration));
    }

    private void LoadDrafts(EnvProfile? profile)
    {
        DraftName = profile?.Name ?? string.Empty;
        DraftUrl = profile?.Url ?? string.Empty;
        DraftTenant = profile?.Tenant ?? string.Empty;
        DraftLegal = profile?.Legal ?? string.Empty;
        DraftEnvironmentType = EnvProfile.NormalizeEnvironmentType(profile?.Tier);
        DraftDataverseUrl = profile?.DataverseUrl ?? string.Empty;
        DraftDataverseClientId = profile?.DataverseClientId ?? string.Empty;
        DraftDataverseAuthMode = profile?.DataverseAuthMode ?? FoAuthMode.Interactive;
        DraftClientId = profile?.ClientId ?? string.Empty;
        DraftAuthMode = profile?.AuthMode ?? FoAuthMode.Interactive;
    }

    /// <summary>Derived Dataverse Web API endpoint from the edited CE base URL (empty when none).</summary>
    public string DataverseWebApi =>
        string.IsNullOrWhiteSpace(DraftDataverseUrl)
            ? string.Empty
            : $"{DraftDataverseUrl.TrimEnd('/')}/api/data/v9.2";

    public IEnumerable<EnvProfile> Filtered =>
        string.IsNullOrWhiteSpace(Search)
            ? Profiles
            : Profiles.Where(p =>
                p.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                p.Legal.Contains(Search, StringComparison.OrdinalIgnoreCase));

    public bool IsSelectedActive => Selected is not null && Selected.Id == ActiveId;

    /// <summary>Header "Set active" button label — reads "Active" once this is the active environment.</summary>
    public string SetActiveLabel => IsSelectedActive ? "Active" : "Set active";

    /// <summary>The header "Set active" button is disabled when the selection is already active.</summary>
    public bool CanSetActive => Selected is not null && !IsSelectedActive;

    /// <summary>Delete is disabled when only one environment remains (always keep at least one).</summary>
    public bool CanDeleteProfile => Profiles.Count > 1;

    /// <summary>Whether the selected environment has a client secret stored (Auth tab).</summary>
    public bool HasSecret => Selected is not null && _secrets.HasSecret(Selected.Id);

    /// <summary>Whether the selected environment has a Dataverse client secret stored (CE tab).</summary>
    public bool HasDataverseSecret => Selected is not null && _secrets.HasSecret(Selected.Id, SecretTarget.Dataverse);

    /// <summary>Whether the selected environment retains a legacy DI password that can be explicitly cleared.</summary>
    public bool HasDiSecret => Selected is not null && _secrets.HasSecret(Selected.Id, SecretTarget.DataIntegrator);

    public bool HasLegacyDiConfiguration => Selected is not null &&
        (HasDiSecret || !string.IsNullOrWhiteSpace(Selected.DataIntegratorClientId)
         || Selected.DataIntegratorMode != DiAuthMode.Interactive
         || !string.IsNullOrWhiteSpace(Selected.DualWriteGatewayUrl));

    /// <summary>Raised when the active profile changes, so the shell's switcher can stay in sync.</summary>
    public event Action<string>? ActiveChanged;

    /// <summary>Raised with the updated profile after Save, so the shell can refresh its env list.</summary>
    public event Action<EnvProfile>? ProfileSaved;

    /// <summary>Raised with the deleted profile id, so the shell can drop it from its env list.</summary>
    public event Action<string>? ProfileDeleted;

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new EnvProfile(
            Guid.NewGuid().ToString("N"), "New environment", string.Empty, string.Empty, string.Empty,
            string.Empty, EnvStatus.Disconnected);

        _store.Save(profile);
        Profiles.Add(profile);
        Selected = profile; // load its (blank) drafts for editing
        ProfileSaved?.Invoke(profile);
        Status = "Added a new environment — fill in the details and Save.";
    }

    [RelayCommand(CanExecute = nameof(CanDeleteProfile))]
    private void DeleteProfile()
    {
        // Enforce the "keep at least one profile" invariant on the command itself, not just the button's
        // IsEnabled binding — ICommand.Execute bypasses CanExecute, so guard here too.
        if (Selected is null || Profiles.Count <= 1)
        {
            return;
        }

        if (Selected.Id == ActiveId && _mutationBlockReason() is { } blocked)
        {
            Status = blocked;
            return;
        }

        var id = Selected.Id;
        var name = Selected.Name;

        // Select the adjacent item after removal (standard list-deletion UX), not always the top.
        var nextIndex = Math.Min(Profiles.IndexOf(Selected), Profiles.Count - 2);
        _store.Delete(id);
        Profiles.Remove(Selected);
        Selected = nextIndex >= 0 ? Profiles[nextIndex] : Profiles.FirstOrDefault();

        if (id == ActiveId)
        {
            ActiveId = null; // the active profile is gone; keep the VM in step with the store
        }

        ProfileDeleted?.Invoke(id);
        Status = $"Deleted '{name}'.";
    }

    // Stores a secret, turning a hard storage failure (e.g. no DPAPI vault on this platform) into a
    // message instead of an unhandled exception out of the command. Returns null on success.
    private string? TryStoreSecret(string key, string plaintext, SecretTarget target = SecretTarget.Fo)
    {
        try
        {
            _secrets.SetSecret(key, plaintext, target);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    [RelayCommand]
    private void SaveSecret()
    {
        if (Selected is null)
        {
            return;
        }
        if (!CanStoreFoClientSecret)
        {
            Status = "Save authentication changes before entering a client secret.";
            return;
        }
        if (string.IsNullOrEmpty(SecretInput)) return;

        var error = TryStoreSecret(Selected.Id, SecretInput);
        OnPropertyChanged(nameof(HasSecret));
        if (error is not null)
        {
            Status = $"Could not store the secret for '{Selected.Name}': {error}";
            return;
        }

        if (!HasSecret)
        {
            // The store no-ops when there's no F&O service principal yet; keep the entry and say so
            // rather than report a false success and lose what the user typed.
            Status = "Set a client ID and save the profile before storing its secret.";
            return;
        }

        SecretInput = string.Empty; // don't keep plaintext around after it's protected
        Status = $"Secret stored for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void ClearSecret()
    {
        if (Selected is null)
        {
            return;
        }
        if (!CanStoreFoClientSecret)
        {
            Status = "Save authentication changes before clearing the client secret.";
            return;
        }

        _secrets.ClearSecret(Selected.Id);
        OnPropertyChanged(nameof(HasSecret));
        Status = $"Secret cleared for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void SaveDataverseSecret()
    {
        if (Selected is null)
        {
            return;
        }
        if (!CanStoreDataverseClientSecret)
        {
            Status = "Save authentication changes before entering a client secret.";
            return;
        }
        if (string.IsNullOrEmpty(DataverseSecretInput)) return;

        var error = TryStoreSecret(Selected.Id, DataverseSecretInput, SecretTarget.Dataverse);
        OnPropertyChanged(nameof(HasDataverseSecret));
        if (error is not null)
        {
            Status = $"Could not store the Dataverse secret for '{Selected.Name}': {error}";
            return;
        }

        if (!HasDataverseSecret)
        {
            // The store no-ops when there's no Dataverse service principal yet; keep the entry and say
            // so rather than report a false success and lose what the user typed.
            Status = "Set a Dataverse client ID and save the profile before storing its secret.";
            return;
        }

        DataverseSecretInput = string.Empty; // don't keep plaintext around after it's protected
        Status = $"Dataverse secret stored for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void ClearDataverseSecret()
    {
        if (Selected is null)
        {
            return;
        }
        if (!CanStoreDataverseClientSecret)
        {
            Status = "Save authentication changes before clearing the client secret.";
            return;
        }

        _secrets.ClearSecret(Selected.Id, SecretTarget.Dataverse);
        OnPropertyChanged(nameof(HasDataverseSecret));
        Status = $"Dataverse secret cleared for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void ClearLegacyDiPassword()
    {
        if (Selected is null)
        {
            return;
        }

        _secrets.ClearSecret(Selected.Id, SecretTarget.DataIntegrator);
        OnPropertyChanged(nameof(HasDiSecret));
        OnPropertyChanged(nameof(HasLegacyDiConfiguration));
        LegacyDiStatus = "Legacy Data Integrator password cleared.";
    }

    [RelayCommand]
    private async Task SignOut(CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        var name = Selected.Name;
        var tenant = string.IsNullOrWhiteSpace(Selected.Tenant) ? "this tenant" : Selected.Tenant;
        try
        {
            await _auth.SignOutAsync(Selected, ct);
            // Say what actually happens. The MSAL token cache is keyed clientId|tenantId and sign-out
            // deletes the whole blob for that key, and every Interactive profile shares the one app
            // registration — so this is tenant-wide, not per-profile. The old wording named only this
            // profile, so a user signing out of one sandbox silently lost every other profile too.
            // Ceiling: making it genuinely per-profile needs either a per-profile client id (separate
            // cache key) or MSAL account-level removal (RemoveAsync for just this profile's account
            // instead of deleting the blob). That is the real fix if the scope ever matters.
            Status = $"Signed out of {tenant}: every profile using the shared app registration in this tenant is signed out. You'll be asked to sign in again next time.";
        }
        catch (Exception ex)
        {
            Status = $"Sign out of '{name}' failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SetActive()
    {
        var selected = Selected;
        if (selected is null)
        {
            return;
        }

        var error = await _requestActivation(selected);
        if (error is not null)
        {
            Status = error;
            return;
        }

        ActiveId = selected.Id;
        Status = $"'{selected.Name}' is now the active environment.";
        ActiveChanged?.Invoke(selected.Id);
    }

    [RelayCommand]
    private async Task Save()
    {
        var selected = Selected;
        if (selected is null)
        {
            return;
        }

        var effectiveFoClientId = string.IsNullOrWhiteSpace(DraftClientId) ? null : DraftClientId;
        var effectiveDataverseClientId = string.IsNullOrWhiteSpace(DraftDataverseClientId)
            ? null
            : DraftDataverseClientId;

        if (!IsSupportedAuthMode(DraftAuthMode)
            && (DraftAuthMode != selected.AuthMode
                || !string.Equals(effectiveFoClientId, selected.ClientId, StringComparison.Ordinal)))
        {
            Status = "The saved F&O authentication mode is unsupported. Choose a supported mode before changing its client ID or mode.";
            return;
        }
        if (!IsSupportedAuthMode(DraftDataverseAuthMode)
            && (DraftDataverseAuthMode != selected.DataverseAuthMode
                || !string.Equals(effectiveDataverseClientId, selected.DataverseClientId, StringComparison.Ordinal)))
        {
            Status = "The saved Dataverse authentication mode is unsupported. Choose a supported mode before changing its client ID or mode.";
            return;
        }

        // Captured before the swap so we can tell whether the auth identity changed (and evict the old
        // cached session if so).
        var previous = selected;

        // Commit the editable drafts onto a new immutable record, persist, and swap it into the list
        // so the master + detail reflect the edit.
        var updated = BuildDraftProfile(selected);

        var activeIdentityChanged = selected.Id == ActiveId &&
            EnvironmentIdentity.Create(selected) != EnvironmentIdentity.Create(updated);
        if (activeIdentityChanged)
        {
            var error = await _commitActiveIdentitySave(selected, updated);
            if (error is not null)
            {
                Status = error;
                return;
            }
        }
        else
        {
            try
            {
                _store.Save(updated);
            }
            catch (Exception ex)
            {
                Status = $"Couldn't save '{updated.Name}': {ex.Message}";
                return;
            }
        }

        var existing = Profiles.FirstOrDefault(p => p.Id == selected.Id);
        var index = existing is null ? -1 : Profiles.IndexOf(existing);
        var sameSelectionOwned = Selected?.Id == selected.Id;
        var legacyDiStatus = LegacyDiStatus;
        if (index >= 0)
        {
            Profiles[index] = updated;
        }

        if (sameSelectionOwned && (Selected is null || Selected.Id == selected.Id))
        {
            Selected = updated;
        }
        if (sameSelectionOwned && Selected?.Id == selected.Id)
        {
            LegacyDiStatus = legacyDiStatus;
        }
        Status = $"Saved '{updated.Name}'.";
        ProfileSaved?.Invoke(updated);

        // If the auth identity changed (client id / tenant / mode), any token cached for the OLD identity
        // is now stale — evict it so the next call signs in fresh. A plain rename leaves SSO intact.
        if (AuthIdentityChanged(previous, updated))
        {
            _ = EvictStaleSessionAsync(previous);
        }
    }

    private static bool AuthIdentityChanged(EnvProfile before, EnvProfile after) =>
        !string.Equals(before.ClientId, after.ClientId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(before.DataverseClientId, after.DataverseClientId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(before.Tenant, after.Tenant, StringComparison.OrdinalIgnoreCase) ||
        before.AuthMode != after.AuthMode ||
        before.DataverseAuthMode != after.DataverseAuthMode;

    private async Task EvictStaleSessionAsync(EnvProfile env)
    {
        // Best-effort: a failed cache eviction must never block saving a profile.
        try
        {
            await _auth.SignOutAsync(env);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Failed to evict cached session for '{env.Name}': {ex}");
        }
    }

    private EnvProfile BuildDraftProfile(EnvProfile selected) => selected with
    {
        Name = DraftName,
        Url = DraftUrl,
        Tenant = DraftTenant,
        Legal = DraftLegal,
        Tier = DraftEnvironmentType,
        DataverseUrl = string.IsNullOrWhiteSpace(DraftDataverseUrl) ? null : DraftDataverseUrl,
        DataverseClientId = string.IsNullOrWhiteSpace(DraftDataverseClientId) ? null : DraftDataverseClientId,
        DataverseAuthMode = DraftDataverseAuthMode,
        ClientId = string.IsNullOrWhiteSpace(DraftClientId) ? null : DraftClientId,
        AuthMode = DraftAuthMode
    };

    private bool CanStartProbe() => !_disposed && Selected is { } selected && Profiles.Any(p => p.Id == selected.Id);

    private bool IsCurrentProbe(EnvProfile snapshot, int generation) =>
        !_disposed && generation == Volatile.Read(ref _testGeneration) && Selected is { } selected &&
        selected.Id == snapshot.Id && Profiles.Any(p => p.Id == snapshot.Id) &&
        string.Equals(DraftName, snapshot.Name, StringComparison.Ordinal) &&
        EnvironmentIdentity.Create(BuildDraftProfile(selected)) == EnvironmentIdentity.Create(snapshot);

    private string? ProbeRefusal(ProbeKind kind, EnvProfile saved, EnvProfile snapshot)
    {
        if (kind == ProbeKind.Gateway)
            return string.IsNullOrWhiteSpace(snapshot.Url) ? "Set the F&O environment URL first." : null;

        var dataverse = kind == ProbeKind.Dataverse;
        var secretInput = dataverse ? DataverseSecretInput : SecretInput;
        if (!string.IsNullOrEmpty(secretInput))
            return "A new secret is pending. Store it explicitly before testing, or clear the entry. Test never stores or uses typed secrets.";

        var mode = dataverse ? snapshot.DataverseAuthMode : snapshot.AuthMode;
        if (!IsSupportedAuthMode(mode))
            return "This authentication mode is unsupported. Choose Interactive or Client secret before testing.";

        if (mode == FoAuthMode.ClientSecret)
        {
            var savedMode = dataverse ? saved.DataverseAuthMode : saved.AuthMode;
            var savedClient = dataverse ? saved.DataverseClientId : saved.ClientId;
            var draftClient = dataverse ? snapshot.DataverseClientId : snapshot.ClientId;
            if (!SavedClientSecretContextMatches(savedMode, savedClient, mode, draftClient, saved.Tenant, snapshot.Tenant))
                return "Save authentication changes, then Store the matching client secret before testing.";
            if (!_secrets.HasSecret(saved.Id, dataverse ? SecretTarget.Dataverse : SecretTarget.Fo))
                return "Store the client secret explicitly before testing.";
        }
        return null;
    }

    private void SetProbeStatus(ProbeKind kind, string status)
    {
        if (kind == ProbeKind.Fo) FoTestStatus = status;
        else if (kind == ProbeKind.Dataverse) DataverseTestStatus = status;
        else DiStatus = status;
    }

    private async Task RunProbeAsync(ProbeKind kind, CancellationToken ct)
    {
        var saved = Selected;
        if (!CanStartProbe() || saved is null) return;
        var snapshot = BuildDraftProfile(saved);
        var generation = Volatile.Read(ref _testGeneration);
        var endpoint = kind == ProbeKind.Dataverse ? snapshot.DataverseUrl : snapshot.Url;
        var target = kind == ProbeKind.Fo ? "F&O" : kind == ProbeKind.Dataverse ? "Dataverse" : "Gateway";
        var attribution = $"{target} draft '{snapshot.Name}' ({endpoint})";
        try
        {
            ct.ThrowIfCancellationRequested();
            if (ProbeRefusal(kind, saved, snapshot) is { } refusal)
            {
                SetProbeStatus(kind, refusal);
                return;
            }
            SetProbeStatus(kind, $"Testing {attribution}…");
            ConnectionTestResult result;
            if (kind == ProbeKind.Gateway)
            {
                var gateway = await WaitForProbeAsync(_gatewayTester.TestAsync(snapshot, ct), ct);
                result = new ConnectionTestResult(gateway.IsSuccess, gateway.Message);
            }
            else
            {
                result = await WaitForProbeAsync(kind == ProbeKind.Fo
                    ? _connectionTester.TestFoAsync(snapshot, ct)
                    : _connectionTester.TestDataverseAsync(snapshot, ct), ct);
            }
            ct.ThrowIfCancellationRequested();
            if (!IsCurrentProbe(snapshot, generation)) return;
            SetProbeStatus(kind, result.Success
                ? $"Connected: {attribution} — {result.Message}"
                : $"{attribution} failed: {result.Message}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (IsCurrentProbe(snapshot, generation)) SetProbeStatus(kind, $"{attribution}: test cancelled.");
        }
        catch (Exception ex)
        {
            if (IsCurrentProbe(snapshot, generation)) SetProbeStatus(kind, $"{attribution} failed: {ex.Message}");
        }
    }

    private static async Task<T> WaitForProbeAsync<T>(Task<T> probe, CancellationToken ct)
    {
        try
        {
            return await probe.WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // WaitAsync detaches from an unfinished probe when cancelled. Observe a later fault on
            // that original task without retaining the VM, publishing/logging, or delaying cancellation.
            _ = probe.ContinueWith(static completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private void OnTestContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName?.StartsWith("Draft", StringComparison.Ordinal) == true ||
            e.PropertyName is nameof(Selected) or nameof(SecretInput) or nameof(DataverseSecretInput) or nameof(HasSecret) or nameof(HasDataverseSecret))
            InvalidateTests();
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateTests();
        OnPropertyChanged(nameof(CanDeleteProfile));
        DeleteProfileCommand.NotifyCanExecuteChanged();
    }

    private void InvalidateTests()
    {
        Interlocked.Increment(ref _testGeneration);
        TestConnectionCommand.Cancel();
        TestDataverseConnectionCommand.Cancel();
        TestGatewayCommand.Cancel();
        FoTestStatus = DataverseTestStatus = DiStatus = string.Empty;
        TestConnectionCommand.NotifyCanExecuteChanged();
        TestDataverseConnectionCommand.NotifyCanExecuteChanged();
        TestGatewayCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PropertyChanged -= OnTestContextChanged;
        Profiles.CollectionChanged -= OnProfilesChanged;
        InvalidateTests();
    }

    // Profiles-only ownership: admission precedes CTS allocation, so rejected direct calls cannot
    // cancel the accepted probe or replace ExecutionTask. Different instances represent independent targets.
    private sealed class ProbeCommand : IAsyncRelayCommand
    {
        private int _held;
        private CancellationTokenSource? _cts;
        private bool _cancelled;
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool> _canStart;
        private readonly Action<bool> _busy;
        public ProbeCommand(Func<CancellationToken, Task> execute, Func<bool> canStart, Action<bool> busy)
        {
            _execute = execute;
            _canStart = canStart;
            _busy = busy;
            CancelCommand = new RelayCommand(Cancel, () => CanBeCanceled);
        }
        public IRelayCommand CancelCommand { get; }
        public Task? ExecutionTask { get; private set; }
        public bool IsRunning => Volatile.Read(ref _held) != 0;
        public bool CanBeCanceled => _cts is { IsCancellationRequested: false };
        public bool IsCancellationRequested => _cts?.IsCancellationRequested ?? _cancelled;
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => _canStart() && !IsRunning;
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        public void Cancel()
        {
            _cts?.Cancel();
            Notify();
        }
        public async void Execute(object? parameter) => await ExecuteAsync(parameter);
        public Task ExecuteAsync(object? parameter)
        {
            if (!_canStart() || Interlocked.CompareExchange(ref _held, 1, 0) != 0) return Task.CompletedTask;
            var cts = new CancellationTokenSource();
            _cts = cts;
            _cancelled = false;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ExecutionTask = completion.Task;
            _busy(true);
            Notify();
            _ = Run(cts, completion);
            return completion.Task;
        }
        private async Task Run(CancellationTokenSource cts, TaskCompletionSource completion)
        {
            Exception? error = null;
            try { await _execute(cts.Token); }
            catch (Exception ex) { error = ex; }
            finally
            {
                _cancelled = cts.IsCancellationRequested;
                _cts = null;
                cts.Dispose();
                Volatile.Write(ref _held, 0);
                _busy(false);
                if (error is not null) completion.TrySetException(error);
                else completion.TrySetResult();
                Notify();
            }
        }
        private void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
}
