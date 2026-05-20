using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class SortDialogTests
{
    [Fact]
    public void BuildSortKeys_ReturnsTypedSortKeysInLevelOrder()
    {
        var levels = new[]
        {
            new SortDialogLevel(2, true),
            new SortDialogLevel(0, false)
        };

        var keys = SortDialog.BuildSortKeys(levels);

        keys.Should().Equal(
            new SortKey(2, true),
            new SortKey(0, false));
    }

    [Fact]
    public void AddLevel_AppendsAscendingFirstColumnLevelByDefault()
    {
        var levels = new[] { new SortDialogLevel(1, false) };

        var updated = SortDialog.AddLevel(levels);

        updated.Should().Equal(
            new SortDialogLevel(1, false),
            new SortDialogLevel(0, true));
    }

    [Fact]
    public void RemoveLevel_RemovesRequestedLevelButKeepsAtLeastOneDefaultLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(1, false),
            new SortDialogLevel(2, true)
        };

        SortDialog.RemoveLevel(levels, 0).Should().Equal(new SortDialogLevel(2, true));
        SortDialog.RemoveLevel([new SortDialogLevel(3, false)], 0)
            .Should()
            .Equal(new SortDialogLevel(0, true));
    }

    [Fact]
    public void BuildColumnChoices_UsesSelectedRangeColumnsInDisplayOrder()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 2, 3),
            new CellAddress(sheetId, 7, 5));

        SortDialog.BuildColumnChoices(range).Should().Equal(
            new SortColumnChoice("Column C", 0),
            new SortColumnChoice("Column D", 1),
            new SortColumnChoice("Column E", 2));
    }

    [Fact]
    public void UpdateLevel_ReplacesRequestedSortLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false)
        };

        SortDialog.UpdateLevel(levels, 1, columnOffset: 2, ascending: true)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true));
    }
}
