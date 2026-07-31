using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Microsoft.Extensions.Logging.Abstractions;
using SheetGridView = FreeX.App.UI.GridView;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void FormulaBarEscape_RestoresActiveCellTextAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("draft edit");

            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.FormulaBarText.Should().Be("original");
            harness.CellText(1, 1).Should().Be("original");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarEscape_RestoresActiveCellFormulaAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(B1:C1)");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=AVERAGE(B1:C1)");

            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(B1:C1)");
            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarEscape_WhileInlineEditorVisible_CancelsInlineEditAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft inline edit");
            harness.FocusFormulaBar();

            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarText.Should().Be("original");
            harness.CellText(1, 1).Should().Be("original");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarEscape_WhileInlineEditorVisible_CancelsFormulaBarDraftAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("formula bar draft");

            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarText.Should().Be("original");
            harness.CellText(1, 1).Should().Be("original");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarCancelButton_AfterPointModeSelection_ClearsRangeStateAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex(1);
            harness.ToggleFormulaRangeEntrySelectionMode(ModifierKeys.Shift);
            harness.PressFormulaBarKey(Key.Right).Should().BeTrue();
            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=B1,B2");
            harness.ClickFormulaBarCancelButton();

            harness.FormulaBarText.Should().Be("original");
            harness.FormulaRangeEntryMode.Should().BeFalse();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.SheetGridFocused.Should().BeTrue();
        });
    }
}
