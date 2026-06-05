using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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

    public ObservableCollection<EntitySet> Entities { get; }
    public ObservableCollection<EntityField> Fields { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EntitySet? _selected;   // IsCached is updated (and notified) by LoadFields, not here.

    [ObservableProperty]
    private bool _isCached;

    public MetadataViewModel(IMetadataService metadata)
    {
        _metadata = metadata;
        Entities = new ObservableCollection<EntitySet>(metadata.GetEntities());
        _selected = Entities.FirstOrDefault();
        LoadFields();
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

    partial void OnSelectedChanged(EntitySet? value) => LoadFields();

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

        OnPropertyChanged(nameof(NotCachedMessage));
    }
}
