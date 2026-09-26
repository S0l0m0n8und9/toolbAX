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
using ToolBax.App.Models;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

public enum SecretPresenceState
{
    Unknown,
    Loading,
    Present,
    Absent,
    Failed,
}

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
    private readonly Func<EnvProfile, CancellationToken, Task<string?>> _requestActivation;
    private readonly Func<EnvProfile, EnvProfile, CancellationToken, Task<string?>> _commitActiveIdentitySave;
    private readonly Func<EnvProfile, CancellationToken, Task<ProfileDeleteOutcome>> _deleteProfile;
    private readonly Func<string?> _mutationBlockReason;
    private readonly EnvironmentWriteGate? _environmentWriteGate;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private CancellationTokenSource? _persistenceCts;
    private CancellationTokenSource? _presenceCts;
    private int _persistenceOwner;
    private int _presenceEpoch;
    private int _editorRevision;
    private int _secretInputRevision;
    private int _dataverseSecretInputRevision;
    private readonly object _presenceRevisionGate = new();
    private readonly Dictionary<(string ProfileId, SecretTarget Target), int> _presenceCommitRevisions = new();
    private bool _internalSameProfilePublication;
    private bool _preserveNewerDrafts;

    private int _testGeneration;
    private bool _disposed;

    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand TestDataverseConnectionCommand { get; }
    public IAsyncRelayCommand TestGatewayCommand { get; }
    public IRelayCommand TestConnectionCancelCommand => ((ProbeCommand)TestConnectionCommand).CancelCommand;
    public IRelayCommand TestDataverseConnectionCancelCommand => ((ProbeCommand)TestDataverseConnectionCommand).CancelCommand;
    public IRelayCommand TestGatewayCancelCommand => ((ProbeCommand)TestGatewayCommand).CancelCommand;
    public IAsyncRelayCommand RefreshSecretPresenceCommand { get; }
    public IAsyncRelayCommand AddProfileCommand { get; }
    public IAsyncRelayCommand DeleteProfileCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand SetActiveCommand { get; }
    public IAsyncRelayCommand SaveSecretCommand { get; }
    public IAsyncRelayCommand ClearSecretCommand { get; }
    public IAsyncRelayCommand SaveDataverseSecretCommand { get; }
    public IAsyncRelayCommand ClearDataverseSecretCommand { get; }
    public IAsyncRelayCommand ClearLegacyDiPasswordCommand { get; }
    public IRelayCommand CancelPersistenceCommand { get; }

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

    public bool CanStoreFoClientSecret => !IsPersisting && SavedClientSecretContextMatches(
        Selected?.AuthMode, Selected?.ClientId, DraftAuthMode, DraftClientId, Selected?.Tenant, DraftTenant);

    public bool CanStoreDataverseClientSecret => !IsPersisting && SavedClientSecretContextMatches(
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProfile))]
    private bool _isPersisting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecret))]
    [NotifyPropertyChangedFor(nameof(SecretPresenceMessage))]
    [NotifyPropertyChangedFor(nameof(HasSecretPresenceFailure))]
    private SecretPresenceState _foSecretPresence = SecretPresenceState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDataverseSecret))]
    [NotifyPropertyChangedFor(nameof(SecretPresenceMessage))]
    [NotifyPropertyChangedFor(nameof(HasSecretPresenceFailure))]
    private SecretPresenceState _dataverseSecretPresence = SecretPresenceState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiSecret))]
    [NotifyPropertyChangedFor(nameof(HasLegacyDiConfiguration))]
    [NotifyPropertyChangedFor(nameof(SecretPresenceMessage))]
    [NotifyPropertyChangedFor(nameof(HasSecretPresenceFailure))]
    private SecretPresenceState _diSecretPresence = SecretPresenceState.Unknown;

    public bool CanEditProfile => !IsPersisting;
    public bool HasSecretPresenceFailure =>
        FoSecretPresence == SecretPresenceState.Failed ||
        DataverseSecretPresence == SecretPresenceState.Failed ||
        DiSecretPresence == SecretPresenceState.Failed;
    public string SecretPresenceMessage =>
        FoSecretPresence == SecretPresenceState.Loading ||
        DataverseSecretPresence == SecretPresenceState.Loading ||
        DiSecretPresence == SecretPresenceState.Loading
            ? "Checking stored credential status…"
            : FoSecretPresence == SecretPresenceState.Failed ||
              DataverseSecretPresence == SecretPresenceState.Failed ||
              DiSecretPresence == SecretPresenceState.Failed
                ? "Stored credential status is unavailable. Retry the check."
                : string.Empty;

    public ProfilesViewModel(
        IProfileStore store,
        ISecretStore? secrets = null,
        IInteractiveAuthBroker? broker = null,
        IAuthService? auth = null,
        IDualWriteGatewayTester? gatewayTester = null,
        IConnectionTester? connectionTester = null,
        Func<EnvProfile, Task<string?>>? requestActivation = null,
        Func<EnvProfile, EnvProfile, Task<string?>>? commitActiveIdentitySave = null,
        Func<string?>? mutationBlockReason = null,
        EnvironmentWriteGate? environmentWriteGate = null,
        Func<EnvProfile, CancellationToken, Task<string?>>? requestActivationAsync = null,
        Func<EnvProfile, EnvProfile, CancellationToken, Task<string?>>? commitActiveIdentitySaveAsync = null,
        Func<EnvProfile, CancellationToken, Task<ProfileDeleteOutcome>>? deleteProfileAsync = null)
    {
        _store = store;
        _secrets = secrets ?? new FakeSecretStore();
        _ = broker; // Retained constructor compatibility; portal testing uses IDualWriteGatewayTester.
        _auth = auth ?? new FakeAuthService();
        _gatewayTester = gatewayTester ?? new FakeDualWriteGatewayTester();
        _connectionTester = connectionTester ?? new FakeConnectionTester();
        _environmentWriteGate = environmentWriteGate;
        _requestActivation = requestActivationAsync ?? (requestActivation is null
            ? LocalActivationAsync
            : (profile, _) => requestActivation(profile));
        _commitActiveIdentitySave = commitActiveIdentitySaveAsync ?? (commitActiveIdentitySave is null
            ? LocalIdentitySaveAsync
            : (before, after, _) => commitActiveIdentitySave(before, after));
        _deleteProfile = deleteProfileAsync ?? LocalDeleteAsync;
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
        RefreshSecretPresenceCommand = new AsyncRelayCommand(
            RefreshSecretPresenceAsync, AsyncRelayCommandOptions.AllowConcurrentExecutions);
        AddProfileCommand = new PersistenceCommand(AddProfile, CanPersist);
        DeleteProfileCommand = new PersistenceCommand(DeleteProfile, () => CanDeleteProfile);
        SaveCommand = new PersistenceCommand(Save, CanPersist);
        SetActiveCommand = new PersistenceCommand(SetActive, () => CanSetActive);
        SaveSecretCommand = new PersistenceCommand(
            ct => StoreSecretForTargetAsync(SecretTarget.Fo, ct), () => CanStoreFoClientSecret);
        ClearSecretCommand = new PersistenceCommand(
            ct => ClearSecretForTargetAsync(SecretTarget.Fo, ct), () => CanStoreFoClientSecret);
        SaveDataverseSecretCommand = new PersistenceCommand(
            ct => StoreSecretForTargetAsync(SecretTarget.Dataverse, ct), () => CanStoreDataverseClientSecret);
        ClearDataverseSecretCommand = new PersistenceCommand(
            ct => ClearSecretForTargetAsync(SecretTarget.Dataverse, ct), () => CanStoreDataverseClientSecret);
        ClearLegacyDiPasswordCommand = new PersistenceCommand(
            ct => ClearSecretForTargetAsync(SecretTarget.DataIntegrator, ct), CanPersist);
        CancelPersistenceCommand = new RelayCommand(CancelPersistence, CanCancelPersistence);
        PropertyChanged += OnTestContextChanged;
        Profiles.CollectionChanged += OnProfilesChanged;
        StartPresenceRefresh();
    }

    private async Task<string?> LocalActivationAsync(EnvProfile target, CancellationToken ct)
    {
        if (!TryAcquireProfileCommit(out var lease))
            return "Finish or cancel the live write before changing the active environment.";
        using (lease)
        try
        {
            await _store.SetActiveAsync(target.Id, ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't switch environment: {ex.Message}";
        }
    }

    private async Task<string?> LocalIdentitySaveAsync(EnvProfile before, EnvProfile after, CancellationToken ct)
    {
        if (!TryAcquireProfileCommit(out var lease))
            return "Finish or cancel the live write before changing the active environment.";
        using (lease)
        try
        {
            await _store.SaveAsync(after, ct);
            return null;
        }
        catch (Exception ex)
        {
            return $"Couldn't save '{after.Name}': {ex.Message}";
        }
    }

    private async Task<ProfileDeleteOutcome> LocalDeleteAsync(EnvProfile profile, CancellationToken ct)
    {
        if (!TryAcquireProfileCommit(out var lease))
            return new ProfileDeleteOutcome(false, "Finish or cancel the live write before deleting a profile.");
        using (lease)
        {
            await _store.DeleteAsync(profile.Id, ct);
            return new ProfileDeleteOutcome(true);
        }
    }

    private bool TryAcquireProfileCommit(out IDisposable? lease)
    {
        if (_environmentWriteGate is null)
        {
            lease = null;
            return true;
        }
        return _environmentWriteGate.TryAcquireProfileCommit(out lease);
    }

    private bool TryBeginPersistence(CancellationToken commandToken, out CancellationToken token)
    {
        token = default;
        if (_disposed || Interlocked.CompareExchange(ref _persistenceOwner, 1, 0) != 0) return false;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(commandToken, _lifetimeCts.Token);
        _persistenceCts = cts;
        IsPersisting = true;
        token = cts.Token;
        return true;
    }

    private void EndPersistence()
    {
        var cts = Interlocked.Exchange(ref _persistenceCts, null);
        cts?.Dispose();
        Volatile.Write(ref _persistenceOwner, 0);
        IsPersisting = false;
    }

    private bool CanCancelPersistence() => _persistenceCts is { IsCancellationRequested: false };

    private void CancelPersistence()
    {
        _persistenceCts?.Cancel();
        CancelPersistenceCommand.NotifyCanExecuteChanged();
    }

    private bool CanPersist() => !_disposed && !IsPersisting;

    partial void OnIsPersistingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditProfile));
        OnPropertyChanged(nameof(CanSetActive));
        OnPropertyChanged(nameof(CanDeleteProfile));
        OnPropertyChanged(nameof(CanStoreFoClientSecret));
        OnPropertyChanged(nameof(CanStoreDataverseClientSecret));
        AddProfileCommand.NotifyCanExecuteChanged();
        DeleteProfileCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        SetActiveCommand.NotifyCanExecuteChanged();
        SaveSecretCommand.NotifyCanExecuteChanged();
        ClearSecretCommand.NotifyCanExecuteChanged();
        SaveDataverseSecretCommand.NotifyCanExecuteChanged();
        ClearDataverseSecretCommand.NotifyCanExecuteChanged();
        ClearLegacyDiPasswordCommand.NotifyCanExecuteChanged();
        CancelPersistenceCommand.NotifyCanExecuteChanged();
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
        if (!_internalSameProfilePublication || !_preserveNewerDrafts) LoadDrafts(newValue);
        if (!_internalSameProfilePublication)
        {
            SecretInput = string.Empty; // never carry an entry across environments
            DataverseSecretInput = string.Empty;
        }
        DiStatus = string.Empty;
        if (!string.Equals(oldValue?.Id, newValue?.Id, StringComparison.Ordinal))
        {
            LegacyDiStatus = string.Empty;
        }
        OnPropertyChanged(nameof(HasDiSecret));
        OnPropertyChanged(nameof(HasLegacyDiConfiguration));
        StartPresenceRefresh();
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
    public bool CanSetActive => !IsPersisting && Selected is not null && !IsSelectedActive;

    /// <summary>Delete is disabled when only one environment remains (always keep at least one).</summary>
    public bool CanDeleteProfile => !IsPersisting && Profiles.Count > 1;

    /// <summary>Whether the selected environment has a client secret stored (Auth tab).</summary>
    public bool HasSecret => Selected is not null && FoSecretPresence == SecretPresenceState.Present;

    /// <summary>Whether the selected environment has a Dataverse client secret stored (CE tab).</summary>
    public bool HasDataverseSecret => Selected is not null && DataverseSecretPresence == SecretPresenceState.Present;

    /// <summary>Whether the selected environment retains a legacy DI password that can be explicitly cleared.</summary>
    public bool HasDiSecret => Selected is not null && DiSecretPresence == SecretPresenceState.Present;

    public bool HasLegacyDiConfiguration => Selected is not null &&
        (HasDiSecret || !string.IsNullOrWhiteSpace(Selected.DataIntegratorClientId)
         || Selected.DataIntegratorMode != DiAuthMode.Interactive
         || !string.IsNullOrWhiteSpace(Selected.DualWriteGatewayUrl));

    private void StartPresenceRefresh()
    {
        Interlocked.Increment(ref _presenceEpoch);
        _presenceCts?.Cancel();
        if (_disposed || Selected is null)
        {
            FoSecretPresence = DataverseSecretPresence = DiSecretPresence = SecretPresenceState.Unknown;
            return;
        }
        FoSecretPresence = DataverseSecretPresence = DiSecretPresence = SecretPresenceState.Loading;
        RefreshSecretPresenceCommand.Execute(null);
    }

    private async Task RefreshSecretPresenceAsync(CancellationToken commandToken)
    {
        var selected = Selected;
        var epoch = Interlocked.Increment(ref _presenceEpoch);
        if (_disposed || selected is null) return;
        FoSecretPresence = DataverseSecretPresence = DiSecretPresence = SecretPresenceState.Loading;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(commandToken, _lifetimeCts.Token);
        var previous = Interlocked.Exchange(ref _presenceCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        try
        {
            await Task.WhenAll(
                RefreshPresenceTargetAsync(selected.Id, SecretTarget.Fo, epoch,
                    PresenceCommitRevision(selected.Id, SecretTarget.Fo), cts.Token),
                RefreshPresenceTargetAsync(selected.Id, SecretTarget.Dataverse, epoch,
                    PresenceCommitRevision(selected.Id, SecretTarget.Dataverse), cts.Token),
                RefreshPresenceTargetAsync(selected.Id, SecretTarget.DataIntegrator, epoch,
                    PresenceCommitRevision(selected.Id, SecretTarget.DataIntegrator), cts.Token));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // A newer selection/refresh owns publication.
        }
        finally
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _presenceCts, null, cts), cts)) cts.Dispose();
        }
    }

    private bool IsPresenceCurrent(string profileId, int epoch) =>
        !_disposed && epoch == Volatile.Read(ref _presenceEpoch) &&
        string.Equals(Selected?.Id, profileId, StringComparison.Ordinal);

    private async Task RefreshPresenceTargetAsync(
        string profileId, SecretTarget target, int epoch, int commitRevision, CancellationToken ct)
    {
        try
        {
            var present = await _secrets.HasSecretAsync(profileId, target, ct);
            if (!CanPublishPresence(profileId, target, epoch, commitRevision)) return;
            SetPresenceState(target, present ? SecretPresenceState.Present : SecretPresenceState.Absent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (CanPublishPresence(profileId, target, epoch, commitRevision))
                SetPresenceState(target, SecretPresenceState.Failed);
        }
    }

    private bool CanPublishPresence(string profileId, SecretTarget target, int epoch, int commitRevision) =>
        IsPresenceCurrent(profileId, epoch) && PresenceCommitRevision(profileId, target) == commitRevision;

    private int PresenceCommitRevision(string profileId, SecretTarget target)
    {
        lock (_presenceRevisionGate)
            return _presenceCommitRevisions.GetValueOrDefault((profileId, target));
    }

    private void IncrementPresenceCommitRevision(string profileId, SecretTarget target)
    {
        lock (_presenceRevisionGate)
        {
            var key = (profileId, target);
            _presenceCommitRevisions[key] = _presenceCommitRevisions.GetValueOrDefault(key) + 1;
        }
    }

    private void SetPresenceState(SecretTarget target, SecretPresenceState state)
    {
        if (target == SecretTarget.Fo) FoSecretPresence = state;
        else if (target == SecretTarget.Dataverse) DataverseSecretPresence = state;
        else DiSecretPresence = state;
    }

    private void SetKnownPresence(string profileId, SecretTarget target, bool present)
    {
        IncrementPresenceCommitRevision(profileId, target);
        if (!string.Equals(Selected?.Id, profileId, StringComparison.Ordinal)) return;
        var value = present ? SecretPresenceState.Present : SecretPresenceState.Absent;
        SetPresenceState(target, value);
    }

    /// <summary>Raised when the active profile changes, so the shell's switcher can stay in sync.</summary>
    public event Action<string>? ActiveChanged;

    /// <summary>Raised with the updated profile after Save, so the shell can refresh its env list.</summary>
    public event Action<EnvProfile>? ProfileSaved;

    /// <summary>Raised with the deleted profile id, so the shell can drop it from its env list.</summary>
    public event Action<string>? ProfileDeleted;

    private async Task AddProfile(CancellationToken commandToken)
    {
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        var profile = new EnvProfile(
            Guid.NewGuid().ToString("N"), "New environment", string.Empty, string.Empty, string.Empty,
            string.Empty, EnvStatus.Disconnected);
        IDisposable? gateLease = null;
        try
        {
            if (!TryAcquireProfileCommit(out gateLease))
            {
                Status = "Finish or cancel the live write before adding a profile.";
                return;
            }
            await _store.SaveAsync(profile, ct);
            if (_disposed) return;
            Profiles.Add(profile);
            Selected = profile; // load its (blank) drafts for editing
            ProfileSaved?.Invoke(profile);
            Status = "Added a new environment — fill in the details and Save.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = "Adding the profile was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Couldn't add the profile: {ex.Message}";
        }
        finally
        {
            gateLease?.Dispose();
            EndPersistence();
        }
    }

    private async Task DeleteProfile(CancellationToken commandToken)
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

        var captured = Selected;
        var id = captured.Id;
        var name = captured.Name;

        // Select the adjacent item after removal (standard list-deletion UX), not always the top.
        var capturedIndex = Profiles.IndexOf(captured);
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        try
        {
            var outcome = await _deleteProfile(captured, ct);
            if (!outcome.Deleted)
            {
                if (!_disposed) Status = outcome.Warning ?? $"Couldn't delete '{name}'.";
                return;
            }
            if (_disposed) return;
            var existing = Profiles.FirstOrDefault(profile => profile.Id == id);
            if (existing is not null) Profiles.Remove(existing);
            if (Selected?.Id == id)
            {
                var nextIndex = Math.Min(Math.Max(capturedIndex, 0), Profiles.Count - 1);
                Selected = nextIndex >= 0 ? Profiles[nextIndex] : Profiles.FirstOrDefault();
            }
            if (id == ActiveId) ActiveId = null;
            ProfileDeleted?.Invoke(id);
            Status = outcome.Warning is null
                ? $"Deleted '{name}'."
                : $"Deleted '{name}'. {outcome.Warning}";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = $"Deleting '{name}' was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Couldn't delete '{name}': {ex.Message}";
        }
        finally { EndPersistence(); }
    }

    private async Task StoreSecretForTargetAsync(SecretTarget target, CancellationToken commandToken)
    {
        var selected = Selected;
        var plaintext = target == SecretTarget.Dataverse ? DataverseSecretInput : SecretInput;
        var revision = target == SecretTarget.Dataverse
            ? Volatile.Read(ref _dataverseSecretInputRevision)
            : Volatile.Read(ref _secretInputRevision);
        if (selected is null || string.IsNullOrEmpty(plaintext)) return;
        if (target == SecretTarget.Fo && !CanStoreFoClientSecret ||
            target == SecretTarget.Dataverse && !CanStoreDataverseClientSecret)
        {
            Status = "Save authentication changes before entering a client secret.";
            return;
        }
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        IDisposable? gateLease = null;
        try
        {
            if (!TryAcquireProfileCommit(out gateLease))
            {
                Status = "Finish or cancel the live write before storing a secret.";
                return;
            }
            bool stored;
            stored = await _secrets.SetSecretAsync(selected.Id, plaintext, target, ct);
            if (_disposed) return;
            if (!stored)
            {
                Status = target == SecretTarget.Dataverse
                    ? "Set a Dataverse client ID and save the profile before storing its secret."
                    : "Set a client ID and save the profile before storing its secret.";
                return;
            }
            SetKnownPresence(selected.Id, target, true);
            InvalidateTests();
            if (string.Equals(Selected?.Id, selected.Id, StringComparison.Ordinal))
            {
                if (target == SecretTarget.Dataverse &&
                    revision == Volatile.Read(ref _dataverseSecretInputRevision) &&
                    string.Equals(DataverseSecretInput, plaintext, StringComparison.Ordinal))
                    DataverseSecretInput = string.Empty;
                else if (target == SecretTarget.Fo &&
                    revision == Volatile.Read(ref _secretInputRevision) &&
                    string.Equals(SecretInput, plaintext, StringComparison.Ordinal))
                    SecretInput = string.Empty;
            }
            Status = target == SecretTarget.Dataverse
                ? $"Dataverse secret stored for '{selected.Name}'."
                : $"Secret stored for '{selected.Name}'.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = $"Storing the secret for '{selected.Name}' was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Could not store the secret for '{selected.Name}': {ex.Message}";
        }
        finally
        {
            gateLease?.Dispose();
            EndPersistence();
        }
    }

    private async Task ClearSecretForTargetAsync(SecretTarget target, CancellationToken commandToken)
    {
        var selected = Selected;
        if (selected is null) return;
        if (target == SecretTarget.Fo && !CanStoreFoClientSecret ||
            target == SecretTarget.Dataverse && !CanStoreDataverseClientSecret)
        {
            Status = "Save authentication changes before clearing the client secret.";
            return;
        }
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        IDisposable? gateLease = null;
        try
        {
            if (!TryAcquireProfileCommit(out gateLease))
            {
                Status = "Finish or cancel the live write before clearing a secret.";
                return;
            }
            await _secrets.ClearSecretAsync(selected.Id, target, ct);
            if (_disposed) return;
            SetKnownPresence(selected.Id, target, false);
            InvalidateTests();
            if (target == SecretTarget.DataIntegrator)
            {
                if (string.Equals(Selected?.Id, selected.Id, StringComparison.Ordinal))
                    LegacyDiStatus = "Legacy Data Integrator password cleared.";
                else
                    Status = $"Legacy Data Integrator password cleared for '{selected.Name}'.";
            }
            else
                Status = target == SecretTarget.Dataverse
                    ? $"Dataverse secret cleared for '{selected.Name}'."
                    : $"Secret cleared for '{selected.Name}'.";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = $"Clearing the secret for '{selected.Name}' was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Could not clear the secret for '{selected.Name}': {ex.Message}";
        }
        finally
        {
            gateLease?.Dispose();
            EndPersistence();
        }
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

    private async Task SetActive(CancellationToken commandToken)
    {
        var selected = Selected;
        if (selected is null)
        {
            return;
        }
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        try
        {
            var error = await _requestActivation(selected, ct);
            if (_disposed) return;
            if (error is not null)
            {
                Status = error;
                return;
            }

            ActiveId = selected.Id;
            Status = $"'{selected.Name}' is now the active environment.";
            ActiveChanged?.Invoke(selected.Id);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = $"Activating '{selected.Name}' was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Couldn't activate '{selected.Name}': {ex.Message}";
        }
        finally { EndPersistence(); }
    }

    private async Task Save(CancellationToken commandToken)
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

        // Commit the editable drafts onto a new immutable record, persist, and swap it into the list
        // so the master + detail reflect the edit.
        var updated = BuildDraftProfile(selected);
        var editorRevision = Volatile.Read(ref _editorRevision);

        var activeIdentityChanged = selected.Id == ActiveId &&
            EnvironmentIdentity.Create(selected) != EnvironmentIdentity.Create(updated);
        if (!TryBeginPersistence(commandToken, out var ct)) return;
        IDisposable? directLease = null;
        try
        {
            if (activeIdentityChanged)
            {
                var error = await _commitActiveIdentitySave(selected, updated, ct);
                if (error is not null)
                {
                    if (!_disposed) Status = error;
                    return;
                }
            }
            else
            {
                if (!TryAcquireProfileCommit(out directLease))
                {
                    Status = "Finish or cancel the live write before saving the profile.";
                    return;
                }
                await _store.SaveAsync(updated, ct);
            }
            if (_disposed) return;

            var existing = Profiles.FirstOrDefault(p => p.Id == selected.Id);
            var index = existing is null ? -1 : Profiles.IndexOf(existing);
            var sameSelectionOwned = Selected?.Id == selected.Id;
            var legacyDiStatus = LegacyDiStatus;
            _internalSameProfilePublication = sameSelectionOwned;
            _preserveNewerDrafts = editorRevision != Volatile.Read(ref _editorRevision);
            try
            {
                if (index >= 0) Profiles[index] = updated;
                if (sameSelectionOwned && (Selected is null || Selected.Id == selected.Id)) Selected = updated;
            }
            finally
            {
                _internalSameProfilePublication = false;
                _preserveNewerDrafts = false;
            }
            if (sameSelectionOwned && Selected?.Id == selected.Id) LegacyDiStatus = legacyDiStatus;
            Status = $"Saved '{updated.Name}'.";
            ProfileSaved?.Invoke(updated);
            StartPresenceRefresh();

        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) Status = $"Saving '{updated.Name}' was cancelled.";
        }
        catch (Exception ex)
        {
            if (!_disposed) Status = $"Couldn't save '{updated.Name}': {ex.Message}";
        }
        finally
        {
            directLease?.Dispose();
            EndPersistence();
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

    private async Task<string?> ProbeRefusalAsync(
        ProbeKind kind, EnvProfile saved, EnvProfile snapshot, CancellationToken ct)
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
            if (!await _secrets.HasSecretAsync(saved.Id,
                    dataverse ? SecretTarget.Dataverse : SecretTarget.Fo, ct))
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
            var refusal = await ProbeRefusalAsync(kind, saved, snapshot, ct);
            ct.ThrowIfCancellationRequested();
            if (!IsCurrentProbe(snapshot, generation)) return;
            if (refusal is not null)
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
        if (e.PropertyName?.StartsWith("Draft", StringComparison.Ordinal) == true)
            Interlocked.Increment(ref _editorRevision);
        else if (e.PropertyName == nameof(SecretInput))
            Interlocked.Increment(ref _secretInputRevision);
        else if (e.PropertyName == nameof(DataverseSecretInput))
            Interlocked.Increment(ref _dataverseSecretInputRevision);
        if (e.PropertyName?.StartsWith("Draft", StringComparison.Ordinal) == true ||
            e.PropertyName is nameof(Selected) or nameof(SecretInput) or nameof(DataverseSecretInput))
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
        _lifetimeCts.Cancel();
        _persistenceCts?.Cancel();
        _presenceCts?.Cancel();
        RefreshSecretPresenceCommand.Cancel();
        PropertyChanged -= OnTestContextChanged;
        Profiles.CollectionChanged -= OnProfilesChanged;
        InvalidateTests();
        _lifetimeCts.Dispose();
    }

    private sealed class PersistenceCommand : IAsyncRelayCommand
    {
        private int _held;
        private CancellationTokenSource? _cts;
        private readonly Func<CancellationToken, Task> _execute;
        private readonly Func<bool> _canStart;

        public PersistenceCommand(Func<CancellationToken, Task> execute, Func<bool> canStart)
        {
            _execute = execute;
            _canStart = canStart;
        }

        public Task? ExecutionTask { get; private set; }
        public bool IsRunning => Volatile.Read(ref _held) != 0;
        public bool CanBeCanceled => _cts is { IsCancellationRequested: false };
        public bool IsCancellationRequested => _cts?.IsCancellationRequested ?? false;
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
            // ICommand callers normally honor CanExecute; direct tests/callers intentionally bypass it and
            // must still reach the method-level guard so refusal guidance is published. Only ownership is
            // enforced here, synchronously and before allocating a cancellation source.
            if (Interlocked.CompareExchange(ref _held, 1, 0) != 0)
                return Task.CompletedTask;
            var cts = new CancellationTokenSource();
            _cts = cts;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ExecutionTask = completion.Task;
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
                _cts = null;
                cts.Dispose();
                Volatile.Write(ref _held, 0);
                if (error is not null) completion.TrySetException(error);
                else completion.TrySetResult();
                Notify();
            }
        }
        private void Notify()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
            NotifyCanExecuteChanged();
        }
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
