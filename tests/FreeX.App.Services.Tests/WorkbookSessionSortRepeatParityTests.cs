using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionSortRepeatParityTests
{
    [Fact]
    public void QuickSort_GroupedSheetsRegistersRepeatAndRebuildsAgainstCurrentSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(30, 20);
        var first = session.ActiveSheet;
        var second = session.Workbook.AddSheet("Second");
        SeedTwoSortAreas(first);
        SeedTwoSortAreas(second);
        session.SelectSheet(first.Id);
        session.SelectAllVisibleSheets().Should().BeTrue();

        var firstArea = Range(first, 1, 1, 3, 2);
        Select(session, firstArea);
        session.SortSelectedRange(ascending: true).Success.Should().BeTrue();

        AssertOrder(first, 1, 10, 20, 30);
        AssertOrder(second, 1, 10, 20, 30);
        session.CanRepeatLastAction.Should().BeTrue();

        var secondArea = Range(first, 1, 4, 3, 5);
        Select(session, secondArea);
        session.RepeatLastAction().Success.Should().BeTrue();

        AssertOrder(first, 4, 100, 200, 300);
        AssertOrder(second, 4, 100, 200, 300);
        session.SelectedRange.Should().Be(secondArea);
    }

    [Fact]
    public void CustomSort_RegistersRepeatAndRebuildsAgainstCurrentSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(30, 20);
        var sheet = session.ActiveSheet;
        SeedTwoSortAreas(sheet);
        var firstArea = Range(sheet, 1, 1, 3, 2);
        Select(session, firstArea);

        session.SortSelectedRange(
                [new SortKey(0, false)],
                new SortOptions(),
                hasHeaders: false)
            .Success.Should().BeTrue();

        AssertOrder(sheet, 1, 30, 20, 10);
        session.CanRepeatLastAction.Should().BeTrue();

        var secondArea = Range(sheet, 1, 4, 3, 5);
        Select(session, secondArea);
        session.RepeatLastAction().Success.Should().BeTrue();

        AssertOrder(sheet, 4, 300, 200, 100);
        session.SelectedRange.Should().Be(secondArea);
    }

    [Fact]
    public void SortSelectionError_IsSharedForMultiAreaAndSingleRowSelections()
    {
        using var session = new WorkbookSessionFactory().CreateNew(30, 20);
        var sheet = session.ActiveSheet;
        var first = Range(sheet, 1, 1, 3, 1);
        var second = Range(sheet, 1, 3, 3, 3);
        session.SynchronizeSelectionState(sheet.Id, first, [first, second], first.Start);

        session.GetSelectedRangeSortError().Should().Contain("multiple selected ranges");

        var singleRow = Range(sheet, 5, 1, 5, 4);
        Select(session, singleRow);
        session.GetSelectedRangeSortError().Should().Be("Select at least two rows to sort.");
    }

    private static void SeedTwoSortAreas(Sheet sheet)
    {
        SeedArea(sheet, 1, [30d, 10d, 20d]);
        SeedArea(sheet, 4, [300d, 100d, 200d]);
    }

    private static void SeedArea(Sheet sheet, uint startColumn, IReadOnlyList<double> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var row = (uint)index + 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, startColumn), new NumberValue(values[index]));
            sheet.SetCell(new CellAddress(sheet.Id, row, startColumn + 1), new TextValue($"row-{values[index]}"));
        }
    }

    private static void AssertOrder(Sheet sheet, uint column, params double[] values)
    {
        for (var index = 0; index < values.Length; index++)
            sheet.GetValue((uint)index + 1, column).Should().Be(new NumberValue(values[index]));
    }

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));

    private static void Select(WorkbookSession session, GridRange range) =>
        session.SynchronizeSelectionState(session.ActiveSheet.Id, range, [range], range.Start);
}
