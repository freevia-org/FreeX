using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class SubtotalPlannerTests
{
    [Fact]
    public void TryCreateSourceRange_TrimsWholeColumnSelectionToOccupiedRows()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 1, "Region");
        SetText(sheet, 1, 2, "Sales");
        SetText(sheet, 2, 1, "East");
        SetNumber(sheet, 2, 2, 10);
        SetText(sheet, 5, 1, "West");
        SetNumber(sheet, 5, 2, 20);
        var selectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        var success = SubtotalPlanner.TryCreateSourceRange(sheet, selectedRange, out var sourceRange, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        sourceRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 2)));
    }

    [Fact]
    public void TryCreateSourceRange_TrimsWholeRowSelectionToOccupiedColumns()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 2, "Region");
        SetText(sheet, 1, 4, "Sales");
        SetText(sheet, 2, 2, "East");
        SetNumber(sheet, 2, 4, 10);
        var selectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, CellAddress.MaxCol));

        var success = SubtotalPlanner.TryCreateSourceRange(sheet, selectedRange, out var sourceRange, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        sourceRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 2, 4)));
    }

    [Fact]
    public void TryCreateSourceRange_RejectsBroadSelectionWithoutOccupiedData()
    {
        var sheet = CreateSheet();
        var selectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

        var success = SubtotalPlanner.TryCreateSourceRange(sheet, selectedRange, out _, out var error);

        success.Should().BeFalse();
        error.Should().Be(SubtotalPlanner.NoOccupiedDataMessage);
    }

    [Fact]
    public void TryCreateSourceRange_RejectsTrimmedSingleColumnRange()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 3, "Region");
        SetText(sheet, 2, 3, "East");
        var selectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

        var success = SubtotalPlanner.TryCreateSourceRange(sheet, selectedRange, out _, out var error);

        success.Should().BeFalse();
        error.Should().Be(SubtotalPlanner.NotEnoughColumnsMessage);
    }

    [Fact]
    public void TryCreateSourceRange_AllowsIncompleteDialogSourceWhenShapeValidationIsDisabled()
    {
        var sheet = CreateSheet();
        SetText(sheet, 1, 3, "Region");
        SetText(sheet, 2, 3, "East");
        var selectedRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, CellAddress.MaxCol));

        var success = SubtotalPlanner.TryCreateSourceRange(
            sheet,
            selectedRange,
            out var sourceRange,
            out var error,
            requireCompleteTableShape: false);

        success.Should().BeTrue();
        error.Should().BeNull();
        sourceRange.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 3),
            new CellAddress(sheet.Id, 2, 3)));
    }

    private static Sheet CreateSheet() =>
        new(SheetId.New(), "Sheet1");

    private static void SetText(Sheet sheet, uint row, uint column, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, column), new TextValue(value));

    private static void SetNumber(Sheet sheet, uint row, uint column, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, column), new NumberValue(value));
}
