using System.Collections.ObjectModel;
using TheEye.Presentation.Collections;

namespace TheEye.Core.Tests;

public sealed class FilteredCollectionTests
{
    [Fact]
    public void Comparer_SortsFilteredItemsAfterSourceChanges()
    {
        var source = new ObservableCollection<Row>
        {
            new("Bravo", true),
            new("Hidden", false),
            new("Alpha", true)
        };
        var view = new FilteredCollection<Row>(source, row => row.IsVisible)
        {
            Comparer = Comparer<Row>.Create((left, right) =>
                StringComparer.Ordinal.Compare(left.Name, right.Name))
        };

        Assert.Equal(["Alpha", "Bravo"], view.Select(row => row.Name));

        source.Add(new Row("Charlie", true));

        Assert.Equal(["Alpha", "Bravo", "Charlie"], view.Select(row => row.Name));
    }

    private sealed record Row(string Name, bool IsVisible);
}