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
    [InlineData(FormulaEditorKey.Right, FormulaEditorKey.None)]
    [InlineData(FormulaEditorKey.System, FormulaEditorKey.Right)]
    public void GetKeyboardSelectionTarget_UsesCtrlShiftArrowDataBoundary(FormulaEditorKey key, FormulaEditorKey systemKey)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(CellAddress.Parse("B2", sheet.Id), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(CellAddress.Parse("C2", sheet.Id), Cell.FromValue(new NumberValue(3)));
        sheet.SetCell(CellAddress.Parse("D2", sheet.Id), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(CellAddress.Parse("E2", sheet.Id), Cell.FromValue(new NumberValue(5)));

        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(key,
                systemKey,
                FormulaEditorModifiers.Control | FormulaEditorModifiers.Shift,
                CellAddress.Parse("B2", sheet.Id),
                sheet,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse("E2", sheet.Id));
    }

    [Theory]
    [InlineData(FormulaEditorKey.None, FormulaEditorKey.Right)]
    [InlineData(FormulaEditorKey.System, FormulaEditorKey.Right)]
    public void GetKeyboardSelectionTarget_NormalizesSyntheticSystemArrowKeys(FormulaEditorKey key, FormulaEditorKey systemKey)
    {
        var current = CellAddress.Parse("B2", SheetId);

        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(key,
                systemKey,
                FormulaEditorModifiers.Shift,
                current,
                sheet: null,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse("C2", SheetId));
    }

    [Theory]
    [InlineData(FormulaEditorKey.None, FormulaEditorKey.Home, "A2")]
    [InlineData(FormulaEditorKey.System, FormulaEditorKey.Home, "A2")]
    [InlineData(FormulaEditorKey.None, FormulaEditorKey.PageDown, "B22")]
    [InlineData(FormulaEditorKey.System, FormulaEditorKey.PageDown, "B22")]
    public void GetKeyboardSelectionTarget_NormalizesSyntheticSystemNavigationKeys(
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        string expected)
    {
        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(key,
                systemKey,
                FormulaEditorModifiers.None,
                CellAddress.Parse("B2", SheetId),
                sheet: null,
                rowPageSize: 20,
                colPageSize: 10)
            .Should()
            .Be(CellAddress.Parse(expected, SheetId));
    }

    [Theory]
    [InlineData(FormulaEditorKey.Right, FormulaEditorKey.None, FormulaEditorModifiers.Alt)]
    [InlineData(FormulaEditorKey.System, FormulaEditorKey.Right, FormulaEditorModifiers.Alt)]
    [InlineData(FormulaEditorKey.Right, FormulaEditorKey.None, FormulaEditorModifiers.Control | FormulaEditorModifiers.Alt)]
    public void GetKeyboardSelectionTarget_IgnoresUnsupportedAltNavigationChords(
        FormulaEditorKey key,
        FormulaEditorKey systemKey,
        FormulaEditorModifiers modifiers)
    {
        FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(key,
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
