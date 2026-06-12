using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class FormulaRangeEntryPlannerTests
{
    [Fact]
    public void GetKeyboardCursor_UsesMovingSelectionCursorWhenFormulaRangeIsAlreadyExtended()
    {
        var cursor = CellAddress.Parse("B1", SheetId);

        FormulaRangeEntryPlanner.GetKeyboardCursor(Range("A1", "B1"), cursor)
            .Should()
            .Be(cursor);
    }

    [Fact]
    public void GetKeyboardCursor_FallsBackToRangeStartWhenNoSelectionCursorExists()
    {
        FormulaRangeEntryPlanner.GetKeyboardCursor(Range("A1", "B1"), selectionCursor: null)
            .Should()
            .Be(CellAddress.Parse("A1", SheetId));
    }

    [Theory]
    [InlineData(Key.Right, Key.None)]
    [InlineData(Key.System, Key.Right)]
    public void GetKeyboardSelectionTarget_UsesCtrlShiftArrowDataBoundary(Key key, Key systemKey)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(CellAddress.Parse("B2", sheet.Id), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(CellAddress.Parse("C2", sheet.Id), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(CellAddress.Parse("D2", sheet.Id), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(CellAddress.Parse("E2", sheet.Id), Cell.FromValue(new NumberValue(5)));

        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                key,
                systemKey,
                ModifierKeys.Control | ModifierKeys.Shift,
                CellAddress.Parse("B2", sheet.Id),
                sheet,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse("E2", sheet.Id));
    }

    [Theory]
    [InlineData(Key.None, Key.Right)]
    [InlineData(Key.System, Key.Right)]
    public void GetKeyboardSelectionTarget_NormalizesSyntheticSystemArrowKeys(Key key, Key systemKey)
    {
        var current = CellAddress.Parse("B2", SheetId);

        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                key,
                systemKey,
                ModifierKeys.Shift,
                current,
                sheet: null,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse("C2", SheetId));
    }

    [Theory]
    [InlineData(Key.None, Key.Home, "A2")]
    [InlineData(Key.System, Key.Home, "A2")]
    [InlineData(Key.None, Key.PageDown, "B22")]
    [InlineData(Key.System, Key.PageDown, "B22")]
    public void GetKeyboardSelectionTarget_NormalizesSyntheticSystemNavigationKeys(
        Key key,
        Key systemKey,
        string expected)
    {
        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                key,
                systemKey,
                ModifierKeys.None,
                CellAddress.Parse("B2", SheetId),
                sheet: null,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse(expected, SheetId));
    }

    [Theory]
    [InlineData(Key.Right, Key.None, ModifierKeys.Alt)]
    [InlineData(Key.System, Key.Right, ModifierKeys.Alt)]
    [InlineData(Key.Right, Key.None, ModifierKeys.Control | ModifierKeys.Alt)]
    public void GetKeyboardSelectionTarget_IgnoresUnsupportedAltNavigationChords(
        Key key,
        Key systemKey,
        ModifierKeys modifiers)
    {
        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                key,
                systemKey,
                modifiers,
                CellAddress.Parse("B2", SheetId),
                sheet: null,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .BeNull();
    }
}
