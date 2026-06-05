using CommunityToolkit.Mvvm.ComponentModel;
using ToolBax.Core.Models;

namespace ToolBax.App.ViewModels;

/// <summary>One row in the Operations maps grid; <see cref="IsChecked"/> and <see cref="State"/>
/// drive action eligibility (see <see cref="DualWriteOpsViewModel"/>).</summary>
public partial class MapRowViewModel : ObservableObject
{
    public required string TableId { get; init; }
    public required string Name { get; init; }
    public required string FoEntity { get; init; }
    public required string DvEntity { get; init; }
    public DwDirection Direction { get; init; }
    public string TemplateVersion { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public long Rows24h { get; init; }
    public int Errors24h { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTransitional))]
    [NotifyPropertyChangedFor(nameof(StateText))]
    private MapState _state;

    [ObservableProperty]
    private bool _isChecked;

    public bool IsTransitional => DwActions.IsTransitional(State);

    /// <summary>Friendly state label for the grid (e.g. "pausing…" while transitional).</summary>
    public string StateText => IsTransitional ? $"{State.ToString().ToLowerInvariant()}…" : State.ToString();

    public string DirectionArrow => Direction switch
    {
        DwDirection.Both => "↔",
        DwDirection.FoToDv => "→",
        _ => "←",
    };

    /// <summary>"{fo} {arrow} {dv}" map identity for the Table-map column.</summary>
    public string MapDisplay => $"{FoEntity} {DirectionArrow} {DvEntity}";

    public static MapRowViewModel From(DwMap m) => new()
    {
        TableId = m.TableId,
        Name = m.Name,
        FoEntity = m.FoEntity,
        DvEntity = m.DvEntity,
        Direction = m.Direction,
        TemplateVersion = m.TemplateVersion,
        Author = m.Author,
        Rows24h = m.Rows24h,
        Errors24h = m.Errors24h,
        State = m.State,
    };
}
