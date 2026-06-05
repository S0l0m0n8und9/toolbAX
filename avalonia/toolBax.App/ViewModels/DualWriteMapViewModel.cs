using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.ViewModels;

/// <summary>
/// Dual-Write Map Browser (control-map §4): read-only master/detail inspector. The master list +
/// search drive a selected map; selection loads its cached detail (KPIs, 24h activity, bindings,
/// value maps). No mutations — acting on a map is the Operations screen's job. Run history + errors
/// are a §4 follow-up.
/// </summary>
public partial class DualWriteMapViewModel : ObservableObject
{
    private readonly IDualWriteMapService _service;

    public ObservableCollection<DwMapSummary> Maps { get; }
    public ObservableCollection<DwBinding> Bindings { get; } = new();
    public ObservableCollection<DwValueMap> ValueMaps { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Filtered))]
    private string _search = string.Empty;

    // Bound to the ListBox. Filtering can null this out when the selected item leaves the result set;
    // DetailMap (below) is what actually drives the detail pane so the panel doesn't get wiped.
    [ObservableProperty]
    private DwMapSummary? _selectedMap;

    // The map whose detail is shown. Only ever advanced by a real (non-null) selection, so a search
    // that hides the current row leaves the detail intact.
    [ObservableProperty]
    private DwMapSummary? _detailMap;

    [ObservableProperty]
    private bool _hasBindings;

    [ObservableProperty]
    private string _latencyP95 = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<double> _activity = Array.Empty<double>();

    public DualWriteMapViewModel(IDualWriteMapService service)
    {
        _service = service;
        Maps = new ObservableCollection<DwMapSummary>(service.GetMaps());
        SelectedMap = Maps.FirstOrDefault();
    }

    public IEnumerable<DwMapSummary> Filtered =>
        string.IsNullOrWhiteSpace(Search)
            ? Maps
            : Maps.Where(m =>
                m.FoEntity.Contains(Search, StringComparison.OrdinalIgnoreCase) ||
                m.DvEntity.Contains(Search, StringComparison.OrdinalIgnoreCase));

    public bool HasValueMaps => ValueMaps.Count > 0;

    public bool HasErrors => DetailMap?.HasErrors ?? false;

    public string NotCachedMessage => DetailMap is null
        ? string.Empty
        : $"Field bindings for {DetailMap.FoEntity} aren't cached — open the map once to fetch its template.";

    partial void OnSelectedMapChanged(DwMapSummary? value)
    {
        // Ignore the null the ListBox emits when filtering hides the current row — keep the detail.
        if (value is not null)
        {
            DetailMap = value;
        }
    }

    partial void OnDetailMapChanged(DwMapSummary? value)
    {
        LoadDetail(value);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(NotCachedMessage));
    }

    private void LoadDetail(DwMapSummary? summary)
    {
        Bindings.Clear();
        ValueMaps.Clear();

        if (summary is null)
        {
            HasBindings = false;
            LatencyP95 = string.Empty;
            Activity = Array.Empty<double>();
            OnPropertyChanged(nameof(HasValueMaps));
            return;
        }

        var detail = _service.GetDetail(summary.Id);
        LatencyP95 = detail.LatencyP95;
        Activity = detail.Activity;

        foreach (var b in detail.Bindings)
        {
            Bindings.Add(b);
        }

        foreach (var vm in detail.ValueMaps)
        {
            ValueMaps.Add(vm);
        }

        HasBindings = Bindings.Count > 0;
        OnPropertyChanged(nameof(HasValueMaps));
    }
}
