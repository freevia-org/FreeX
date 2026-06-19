using FluentAssertions;
using FreeX.App.Presentation.Protection;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Protection;

public sealed class AllowEditRangePlannerTests
{
    private static readonly SheetId Sheet = SheetId.New();

    [Theory]
    [InlineData("A1:B5", true)]
    [InlineData(" B2 ", true)]
    [InlineData("not a range", false)]
    [InlineData("", false)]
    public void TryParseRange_ParsesValidRangesOnly(string text, bool expected)
    {
        AllowEditRangePlanner.TryParseRange(text, Sheet, out _).Should().Be(expected);
    }

    [Fact]
    public void TryParseRange_BindsRangeToGivenSheet()
    {
        AllowEditRangePlanner.TryParseRange("A1:B2", Sheet, out var range).Should().BeTrue();
        range.Start.Sheet.Should().Be(Sheet);
        range.End.Sheet.Should().Be(Sheet);
    }

    [Fact]
    public void BuildExistingRangeItems_ProjectsRangesToA1Strings()
    {
        var ranges = new List<GridRange>
        {
            new(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 1, 1)),
            new(new CellAddress(Sheet, 2, 2), new CellAddress(Sheet, 3, 3)),
        };

        AllowEditRangePlanner.BuildExistingRangeItems(ranges).Should().Equal("A1:A1", "B2:C3");
    }

    [Fact]
    public void BuildExistingRangeItems_NullReturnsEmpty()
    {
        AllowEditRangePlanner.BuildExistingRangeItems(null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(2, true, true, true)]
    public void BuildButtonState_RequiresRangesAndSelection(int count, bool hasSelection, bool canModify, bool canDelete)
    {
        var state = AllowEditRangePlanner.BuildButtonState(count, hasSelection);

        state.CanModifySelectedRange.Should().Be(canModify);
        state.CanDeleteSelectedRange.Should().Be(canDelete);
        state.CanUsePermissions.Should().BeFalse();
    }

    [Fact]
    public void CreateResults_CarryActionAndRanges()
    {
        var a = new GridRange(new CellAddress(Sheet, 1, 1), new CellAddress(Sheet, 1, 1));
        var b = new GridRange(new CellAddress(Sheet, 2, 2), new CellAddress(Sheet, 2, 2));

        AllowEditRangePlanner.CreateAddResult(a).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Add, a));
        AllowEditRangePlanner.CreateModifyResult(a, b).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Modify, b, a));
        AllowEditRangePlanner.CreateRemoveResult(a).Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Remove, a));
        AllowEditRangePlanner.CreateClearResult().Should()
            .Be(new AllowEditRangeResult(AllowEditRangeAction.Clear, null));
    }
}
