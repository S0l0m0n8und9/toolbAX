using FoToolbox.Core.DualWrite;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
