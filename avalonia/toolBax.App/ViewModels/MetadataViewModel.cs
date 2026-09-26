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
/// Metadata Browser (control-map §6): entity-set master list + the selected entity's property table.
/// When an entity's fields aren't cached, shows a "fetch via Query Builder" hint instead.
/// </summary>
public partial class MetadataViewModel : ObservableObject, IDisposable
{
    private readonly IMetadataService _metadata;
    private readonly EntityCatalogLoader _loader;
    private readonly Func<EnvProfile?>? _activeEnvironment;
    private bool _disposed;
    private int _lifecycleGeneration;

    // Identifies the newest field fetch, so only it may lower IsLoadingFields. Interlocked/Volatile because
    // a superseded fetch can unwind on a pool thread while the newest one is being started.
    private int _fieldsFetchSequence;
    private int _catalogReadSequence;
    private int _pendingReads;
    public bool HasPendingReads => Volatile.Read(ref _pendingReads) > 0;

    private void BeginRead()
    {
        Interlocked.Increment(ref _pendingReads);
        OnPropertyChanged(nameof(HasPendingReads));
        CancelReadsCommand.NotifyCanExecuteChanged();
    }

    private void EndRead()
    {
        Interlocked.Decrement(ref _pendingReads);
        OnPropertyChanged(nameof(HasPendingReads));
        CancelReadsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasPendingReads))]
    private void CancelReads()
    {
        InitializeCommand.Cancel();
        RefreshCommand.Cancel();
        LoadSelectedFieldsCommand.Cancel();
    }

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

    // True while the current catalogue read is in flight; its owner alone clears the flag.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    public MetadataViewModel(IMetadataService metadata, Func<EnvProfile?>? activeEnvironment = null)
    {
        _metadata = metadata;
        _loader = new EntityCatalogLoader(metadata);
        _activeEnvironment = activeEnvironment;
        // The fake seeds its catalogue synchronously, so this populates immediately; the real service
        // starts empty and fills in via InitializeAsync (triggered by the view on load).
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
        _selected = Entities.FirstOrDefault();
        LoadFields();
    }

    private bool TryCaptureLifecycle(out EnvironmentIdentity? identity, out int generation)
    {
        generation = _lifecycleGeneration;
        identity = null;
        if (_disposed)
        {
            return false;
        }

        if (_activeEnvironment is null)
        {
            return true;
        }

        var profile = _activeEnvironment();
        if (profile is null)
        {
            return false;
        }

        identity = EnvironmentIdentity.Create(profile);
        return true;
    }

    private bool IsLifecycleCurrent(EnvironmentIdentity? identity, int generation) =>
        !_disposed
        && generation == _lifecycleGeneration
        && (_activeEnvironment is null || identity is not null && identity.IsCurrent(_activeEnvironment()));

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Interlocked.Increment(ref _lifecycleGeneration);
        InitializeCommand.Cancel();
        RefreshCommand.Cancel();
        LoadSelectedFieldsCommand.Cancel();
        _loader.Dispose();
    }

    // Fetches the entity list (and the selected entity's fields) from the active environment's live
    // $metadata. The view calls this on load; with the fake it's a no-op over already-seeded data.
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task Initialize(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !TryCaptureLifecycle(out var identity, out var lifecycleGeneration)) return;
        var owner = Interlocked.Increment(ref _catalogReadSequence);
        RefreshCommand.Cancel();
        IsBusy = true;
        BeginRead();
        try
        {
            var loaded = await _loader.LoadEntitiesAsync(Entities.Select(e => e.Name).ToList(), ct);
            if (ct.IsCancellationRequested || owner != Volatile.Read(ref _catalogReadSequence) || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            LoadError = _loader.LastError;
            if (loaded is not null)
            {
                var previous = Selected?.Name;
                Entities.Clear();
                foreach (var entity in loaded) Entities.Add(entity);
                Selected = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
                OnPropertyChanged(nameof(Filtered));
            }
            if (!ct.IsCancellationRequested) await LoadSelectedFieldsAsync(ct);
        }
        finally
        {
            if (owner == Volatile.Read(ref _catalogReadSequence)) IsBusy = false;
            EndRead();
        }
    }
    // Re-reads the entity list and the selected entity's properties straight from the environment,
    // bypassing the cached copies. The escape hatch for metadata that changed since it was cached (a
    // deployed entity, or a profile repointed at another environment) — Initialize alone would keep
    // serving the cache.
    [RelayCommand(IncludeCancelCommand = true, CanExecute = nameof(CanRefresh))]
    private async Task Refresh(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !TryCaptureLifecycle(out var identity, out var lifecycleGeneration)) return;
        var owner = Interlocked.Increment(ref _catalogReadSequence);
        InitializeCommand.Cancel();
        IsBusy = true;
        BeginRead();
        try
        {
            await _metadata.LoadEntitiesAsync(forceRefresh: true, ct);
            if (ct.IsCancellationRequested || owner != Volatile.Read(ref _catalogReadSequence) || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            var loaded = _metadata.GetEntities();
            if (ct.IsCancellationRequested || owner != Volatile.Read(ref _catalogReadSequence) || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            var previous = Selected?.Name;
            LoadError = null;
            Entities.Clear();
            foreach (var entity in loaded) Entities.Add(entity);
            Selected = Entities.FirstOrDefault(e => e.Name == previous) ?? Entities.FirstOrDefault();
            OnPropertyChanged(nameof(Filtered));
            LoadSelectedFieldsCommand.Cancel();
            if (!ct.IsCancellationRequested) await LoadSelectedFieldsAsync(ct, forceRefresh: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || owner != Volatile.Read(ref _catalogReadSequence) || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            LoadError = ex.Message;
        }
        finally
        {
            if (owner == Volatile.Read(ref _catalogReadSequence)) IsBusy = false;
            EndRead();
        }
    }
    private bool CanRefresh() => !_disposed && !IsBusy && (_activeEnvironment is null || _activeEnvironment() is not null);

    // Fetches the selected entity's fields if they aren't cached yet, then refreshes the grid.
    [RelayCommand(IncludeCancelCommand = true)]
    private Task LoadSelectedFields(CancellationToken ct) => LoadSelectedFieldsAsync(ct);

    private async Task LoadSelectedFieldsAsync(CancellationToken ct, bool forceRefresh = false)
    {
        if (ct.IsCancellationRequested || !TryCaptureLifecycle(out var identity, out var lifecycleGeneration)) return;
        var fetchId = Interlocked.Increment(ref _fieldsFetchSequence);
        var entity = Selected;
        if (entity is null)
        {
            IsLoadingFields = false;
            return;
        }
        IsLoadingFields = true;
        BeginRead();
        try
        {
            var fetched = forceRefresh
                ? await _metadata.LoadFieldsAsync(entity.Name, forceRefresh: true, ct)
                : await _loader.EnsureFieldsAsync(entity.Name, ct);
            if (ct.IsCancellationRequested || fetchId != Volatile.Read(ref _fieldsFetchSequence)
                || Selected != entity || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            LoadError = forceRefresh ? null : _loader.LastError;
            if (fetched || forceRefresh) LoadFields();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested || fetchId != Volatile.Read(ref _fieldsFetchSequence)
                || Selected != entity || !IsLifecycleCurrent(identity, lifecycleGeneration)) return;
            LoadError = ex.Message;
        }
        finally
        {
            if (fetchId == Volatile.Read(ref _fieldsFetchSequence)) IsLoadingFields = false;
            EndRead();
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
        if (_disposed)
        {
            return;
        }
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
