using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FoToolbox.SDK.Collections;

/// <summary>
/// ObservableCollection that can replace its contents with a single Reset notification.
/// This avoids UI freezes when loading thousands of rows into WPF controls.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    public void ReplaceAll(IEnumerable<T> items)
    {
        if (items is null) throw new ArgumentNullException(nameof(items));

        _suppressNotifications = true;
        try
        {
            Clear();
            foreach (var item in items)
            {
                Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suppressNotifications) return;
        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_suppressNotifications) return;
        base.OnPropertyChanged(e);
    }
}

