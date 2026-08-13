using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class SelectionAreaPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    [Fact]
    public void AppendOrReplaceActiveArea_FirstAdditionalAreaPreservesCurrentSelection()
    {
        var current = Range(1, 1, 2, 2);
        var added = Range(4, 4, 4, 4);

        var result = SelectionAreaPlanner.AppendOrReplaceActiveArea(
            selectedRanges: null,
            current,
            added,
            startNewArea: true);

        result.Should().Equal(current, added);
    }

    [Fact]
    public void AppendOrReplaceActiveArea_FreshAreaAppendsInGestureOrder()
    {
        var existing = new[] { Range(1, 1, 1, 1), Range(3, 3, 3, 3) };
        var added = Range(6, 2, 8, 4);

        var result = SelectionAreaPlanner.AppendOrReplaceActiveArea(
            existing,
            currentActive: existing[^1],
            added,
            startNewArea: true);

        result.Should().Equal(existing[0], existing[1], added);
    }

    [Fact]
    public void AppendOrReplaceActiveArea_DragContinuationReplacesOnlyLastArea()
    {
        var first = Range(1, 1, 2, 2);
        var pressed = Range(5, 5, 5, 5);
        var dragged = Range(5, 5, 9, 7);
        var existing = new[] { first, pressed };

        var result = SelectionAreaPlanner.AppendOrReplaceActiveArea(
            existing,
            currentActive: pressed,
            dragged,
            startNewArea: false);

        result.Should().Equal(first, dragged);
        existing.Should().Equal(first, pressed);
    }

    [Fact]
    public void AppendOrReplaceActiveArea_ContinuationWithoutExistingSelectionCreatesActiveArea()
    {
        var active = Range(2, 3, 7, 8);

        var result = SelectionAreaPlanner.AppendOrReplaceActiveArea(
            selectedRanges: null,
            currentActive: null,
            active,
            startNewArea: false);

        result.Should().Equal(active);
    }

    private static GridRange Range(uint startRow, uint startColumn, uint endRow, uint endColumn) =>
        new(
            new CellAddress(SheetId, startRow, startColumn),
            new CellAddress(SheetId, endRow, endColumn));
}
