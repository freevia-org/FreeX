using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
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

    [Fact]
    public void UpdateLevel_PreservesSortOnChoice()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false) { SortOn = "Font Color", TargetColor = "#FF0000" }
        };

        SortDialog.UpdateLevel(levels, 1, columnOffset: 2, ascending: true)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true) { SortOn = "Font Color", TargetColor = "#FF0000" });
    }

    [Fact]
    public void CopyLevel_InsertsDuplicateAfterRequestedLevel()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(2, false)
        };

        SortDialog.CopyLevel(levels, 1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, false),
                new SortDialogLevel(2, false));
    }

    [Fact]
    public void CopyLevel_PreservesSortOnChoice()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(2, false) { SortOn = "Cell Color", TargetColor = "#00FF00" }
        };

        SortDialog.CopyLevel(levels, 1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, false) { SortOn = "Cell Color", TargetColor = "#00FF00" },
                new SortDialogLevel(2, false) { SortOn = "Cell Color", TargetColor = "#00FF00" });
    }

    [Fact]
    public void MoveLevel_ReordersRequestedLevelWithinBounds()
    {
        var levels = new[]
        {
            new SortDialogLevel(0, true),
            new SortDialogLevel(1, false),
            new SortDialogLevel(2, true)
        };

        SortDialog.MoveLevel(levels, 2, -1)
            .Should()
            .Equal(
                new SortDialogLevel(0, true),
                new SortDialogLevel(2, true),
                new SortDialogLevel(1, false));

        SortDialog.MoveLevel(levels, 0, -1).Should().Equal(levels);
        SortDialog.MoveLevel(levels, 2, 1).Should().Equal(levels);
    }
}
