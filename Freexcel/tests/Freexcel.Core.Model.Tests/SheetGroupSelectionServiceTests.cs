using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class SheetGroupSelectionServiceTests
{
    [Fact]
    public void SelectSingle_ReplacesGroupWithClickedSheet()
    {
        var ids = NewSheetIds(3);

        var selected = SheetGroupSelectionService.SelectSingle(ids[1]);

        selected.Should().Equal(ids[1]);
    }

    [Fact]
    public void ToggleSelection_RemovesExistingButKeepsAtLeastOneSelected()
    {
        var ids = NewSheetIds(2);
        var current = new HashSet<SheetId> { ids[0], ids[1] };

        var selected = SheetGroupSelectionService.Toggle(ids[1], current);

        selected.Should().Equal(ids[0]);
    }

    [Fact]
    public void ToggleSelection_AddsMissingSheet()
    {
        var ids = NewSheetIds(2);
        var current = new HashSet<SheetId> { ids[0] };

        var selected = SheetGroupSelectionService.Toggle(ids[1], current);

        selected.Should().BeEquivalentTo([ids[0], ids[1]]);
    }

    [Fact]
    public void SelectRange_SelectsVisibleSpanBetweenAnchorAndTarget()
    {
        var ids = NewSheetIds(5);

        var selected = SheetGroupSelectionService.SelectRange(ids, ids[1], ids[3]);

        selected.Should().Equal(ids[1], ids[2], ids[3]);
    }

    [Fact]
    public void SelectAll_ReturnsAllVisibleSheets()
    {
        var ids = NewSheetIds(3);

        var selected = SheetGroupSelectionService.SelectAll(ids);

        selected.Should().Equal(ids);
    }

    private static List<SheetId> NewSheetIds(int count)
    {
        return Enumerable.Range(0, count).Select(_ => SheetId.New()).ToList();
    }
}
