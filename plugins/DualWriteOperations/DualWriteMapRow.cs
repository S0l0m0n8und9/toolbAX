using FoToolbox.Core.DualWrite;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DualWriteOperationsPlugin;

/// <summary>Bindable row wrapping a <see cref="DualWriteMap"/> with a selection checkbox.</summary>
public sealed class DualWriteMapRow : INotifyPropertyChanged
{
    private bool _isSelected;

    internal DualWriteMapRow(DualWriteMap map)
    {
        Map = map;
    }

    internal DualWriteMap Map { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Name => string.IsNullOrWhiteSpace(Map.DisplayName) ? Map.Name : Map.DisplayName;
    public string State => Map.State;
    public string Version => Map.CurrentVersion;
    public string Author => Map.CurrentAuthor;

    /// <summary>The CE (Dataverse) entity this map targets — the unit "Apply Integration Keys" operates on.</summary>
    public string CeEntity => Map.RightEntityName;

    /// <summary>
    /// Case-insensitive match of <paramref name="search"/> against the user-facing fields
    /// (name, CE entity, version, author, state) and identifiers (map id / raw name). A blank
    /// search matches everything. Used by the map-view search box (#31).
    /// </summary>
    public bool Matches(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var term = search.Trim();
        return Field(Name) || Field(CeEntity) || Field(Version) || Field(Author) || Field(State)
            || Field(Map.Id) || Field(Map.Name);

        bool Field(string? value) =>
            !string.IsNullOrEmpty(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
