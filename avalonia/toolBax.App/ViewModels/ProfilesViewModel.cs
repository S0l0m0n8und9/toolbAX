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

    public ObservableCollection<EnvProfile> Profiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    [NotifyPropertyChangedFor(nameof(HasSecret))]
    private EnvProfile? _selected;

    /// <summary>The Auth-tab client-secret entry. Write-only: stored on save, never loaded back.</summary>
    [ObservableProperty]
    private string _secretInput = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    private string? _activeId;

    [ObservableProperty]
    private string _status = "Ready.";

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

    // Data Integrator config drafts.
    [ObservableProperty]
    private string _draftDiClientId = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRopc))]
    [NotifyPropertyChangedFor(nameof(IsInteractive))]
    private DiAuthMode _draftDiMode = DiAuthMode.Interactive;

    /// <summary>The DI ROPC service-account secret entry. Write-only, like the Auth client secret.</summary>
    [ObservableProperty]
    private string _diSecretInput = string.Empty;

    [ObservableProperty]
    private bool _isSigningIn;

    [ObservableProperty]
    private string _diStatus = string.Empty;

    public DiAuthMode[] DiModes { get; } = { DiAuthMode.Interactive, DiAuthMode.Ropc };

    public ProfilesViewModel(IProfileStore store, ISecretStore? secrets = null, IInteractiveAuthBroker? broker = null)
    {
        _store = store;
        _secrets = secrets ?? new FakeSecretStore();
        _broker = broker ?? new FakeInteractiveAuthBroker();
        Profiles = new ObservableCollection<EnvProfile>(store.GetAll());
        _activeId = store.ActiveId;
        _selected = Profiles.FirstOrDefault(p => p.Id == _activeId) ?? Profiles.FirstOrDefault();
        LoadDrafts(_selected);
    }

    public bool IsRopc => DraftDiMode == DiAuthMode.Ropc;

    public bool IsInteractive => DraftDiMode == DiAuthMode.Interactive;

    private static string DiKey(string id) => $"{id}:di";

    partial void OnSelectedChanged(EnvProfile? value)
    {
        LoadDrafts(value);
        SecretInput = string.Empty; // never carry an entry across environments
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
        DraftDiClientId = profile?.DataIntegratorClientId ?? string.Empty;
        DraftDiMode = profile?.DataIntegratorMode ?? DiAuthMode.Interactive;
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

    /// <summary>Whether the selected environment has a DI ROPC service-account secret stored.</summary>
    public bool HasDiSecret => Selected is not null && _secrets.HasSecret(DiKey(Selected.Id));

    /// <summary>Raised when the active profile changes, so the shell's switcher can stay in sync.</summary>
    public event Action<string>? ActiveChanged;

    [RelayCommand]
    private void SaveSecret()
    {
        if (Selected is null || string.IsNullOrEmpty(SecretInput))
        {
            return;
        }

        _secrets.SetSecret(Selected.Id, SecretInput);
        SecretInput = string.Empty; // don't keep plaintext around after it's protected
        OnPropertyChanged(nameof(HasSecret));
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

        IsSigningIn = true;
        DiStatus = "Opening sign-in…";
        try
        {
            var result = await _broker.SignInAsync(DraftDiClientId, DraftTenant, ct);
            DiStatus = result is null ? "Sign-in cancelled." : $"Signed in as {result.Account}.";
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
            DataIntegratorClientId = string.IsNullOrWhiteSpace(DraftDiClientId) ? null : DraftDiClientId,
            DataIntegratorMode = DraftDiMode,
        };

        _store.Save(updated);

        var index = Profiles.IndexOf(Selected);
        if (index >= 0)
        {
            Profiles[index] = updated;
        }

        Selected = updated;
        Status = $"Saved '{updated.Name}'.";
    }
}
