using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
/// selection, and save. The auth / Dataverse / Data-Integrator detail tabs land in a follow-up (they
/// need the platform auth seams); this slice covers the master + the F&amp;O environment fields.
/// </summary>
public partial class ProfilesViewModel : ObservableObject
{
    private readonly IProfileStore _store;
    private readonly ISecretStore _secrets;
    private readonly IInteractiveAuthBroker _broker;
    private readonly IAuthService _auth;
    private readonly IDualWriteGatewayTester _gatewayTester;

    public ObservableCollection<EnvProfile> Profiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    [NotifyPropertyChangedFor(nameof(HasSecret))]
    [NotifyPropertyChangedFor(nameof(HasDataverseSecret))]
    private EnvProfile? _selected;

    /// <summary>The Auth-tab client-secret entry. Write-only: stored on save, never loaded back.</summary>
    [ObservableProperty]
    private string _secretInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
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
    private string _draftTenant = string.Empty;

    [ObservableProperty]
    private string _draftLegal = string.Empty;

    [ObservableProperty]
    private string _draftTier = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DataverseWebApi))]
    private string _draftDataverseUrl = string.Empty;

    // Dataverse drafts — a separate app reg from F&O.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDataverseDefaultClientIdNote))]
    private string _draftDataverseClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDataverseDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(IsDataverseClientSecretMode))]
    private FoAuthMode _draftDataverseAuthMode = FoAuthMode.Interactive;

    /// <summary>The Dataverse client-secret entry. Write-only, like the F&amp;O secret.</summary>
    [ObservableProperty]
    private string _dataverseSecretInput = string.Empty;

    // F&O drafts.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFoDefaultClientIdNote))]
    private string _draftClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFoDefaultClientIdNote))]
    [NotifyPropertyChangedFor(nameof(IsFoClientSecretMode))]
    private FoAuthMode _draftAuthMode = FoAuthMode.Interactive;

    public FoAuthMode[] AuthModes { get; } = { FoAuthMode.Interactive, FoAuthMode.ClientSecret, FoAuthMode.Certificate };

    /// <summary>Show the "Microsoft default client ID" note while the F&amp;O auth is Interactive and the
    /// client ID is still the default (it's editable; changing it hides the note).</summary>
    public bool ShowFoDefaultClientIdNote =>
        DraftAuthMode == FoAuthMode.Interactive && DraftClientId == FoAuthModeExtensions.DefaultInteractiveClientId;

    public bool ShowDataverseDefaultClientIdNote =>
        DraftDataverseAuthMode == FoAuthMode.Interactive && DraftDataverseClientId == FoAuthModeExtensions.DefaultInteractiveClientId;

    /// <summary>Client-secret entry only applies to the app-only ClientSecret mode (not Interactive/Certificate).</summary>
    public bool IsFoClientSecretMode => DraftAuthMode == FoAuthMode.ClientSecret;

    public bool IsDataverseClientSecretMode => DraftDataverseAuthMode == FoAuthMode.ClientSecret;

    // Data Integrator config drafts.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDiDefaultClientIdNote))]
    private string _draftDiClientId = string.Empty;

    /// <summary>Show the "well-known Data Integrator client ID" note while the DI client ID is still the
    /// default first-party app id (it's editable; changing it hides the note).</summary>
    public bool ShowDiDefaultClientIdNote =>
        DraftDiClientId == DiAuthModeExtensions.DefaultDataIntegratorClientId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRopc))]
    [NotifyPropertyChangedFor(nameof(IsInteractive))]
    private DiAuthMode _draftDiMode = DiAuthMode.Interactive;

    /// <summary>The dual-write management gateway base URL (entered manually for the loopback path).</summary>
    [ObservableProperty]
    private string _draftGatewayUrl = string.Empty;

    /// <summary>The DI ROPC service-account secret entry. Write-only, like the Auth client secret.</summary>
    [ObservableProperty]
    private string _diSecretInput = string.Empty;

    [ObservableProperty]
    private bool _isSigningIn;

    [ObservableProperty]
    private bool _isTestingGateway;

    [ObservableProperty]
    private string _diStatus = string.Empty;

    public DiAuthMode[] DiModes { get; } = { DiAuthMode.Interactive, DiAuthMode.Ropc };

    public ProfilesViewModel(
        IProfileStore store,
        ISecretStore? secrets = null,
        IInteractiveAuthBroker? broker = null,
        IAuthService? auth = null,
        IDualWriteGatewayTester? gatewayTester = null)
    {
        _store = store;
        _secrets = secrets ?? new FakeSecretStore();
        _broker = broker ?? new FakeInteractiveAuthBroker();
        _auth = auth ?? new FakeAuthService();
        _gatewayTester = gatewayTester ?? new FakeDualWriteGatewayTester();
        Profiles = new ObservableCollection<EnvProfile>(store.GetAll());
        _activeId = store.ActiveId;
        _selected = Profiles.FirstOrDefault(p => p.Id == _activeId) ?? Profiles.FirstOrDefault();
        LoadDrafts(_selected);
    }

    public bool IsRopc => DraftDiMode == DiAuthMode.Ropc;

    public bool IsInteractive => DraftDiMode == DiAuthMode.Interactive;

    // A status from one mode shouldn't linger in the other's section.
    partial void OnDraftDiModeChanged(DiAuthMode value) => DiStatus = string.Empty;

    // Selecting Interactive (MFA) defaults a blank client ID to Microsoft's global public client; an
    // already-entered ID is respected (item 4 — the field stays editable).
    partial void OnDraftAuthModeChanged(FoAuthMode value)
    {
        if (value == FoAuthMode.Interactive && string.IsNullOrWhiteSpace(DraftClientId))
        {
            DraftClientId = FoAuthModeExtensions.DefaultInteractiveClientId;
        }
    }

    partial void OnDraftDataverseAuthModeChanged(FoAuthMode value)
    {
        if (value == FoAuthMode.Interactive && string.IsNullOrWhiteSpace(DraftDataverseClientId))
        {
            DraftDataverseClientId = FoAuthModeExtensions.DefaultInteractiveClientId;
        }
    }

    private static string DiKey(string id) => $"{id}:di";

    partial void OnSelectedChanged(EnvProfile? value)
    {
        LoadDrafts(value);
        SecretInput = string.Empty; // never carry an entry across environments
        DataverseSecretInput = string.Empty;
        DiSecretInput = string.Empty;
        DiStatus = string.Empty;
        OnPropertyChanged(nameof(HasDiSecret));
    }

    private void LoadDrafts(EnvProfile? profile)
    {
        DraftName = profile?.Name ?? string.Empty;
        DraftUrl = profile?.Url ?? string.Empty;
        DraftTenant = profile?.Tenant ?? string.Empty;
        DraftLegal = profile?.Legal ?? string.Empty;
        DraftTier = profile?.Tier ?? string.Empty;
        DraftDataverseUrl = profile?.DataverseUrl ?? string.Empty;
        DraftDataverseClientId = profile?.DataverseClientId ?? string.Empty;
        DraftDataverseAuthMode = profile?.DataverseAuthMode ?? FoAuthMode.Interactive;
        DraftClientId = profile?.ClientId ?? string.Empty;
        DraftAuthMode = profile?.AuthMode ?? FoAuthMode.Interactive;
        // Default a blank DI client id to the well-known first-party app (editable) — the user shouldn't
        // have to supply one; a configured custom id is respected.
        DraftDiClientId = string.IsNullOrWhiteSpace(profile?.DataIntegratorClientId)
            ? DiAuthModeExtensions.DefaultDataIntegratorClientId
            : profile.DataIntegratorClientId;
        DraftDiMode = profile?.DataIntegratorMode ?? DiAuthMode.Interactive;
        DraftGatewayUrl = profile?.DualWriteGatewayUrl ?? string.Empty;
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

    /// <summary>Whether the selected environment has a client secret stored (Auth tab).</summary>
    public bool HasSecret => Selected is not null && _secrets.HasSecret(Selected.Id);

    /// <summary>Whether the selected environment has a Dataverse client secret stored (CE tab).</summary>
    public bool HasDataverseSecret => Selected is not null && _secrets.HasSecret(Selected.Id, SecretTarget.Dataverse);

    /// <summary>Whether the selected environment has a DI ROPC service-account secret stored.</summary>
    public bool HasDiSecret => Selected is not null && _secrets.HasSecret(DiKey(Selected.Id));

    /// <summary>Raised when the active profile changes, so the shell's switcher can stay in sync.</summary>
    public event Action<string>? ActiveChanged;

    /// <summary>Raised with the updated profile after Save, so the shell can refresh its env list.</summary>
    public event Action<EnvProfile>? ProfileSaved;

    /// <summary>Raised with the deleted profile id, so the shell can drop it from its env list.</summary>
    public event Action<string>? ProfileDeleted;

    [RelayCommand]
    private async Task TestConnection(CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        IsTestingFoConnection = true;
        Status = $"Testing connection to '{Selected.Name}'…";
        try
        {
            await _auth.AcquireFoTokenAsync(Selected, ct);
            Status = $"Connected to '{Selected.Name}' — token acquired.";
        }
        catch (Exception ex)
        {
            Status = $"Connection to '{Selected.Name}' failed: {ex.Message}";
        }
        finally
        {
            IsTestingFoConnection = false;
        }
    }

    [RelayCommand]
    private async Task TestDataverseConnection(CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        IsTestingDataverseConnection = true;
        Status = $"Testing Dataverse connection for '{Selected.Name}'…";
        try
        {
            await _auth.AcquireDataverseTokenAsync(Selected, ct);
            Status = $"Connected to Dataverse for '{Selected.Name}' — token acquired.";
        }
        catch (Exception ex)
        {
            Status = $"Dataverse connection for '{Selected.Name}' failed: {ex.Message}";
        }
        finally
        {
            IsTestingDataverseConnection = false;
        }
    }

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

    [RelayCommand]
    private void DeleteProfile()
    {
        if (Selected is null)
        {
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

    [RelayCommand]
    private void SaveSecret()
    {
        if (Selected is null || string.IsNullOrEmpty(SecretInput))
        {
            return;
        }

        _secrets.SetSecret(Selected.Id, SecretInput);
        OnPropertyChanged(nameof(HasSecret));
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

        _secrets.ClearSecret(Selected.Id);
        OnPropertyChanged(nameof(HasSecret));
        Status = $"Secret cleared for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void SaveDataverseSecret()
    {
        if (Selected is null || string.IsNullOrEmpty(DataverseSecretInput))
        {
            return;
        }

        _secrets.SetSecret(Selected.Id, DataverseSecretInput, SecretTarget.Dataverse);
        OnPropertyChanged(nameof(HasDataverseSecret));
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

        _secrets.ClearSecret(Selected.Id, SecretTarget.Dataverse);
        OnPropertyChanged(nameof(HasDataverseSecret));
        Status = $"Dataverse secret cleared for '{Selected.Name}'.";
    }

    [RelayCommand]
    private void SaveDiSecret()
    {
        if (Selected is null || string.IsNullOrEmpty(DiSecretInput))
        {
            return;
        }

        _secrets.SetSecret(DiKey(Selected.Id), DiSecretInput);
        DiSecretInput = string.Empty;
        OnPropertyChanged(nameof(HasDiSecret));
        DiStatus = "Service-account secret stored.";
    }

    [RelayCommand]
    private void ClearDiSecret()
    {
        if (Selected is null)
        {
            return;
        }

        _secrets.ClearSecret(DiKey(Selected.Id));
        OnPropertyChanged(nameof(HasDiSecret));
        DiStatus = "Service-account secret cleared.";
    }

    [RelayCommand]
    private async Task SignIn(CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DraftDiClientId))
        {
            DiStatus = "Enter a Data Integrator client ID before signing in.";
            return;
        }

        IsSigningIn = true;
        DiStatus = "Opening sign-in…";
        try
        {
            var result = await _broker.SignInAsync(DraftDiClientId, DraftTenant, ct);
            DiStatus = result is null ? "Sign-in cancelled." : $"Signed in as {result.Account}.";
        }
        catch (OperationCanceledException)
        {
            DiStatus = "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            DiStatus = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            IsSigningIn = false;
        }
    }

    // Tests the dual-write gateway connection using the (unsaved) draft client id + gateway URL, so the
    // user can verify before saving. Acquires the delegated token, builds the gateway, resolves linkage.
    [RelayCommand]
    private async Task TestGateway(CancellationToken ct)
    {
        if (Selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(DraftGatewayUrl))
        {
            DiStatus = "Enter a gateway URL before testing.";
            return;
        }

        IsTestingGateway = true;
        DiStatus = "Testing gateway…";
        try
        {
            var probe = Selected with
            {
                Url = DraftUrl,
                Tenant = DraftTenant,
                DataIntegratorClientId = string.IsNullOrWhiteSpace(DraftDiClientId) ? null : DraftDiClientId,
                DualWriteGatewayUrl = string.IsNullOrWhiteSpace(DraftGatewayUrl) ? null : DraftGatewayUrl,
            };
            var result = await _gatewayTester.TestAsync(probe, ct);
            DiStatus = result.Message;
        }
        catch (OperationCanceledException)
        {
            DiStatus = "Gateway test cancelled.";
        }
        catch (Exception ex)
        {
            DiStatus = $"Gateway test failed: {ex.Message}";
        }
        finally
        {
            IsTestingGateway = false;
        }
    }

    [RelayCommand]
    private void SetActive()
    {
        if (Selected is null)
        {
            return;
        }

        ActiveId = Selected.Id;
        _store.ActiveId = Selected.Id;
        Status = $"'{Selected.Name}' is now the active environment.";
        ActiveChanged?.Invoke(Selected.Id);
    }

    [RelayCommand]
    private void Save()
    {
        if (Selected is null)
        {
            return;
        }

        // Commit the editable drafts onto a new immutable record, persist, and swap it into the list
        // so the master + detail reflect the edit.
        var updated = Selected with
        {
            Name = DraftName,
            Url = DraftUrl,
            Tenant = DraftTenant,
            Legal = DraftLegal,
            Tier = DraftTier,
            DataverseUrl = string.IsNullOrWhiteSpace(DraftDataverseUrl) ? null : DraftDataverseUrl,
            DataverseClientId = string.IsNullOrWhiteSpace(DraftDataverseClientId) ? null : DraftDataverseClientId,
            DataverseAuthMode = DraftDataverseAuthMode,
            DataIntegratorClientId = string.IsNullOrWhiteSpace(DraftDiClientId) ? null : DraftDiClientId,
            DataIntegratorMode = DraftDiMode,
            DualWriteGatewayUrl = string.IsNullOrWhiteSpace(DraftGatewayUrl) ? null : DraftGatewayUrl,
            ClientId = string.IsNullOrWhiteSpace(DraftClientId) ? null : DraftClientId,
            AuthMode = DraftAuthMode,
        };

        _store.Save(updated);

        var index = Profiles.IndexOf(Selected);
        if (index >= 0)
        {
            Profiles[index] = updated;
        }

        Selected = updated;
        Status = $"Saved '{updated.Name}'.";
        ProfileSaved?.Invoke(updated);
    }
}
