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

    // Identifies the newest field fetch, so only it may lower IsLoadingFields. Interlocked/Volatile because
    // a superseded fetch can unwind on a pool thread while the newest one is being started.
    private int _fieldsFetchSequence;

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
    [NotifyPropertyChangedFor(nameof(FieldsLoadingMessage))]
    private EntitySet? _selected;   // IsCached is updated (and notified) by LoadFields, not here.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNotCachedHint))]
    private bool _isCached;

    // True while the selected entity's properties are being fetched from the environment's $metadata.
    // Clicking an uncached entity is a live round-trip against a document that can be tens of MB; without
    // this the detail pane sat on the "aren't cached — open it in Query Builder" hint for the whole call,
    // which reads as a dead click rather than as work in progress.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsLoadingMessage))]
    [NotifyPropertyChangedFor(nameof(ShowNotCachedHint))]
    private bool _isLoadingFields;

    // Surfaces a $metadata load/auth failure so the view shows it instead of a silently blank list.
    [ObservableProperty]
    private string? _loadError;

    // True while a forced refresh is in flight; keeps the Refresh button from re-entering.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

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

    // Re-reads the entity list and the selected entity's properties straight from the environment,
    // bypassing the cached copies. The escape hatch for metadata that changed since it was cached (a
    // deployed entity, or a profile repointed at another environment) — Initialize alone would keep
    // serving the cache.
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task Refresh(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            await _metadata.LoadEntitiesAsync(forceRefresh: true, ct);
            LoadError = null;

            // Rebuilt unconditionally: an empty result is a legitimate answer (an environment with no
            // OData metadata, or one the refresh couldn't enumerate), and keeping the previous list would
            // leave the browser showing another environment's entities with the error already cleared. A
            // failed refresh throws instead, and is handled below without reaching here.
            var loaded = _metadata.GetEntities();
            var previous = Selected?.Name;
            Entities.Clear();
            foreach (var e in loaded)
            {
                Entities.Add(e);
            }

            Selected = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
            OnPropertyChanged(nameof(Filtered));

            // Refetch the selection's properties too, so the grid isn't left showing the cached ones.
            if (Selected is { } entity)
            {
                await _metadata.LoadFieldsAsync(entity.Name, forceRefresh: true, ct);
                if (Selected == entity)
                {
                    LoadFields();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // A cancelled refresh leaves the previously loaded list and fields in place.
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

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

        // Set unconditionally: the loader decides whether a fetch is actually needed, and for an entity
        // whose fields are already cached it returns without yielding — so the flag goes up and down inside
        // this one call, with no render pass in between to flicker.
        var fetchId = Interlocked.Increment(ref _fieldsFetchSequence);
        IsLoadingFields = true;
        try
        {
            var fetched = await _loader.EnsureFieldsAsync(entity.Name, ct);
            LoadError = _loader.LastError;
            if (fetched && Selected == entity)
            {
                LoadFields();
            }
        }
        finally
        {
            // Only the newest fetch may lower the flag, so a superseded one unwinding later (the user
            // clicked on to another entity mid-flight, cancelling this one) can't clear the indicator the
            // newer fetch just raised.
            //
            // Ownership is the fetch sequence, deliberately not "is my entity still selected": a selection
            // change that produces no fetch of its own — clearing the selection, which is what Refresh does
            // when the environment comes back with no entities — would otherwise leave nobody willing to
            // lower the flag, and the pane would spin forever. Nothing newer claimed the indicator, so this
            // fetch still owns it and still clears it.
            if (Volatile.Read(ref _fieldsFetchSequence) == fetchId)
            {
                IsLoadingFields = false;
            }
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

    /// <summary>Detail-pane progress text for an in-flight properties fetch.</summary>
    public string FieldsLoadingMessage => Selected is null ? string.Empty : $"Loading {Selected.Name}…";

    /// <summary>
    /// The "not cached" hint, suppressed while a fetch is in flight — during the fetch the fields aren't
    /// cached *yet*, and telling the user to go to Query Builder is wrong until the fetch has failed to
    /// produce them.
    /// </summary>
    public bool ShowNotCachedHint => !IsCached && !IsLoadingFields;

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
        var term = FieldSearch.Trim();
        FilteredFields.Clear();
        foreach (var f in Fields)
        {
            if (term.Length == 0
                || f.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || f.TypeDisplay.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredFields.Add(f);
            }
        }
    }
}
