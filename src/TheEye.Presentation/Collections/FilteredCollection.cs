using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TheEye.Presentation.Collections;

/// <summary>
/// A live, filtered projection of a source collection. WPF has
/// <c>ICollectionView</c> for this and Avalonia has <c>DataGridCollectionView</c>,
/// but both are toolkit types; this keeps the traffic lists, which a search box
/// narrows, in the shared layer.
/// </summary>
public sealed class FilteredCollection<T> : ObservableCollection<T>
    where T : class
{
    private readonly ObservableCollection<T> _source;
    private Func<T, bool> _filter;

    public FilteredCollection(ObservableCollection<T> source, Func<T, bool>? filter = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _filter = filter ?? (_ => true);

        _source.CollectionChanged += OnSourceChanged;
        Rebuild();
    }

    /// <summary>Replaces the predicate and re-applies it to every item.</summary>
    public Func<T, bool> Filter
    {
        get => _filter;
        set
        {
            _filter = value ?? throw new ArgumentNullException(nameof(value));
            Rebuild();
        }
    }

    /// <summary>Re-runs the predicate, e.g. after an item's search text changed.</summary>
    public void Refresh() => Rebuild();

    private void OnSourceChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// Rebuilds the projection in one reset notification: adding thousands of
    /// aircraft one by one would have a data grid re-measure on every item.
    /// </summary>
    private void Rebuild()
    {
        CheckReentrancy();

        var matched = new List<T>(_source.Count);
        foreach (var item in _source)
        {
            if (_filter(item))
            {
                matched.Add(item);
            }
        }

        Items.Clear();
        foreach (var item in matched)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
