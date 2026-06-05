using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    public ObservableCollection<EnvProfile> Profiles { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    private EnvProfile? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectedActive))]
    private string? _activeId;

    [ObservableProperty]
    private string _status = "Ready.";

    public ProfilesViewModel(IProfileStore store)
    {
        _store = store;
        Profiles = new ObservableCollection<EnvProfile>(store.GetAll());
        _activeId = store.ActiveId;
        _selected = Profiles.FirstOrDefault(p => p.Id == _activeId) ?? Profiles.FirstOrDefault();
    }

    public IEnumerable<EnvProfile> Filtered =>
        string.IsNullOrWhiteSpace(Search)
            ? Profiles
            : Profiles.Where(p =>
                p.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                p.Legal.Contains(Search, StringComparison.OrdinalIgnoreCase));

    public bool IsSelectedActive => Selected is not null && Selected.Id == ActiveId;

    /// <summary>Raised when the active profile changes, so the shell's switcher can stay in sync.</summary>
    public event Action<string>? ActiveChanged;

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

        _store.Save(Selected);
        Status = $"Saved '{Selected.Name}'.";
    }
}
