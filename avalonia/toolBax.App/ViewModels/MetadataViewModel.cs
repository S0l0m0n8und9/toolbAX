using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Metadata Browser (control-map §6): entity-set master list + the selected entity's property table.
/// When an entity's fields aren't cached, shows a "fetch via Query Builder" hint instead.
/// </summary>
public partial class MetadataViewModel : ObservableObject
{
    private readonly IMetadataService _metadata;
    private readonly EntityCatalogLoader _loader;

    public ObservableCollection<EntitySet> Entities { get; }
    public ObservableCollection<EntityField> Fields { get; } = new();

    /// <summary>The property rows as shown, after applying <see cref="FieldSearch"/>. The grid binds to
    /// this; <see cref="Fields"/> stays the full master.</summary>
    public ObservableCollection<EntityField> FilteredFields { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    /// <summary>Case-insensitive substring filter over the selected entity's property names/types.</summary>
    [ObservableProperty]
    private string _fieldSearch = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EntitySet? _selected;   // IsCached is updated (and notified) by LoadFields, not here.

    [ObservableProperty]
    private bool _isCached;

    // Surfaces a $metadata load/auth failure so the view shows it instead of a silently blank list.
    [ObservableProperty]
    private string? _loadError;

    public MetadataViewModel(IMetadataService metadata)
    {
        _metadata = metadata;
        _loader = new EntityCatalogLoader(metadata);
        // The fake seeds its catalogue synchronously, so this populates immediately; the real service
        // starts empty and fills in via InitializeAsync (triggered by the view on load).
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
        _selected = Entities.FirstOrDefault();
        LoadFields();
    }

    // Fetches the entity list (and the selected entity's fields) from the active environment's live
    // $metadata. The view calls this on load; with the fake it's a no-op over already-seeded data.
    [RelayCommand]
    private async Task Initialize(CancellationToken ct)
    {
        var loaded = await _loader.LoadEntitiesAsync(Entities.Select(e => e.Name).ToList(), ct);
        LoadError = _loader.LastError;
        if (loaded is not null)
        {
            var previous = Selected?.Name;
            Entities.Clear();
            foreach (var e in loaded)
            {
                Entities.Add(e);
            }

            Selected = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
            OnPropertyChanged(nameof(Filtered));
        }

        await LoadSelectedFieldsAsync(ct);
    }

    // Fetches the selected entity's fields if they aren't cached yet, then refreshes the grid.
    [RelayCommand]
    private Task LoadSelectedFields(CancellationToken ct) => LoadSelectedFieldsAsync(ct);

    private async Task LoadSelectedFieldsAsync(CancellationToken ct)
    {
        var entity = Selected;
        if (entity is null)
        {
            return;
        }

        var fetched = await _loader.EnsureFieldsAsync(entity.Name, ct);
        LoadError = _loader.LastError;
        if (fetched && Selected == entity)
        {
            LoadFields();
        }
    }

    public IEnumerable<EntitySet> Filtered =>
        string.IsNullOrWhiteSpace(Search)
            ? Entities
            : Entities.Where(e =>
                e.Name.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                e.Module.Contains(Search, StringComparison.OrdinalIgnoreCase));

    public bool HasSelection => Selected is not null;

    public string NotCachedMessage => Selected is null
        ? string.Empty
        : $"Fields for {Selected.Name} aren't cached — open it in Query Builder to fetch $metadata.";

    partial void OnSelectedChanged(EntitySet? value)
    {
        LoadFields();                              // show what's cached immediately
        LoadSelectedFieldsCommand.Execute(null);   // then fetch from $metadata if not cached yet
    }

    // The property search only re-filters what's displayed; Fields stays the master.
    partial void OnFieldSearchChanged(string value) => RefreshFieldFilter();

    private void LoadFields()
    {
        Fields.Clear();
        var fields = Selected is null ? null : _metadata.GetFields(Selected.Name);
        IsCached = fields is not null;
        if (fields is not null)
        {
            foreach (var f in fields)
            {
                Fields.Add(f);
            }
        }

        RefreshFieldFilter();
        OnPropertyChanged(nameof(NotCachedMessage));
    }

    // Rebuilds FilteredFields from Fields applying the (trimmed, case-insensitive) FieldSearch over the
    // property name and type.
    private void RefreshFieldFilter()
    {
        var term = FieldSearch?.Trim();
        FilteredFields.Clear();
        foreach (var f in Fields)
        {
            if (string.IsNullOrEmpty(term)
                || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || f.TypeDisplay.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFields.Add(f);
            }
        }
    }
}
