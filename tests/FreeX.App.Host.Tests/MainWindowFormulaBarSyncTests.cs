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

public sealed class MainWindowFormulaBarSyncTests
{
    [Fact]
    public void NewWorkbook_SelectsA1AndBindsFormulaBarEditsToA1()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expected = new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1));

            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("fresh value");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1).Should().Be("fresh value");
            harness.SelectedRange.Should().Be(expected);
            harness.CellAddressBoxText.Should().Be("A1");
        });
    }

    [Fact]
    public void InsertedSheet_RebindsActiveCellToCurrentSheet()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var firstSheetId = harness.CurrentSheetId;

            harness.SetFormulaBarText("first sheet");
            harness.CommitEdit().Should().BeTrue();
            harness.InsertNewSheet();

            harness.CurrentSheetId.Should().NotBe(firstSheetId);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("A1");

            harness.SetFormulaBarText("second sheet");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1, firstSheetId).Should().Be("first sheet");
            harness.CellText(1, 1, harness.CurrentSheetId).Should().Be("second sheet");
        });
    }

    [Fact]
    public void ClearSelection_RefreshesFormulaBarForClearedActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "stale text");
            harness.SelectActiveCell(1, 1);
            harness.FormulaBarText.Should().Be("stale text");

            harness.ClearSelectedContents();

            harness.CellText(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().BeEmpty();
        });
    }

    [Fact]
    public void InlineEditorTextChange_RefreshesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetInlineEditorText("typed inline");

            harness.FormulaBarText.Should().Be("typed inline");
        });
    }

    [Fact]
    public void FormulaBarTextChange_WhileInlineEditorVisible_RefreshesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);

            harness.SetFormulaBarText("typed in formula bar");

            harness.InlineEditorText.Should().Be("typed in formula bar");
        });
    }

    [Fact]
    public void FocusFormulaBar_WhileInlineEditorVisible_DoesNotCommitDraftEdit()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft edit");

            harness.FocusFormulaBar();

            harness.FormulaBarFocused.Should().BeTrue();
            harness.InlineEditorVisible.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
            harness.FormulaBarText.Should().Be("draft edit");
        });
    }

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
    public void FormulaBarEnter_CommitsEditMovesSelectionAndRefreshesEditors()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("entered from formula bar");

            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("entered from formula bar");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarEnter_CommitsFormulaMovesSelectionAndRefreshesEditors()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=SUM(B1:C1)");

            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarTab_CommitsEditMovesSelectionRightAndRefreshesEditors()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("tabbed from formula bar");

            harness.PressFormulaBarKey(Key.Tab).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("tabbed from formula bar");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));
            harness.CellAddressBoxText.Should().Be("B1");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void CtrlEnterFormulaBarEdit_FillsSelectedRangeWhenNotChoosingFormulaReferences()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(1, 1, 2, 2);
            harness.SetFormulaEditCell(1, 1);
            harness.SetFormulaBarText("filled");

            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: false).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("filled");
            harness.CellText(1, 2).Should().Be("filled");
            harness.CellText(2, 1).Should().Be("filled");
            harness.CellText(2, 2).Should().Be("filled");
        });
    }

    [Fact]
    public void CtrlEnterFormulaBarEdit_ClearsFormulaEditCellAfterFillingSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(3, 3, 4, 4);
            harness.SetFormulaEditCell(1, 1);
            harness.SetFormulaBarText("filled range");

            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: false).Should().BeTrue();
            harness.SetFormulaBarText("next edit");
            harness.CommitEdit().Should().BeTrue();

            harness.CellText(1, 1).Should().BeNull();
            harness.CellText(3, 3).Should().Be("next edit");
        });
    }

    [Fact]
    public void CtrlEnterFormulaReferenceEntry_CommitsOnlyOriginalEditCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectRange(3, 3, 4, 4);
            harness.SetFormulaEditCell(1, 1);
            harness.SetFormulaBarText("=C3");

            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: true).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("C3");
            harness.CellFormula(3, 3).Should().BeNull();
            harness.CellFormula(3, 4).Should().BeNull();
            harness.CellFormula(4, 3).Should().BeNull();
            harness.CellFormula(4, 4).Should().BeNull();
        });
    }

    [Fact]
    public void FunctionMenuInsertion_SeedsFormulaBarWithoutInlineEditorOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");

            harness.FormulaBarText.Should().Be("=SUM(");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(".Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void FunctionMenuInsertion_EnterCommitsCompletedFormulaToActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");
            harness.SetFormulaBarText("=SUM(1,2)");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("SUM(1,2)");
            harness.CellText(1, 1).Should().BeNull();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FunctionMenuInsertion_EscapeRestoresOriginalCellText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertFormulaFunction("SUM");
            harness.SetFormulaBarText("=SUM(1,2)");
            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().Be("original");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void UseInFormulaInsertion_SeedsFormulaBarWithoutInlineEditorOverwrite()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("=");

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SalesData");
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_ReplacesDisplayedActiveCellValueWithFormulaSeed()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SalesData");
            harness.FormulaBarCaretIndex.Should().Be("=SalesData".Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_EnterCommitsFormulaToActiveCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("SalesData");
            harness.CellText(1, 1).Should().BeNull();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void UseInFormulaInsertion_EscapeRestoresOriginalCellText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);

            harness.InsertDefinedNameIntoFormula("SalesData");
            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.FormulaBarText.Should().Be("original");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void UseInFormulaInsertion_InsertsDefinedNameAtFormulaBarCaret()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("=SUM(,B1)");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SUM(SalesData,B1)");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(SalesData".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_InsertsDefinedNameIntoDisplayedActiveFormulaAtCaret()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(,B1)");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=SUM(SalesData,B1)");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(SalesData".Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().Be("SUM(,B1)");
        });
    }

    [Fact]
    public void UseInFormulaInsertion_PrependsFormulaPrefixForPlainFormulaBarText()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaBarText("A1+");
            harness.SetFormulaBarCaretIndex("A1+".Length);

            harness.InsertDefinedNameIntoFormula("SalesData");

            harness.FormulaBarText.Should().Be("=A1+SalesData");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
        });
    }

    [Fact]
    public void EditInFormulaBar_LoadsActiveCellFormulaAndFocusesFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(B1:C1)");
            harness.SelectActiveCell(1, 1);

            harness.EditActiveCellInFormulaBar();

            harness.FormulaBarText.Should().Be("=SUM(B1:C1)");
            harness.FormulaBarCaretIndex.Should().Be(harness.FormulaBarText.Length);
            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
        });
    }

    [Fact]
    public void FormulaBarExpandButton_TogglesMultilineEntryAndAccessibilityName()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            if (harness.FormulaBarAcceptsReturn)
                harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarHeight.Should().Be(double.NaN);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_ExpandFormulaBar"));

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeTrue();
            harness.FormulaBarHeight.Should().Be(84);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_CollapseFormulaBar"));

            harness.ToggleFormulaBarExpansion();

            harness.FormulaBarAcceptsReturn.Should().BeFalse();
            harness.FormulaBarHeight.Should().Be(double.NaN);
            harness.FormulaBarExpandButtonAutomationName.Should().Be(UiText.Get("MainWindow_AutomationName_ExpandFormulaBar"));
        });
    }

    [Fact]
    public void NameBoxEnter_NavigatesRefreshesFormulaBarAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(5, 3, "target cell");
            harness.SetCellAddressBoxText("C5");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 5, 3),
                new CellAddress(harness.CurrentSheetId, 5, 3)));
            harness.CellAddressBoxText.Should().Be("C5");
            harness.FormulaBarText.Should().Be("target cell");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedCellReference_NavigatesRefreshesFormulaBarAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(5, 3, "padded target cell");
            harness.SetCellAddressBoxText("  C5  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 5, 3),
                new CellAddress(harness.CurrentSheetId, 5, 3)));
            harness.CellAddressBoxText.Should().Be("C5");
            harness.FormulaBarText.Should().Be("padded target cell");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithRangeReference_SelectsRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(2, 2, "range start");
            harness.SetCellText(3, 3, "range end");
            harness.SetCellAddressBoxText("B2:C3");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3)));
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("range start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "named range start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("SalesData");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("named range start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithDifferentCaseDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "case-insensitive name start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("salesdata");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("case-insensitive name start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedDefinedName_SelectsNamedRangeAndRefreshesFormulaBarFromStartCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SetCellText(2, 2, "padded name start");
            harness.DefineNamedRange("SalesData", expectedRange);
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("  SalesData  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.FormulaBarText.Should().Be("padded name start");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithInvalidReference_DoesNotChangeSelectionOrFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "active cell");
            harness.SelectActiveCell(1, 1);
            harness.SetCellAddressBoxText("not a reference");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("not a reference");
            harness.CellAddressBoxFocused.Should().BeTrue();
            harness.CellAddressBoxSelectionLength.Should().Be(harness.CellAddressBoxText.Length);
            harness.FormulaBarText.Should().Be("active cell");
        });
    }

    [Fact]
    public void NameBoxEnter_WithValidNewName_DefinesNameForSelectedRangeAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 4, 3));

            harness.SelectRange(2, 2, 4, 3);
            harness.SetCellAddressBoxText("SalesData");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.NamedRange("SalesData").Should().Be(expectedRange);
            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("SalesData");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEnter_WithPaddedValidNewName_DefinesTrimmedNameForSelectedRange()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 4, 3));

            harness.SelectRange(2, 2, 4, 3);
            harness.SetCellAddressBoxText("  SalesData  ");

            harness.PressCellAddressBoxKey(Key.Enter).Should().BeTrue();

            harness.NamedRange("SalesData").Should().Be(expectedRange);
            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("SalesData");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void NameBoxEscape_RestoresSelectedRangeReferenceAndReturnsFocusToGrid()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var expectedRange = new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 2),
                new CellAddress(harness.CurrentSheetId, 3, 3));

            harness.SelectRange(2, 2, 3, 3);
            harness.SetCellAddressBoxText("Z99");

            harness.PressCellAddressBoxKey(Key.Escape).Should().BeTrue();

            harness.SelectedRange.Should().Be(expectedRange);
            harness.CellAddressBoxText.Should().Be("B2:C3");
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    private sealed class MainWindowHarness : IDisposable
    {
        private readonly MainWindow _window;
        private readonly FieldInfo _workbookField;
        private readonly FieldInfo _currentSheetIdField;
        private readonly FieldInfo _formulaEditCellField;
        private readonly FieldInfo _inlineEditorField;
        private readonly MethodInfo _commitEdit;
        private readonly MethodInfo _commitEditAcrossSelection;
        private readonly MethodInfo _insertNewSheet;
        private readonly MethodInfo _setActiveCell;
        private readonly MethodInfo _showInlineEditor;
        private readonly MethodInfo _executeClearSelection;
        private readonly MethodInfo _formulaBarKeyDown;
        private readonly MethodInfo _cellAddressBoxKeyDown;
        private readonly MethodInfo _insertFormulaFunction;
        private readonly MethodInfo _insertDefinedNameIntoFormula;
        private readonly MethodInfo _formulaBarExpandButtonClick;
        private readonly MethodInfo _editActiveCellInFormulaBar;

        private MainWindowHarness(MainWindow window)
        {
            _window = window;
            _workbookField = typeof(MainWindow)
                .GetField("_workbook", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_workbook");
            _currentSheetIdField = typeof(MainWindow)
                .GetField("_currentSheetId", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_currentSheetId");
            _formulaEditCellField = typeof(MainWindow)
                .GetField("_formulaEditCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_formulaEditCell");
            _inlineEditorField = typeof(MainWindow)
                .GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
            _commitEdit = typeof(MainWindow)
                .GetMethod("CommitEdit", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEdit");
            _commitEditAcrossSelection = typeof(MainWindow)
                .GetMethod("CommitEditAcrossSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CommitEditAcrossSelection");
            _insertNewSheet = typeof(MainWindow)
                .GetMethod("InsertNewSheet", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertNewSheet");
            _setActiveCell = typeof(MainWindow)
                .GetMethod("SetActiveCell", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "SetActiveCell");
            _showInlineEditor = typeof(MainWindow)
                .GetMethod("ShowInlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ShowInlineEditor");
            _executeClearSelection = typeof(MainWindow)
                .GetMethod("ExecuteClearSelection", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "ExecuteClearSelection");
            _formulaBarKeyDown = typeof(MainWindow)
                .GetMethod("FormulaBar_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBar_KeyDown");
            _cellAddressBoxKeyDown = typeof(MainWindow)
                .GetMethod("CellAddressBox_KeyDown", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "CellAddressBox_KeyDown");
            _insertFormulaFunction = typeof(MainWindow)
                .GetMethod("InsertFormulaFunction", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertFormulaFunction");
            _insertDefinedNameIntoFormula = typeof(MainWindow)
                .GetMethod("InsertDefinedNameIntoFormula", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "InsertDefinedNameIntoFormula");
            _formulaBarExpandButtonClick = typeof(MainWindow)
                .GetMethod("FormulaBarExpandBtn_Click", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "FormulaBarExpandBtn_Click");
            _editActiveCellInFormulaBar = typeof(MainWindow)
                .GetMethod("EditActiveCellInFormulaBar", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(nameof(MainWindow), "EditActiveCellInFormulaBar");
        }

        public string FormulaBarText => ((TextBox)_window.FindName("FormulaBar")).Text;

        public string CellAddressBoxText => ((TextBox)_window.FindName("CellAddressBox")).Text;

        public SheetId CurrentSheetId => (SheetId)_currentSheetIdField.GetValue(_window)!;

        public GridRange? SelectedRange => ((SheetGridView)_window.FindName("SheetGrid")).SelectedRange;

        public string? InlineEditorText => InlineEditor?.Text;

        public bool InlineEditorVisible => InlineEditor?.IsVisible == true;

        public bool FormulaBarFocused => IsFocused((TextBox)_window.FindName("FormulaBar"));

        public bool CellAddressBoxFocused => IsFocused((TextBox)_window.FindName("CellAddressBox"));

        public int CellAddressBoxSelectionLength => ((TextBox)_window.FindName("CellAddressBox")).SelectionLength;

        public bool SheetGridFocused => IsFocused((SheetGridView)_window.FindName("SheetGrid"));

        public bool FormulaBarAcceptsReturn => ((TextBox)_window.FindName("FormulaBar")).AcceptsReturn;

        public int FormulaBarCaretIndex => ((TextBox)_window.FindName("FormulaBar")).CaretIndex;

        public double FormulaBarHeight => ((TextBox)_window.FindName("FormulaBar")).Height;

        public string FormulaBarExpandButtonAutomationName =>
            System.Windows.Automation.AutomationProperties.GetName((Button)_window.FindName("FormulaBarExpandBtn"));

        public void SetCellText(uint row, uint col, string text)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromValue(new TextValue(text)));
        }

        public void SetCellFormula(uint row, uint col, string formulaText)
        {
            var sheet = Workbook.Sheets[0];
            sheet.SetCell(new CellAddress(sheet.Id, row, col), Cell.FromFormula(formulaText));
        }

        public string? CellText(uint row, uint col) => CellText(row, col, Workbook.Sheets[0].Id);

        public string? CellText(uint row, uint col, SheetId sheetId)
        {
            var sheet = Workbook.GetSheet(sheetId)
                ?? throw new InvalidOperationException($"Sheet {sheetId} not found.");
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value is TextValue text
                ? text.Value
                : null;
        }

        public string? CellFormula(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            return sheet.GetCell(new CellAddress(sheet.Id, row, col))?.FormulaText;
        }

        public GridRange NamedRange(string name)
        {
            Workbook.TryGetNamedRange(name, out var range).Should().BeTrue();
            return range;
        }

        public void DefineNamedRange(string name, GridRange range)
        {
            Workbook.DefineNamedRange(name, range);
        }

        public void SelectActiveCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _setActiveCell.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        public void SelectRange(uint startRow, uint startCol, uint endRow, uint endCol)
        {
            var sheet = Workbook.Sheets[0];
            var range = new GridRange(
                new CellAddress(sheet.Id, startRow, startCol),
                new CellAddress(sheet.Id, endRow, endCol));
            var grid = (SheetGridView)_window.FindName("SheetGrid");
            grid.SelectedRanges = null;
            grid.SelectedRange = range;
            PumpDispatcher();
        }

        public void SetFormulaEditCell(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _formulaEditCellField.SetValue(_window, new CellAddress(sheet.Id, row, col));
            PumpDispatcher();
        }

        public bool CommitEditAcrossSelection(bool fillFormulaEditCellOnly)
        {
            var committed = (bool)_commitEditAcrossSelection.Invoke(_window, [fillFormulaEditCellOnly])!;
            PumpDispatcher();
            return committed;
        }

        public bool CommitEdit()
        {
            var committed = (bool)_commitEdit.Invoke(_window, null)!;
            PumpDispatcher();
            return committed;
        }

        public void InsertNewSheet()
        {
            _insertNewSheet.Invoke(_window, null);
            PumpDispatcher();
        }

        public void ShowInlineEditor(uint row, uint col)
        {
            var sheet = Workbook.Sheets[0];
            _showInlineEditor.Invoke(_window, [new CellAddress(sheet.Id, row, col)]);
            PumpDispatcher();
        }

        public void InsertFormulaFunction(string functionName)
        {
            _insertFormulaFunction.Invoke(_window, [functionName]);
            PumpDispatcher();
        }

        public void InsertDefinedNameIntoFormula(string name)
        {
            _insertDefinedNameIntoFormula.Invoke(_window, [name]);
            PumpDispatcher();
        }

        public void ToggleFormulaBarExpansion()
        {
            var button = (Button)_window.FindName("FormulaBarExpandBtn");
            _formulaBarExpandButtonClick.Invoke(_window, [button, new RoutedEventArgs()]);
            PumpDispatcher();
        }

        public void EditActiveCellInFormulaBar()
        {
            _editActiveCellInFormulaBar.Invoke(_window, null);
            PumpDispatcher();
        }

        public void SetFormulaBarText(string text)
        {
            ((TextBox)_window.FindName("FormulaBar")).Text = text;
            PumpDispatcher();
        }

        public void SetFormulaBarCaretIndex(int caretIndex)
        {
            ((TextBox)_window.FindName("FormulaBar")).CaretIndex = caretIndex;
            PumpDispatcher();
        }

        public void SetCellAddressBoxText(string text)
        {
            ((TextBox)_window.FindName("CellAddressBox")).Text = text;
            PumpDispatcher();
        }

        public bool PressCellAddressBoxKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _cellAddressBoxKeyDown.Invoke(_window, [((TextBox)_window.FindName("CellAddressBox")), args]);
            PumpDispatcher();
            return args.Handled;
        }

        public bool PressFormulaBarKey(Key key)
        {
            var source = PresentationSource.FromVisual(_window)
                ?? throw new InvalidOperationException("MainWindow presentation source is not available.");
            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            };
            _formulaBarKeyDown.Invoke(_window, [((TextBox)_window.FindName("FormulaBar")), args]);
            PumpDispatcher();
            return args.Handled;
        }

        public void SetInlineEditorText(string text)
        {
            var inlineEditor = InlineEditor ?? throw new InvalidOperationException("Inline editor is not visible.");
            inlineEditor.Text = text;
            PumpDispatcher();
        }

        public void FocusFormulaBar()
        {
            var formulaBar = (TextBox)_window.FindName("FormulaBar");
            _window.Activate();
            FocusManager.SetFocusedElement(_window, formulaBar);
            formulaBar.Focus();
            Keyboard.Focus(formulaBar);
            PumpDispatcher();
        }

        public void ClearSelectedContents()
        {
            _executeClearSelection.Invoke(_window, null);
            PumpDispatcher();
        }

        public static MainWindowHarness Create()
        {
            var workbook = new Workbook("Book1");
            workbook.AddSheet("Sheet1");
            var workbookRef = new WorkbookRef { Current = workbook };
            var graph = new DependencyGraph();
            var evaluator = new FormulaEvaluator();
            var window = new MainWindow(
                NullLogger<MainWindow>.Instance,
                new ViewportService(),
                new CommandBus(_ => new TestCommandContext(workbookRef.Current)),
                new RecalcEngine(graph, evaluator),
                [],
                workbookRef,
                workbook,
                NullUserMessageService.Instance)
            {
                WindowState = WindowState.Normal,
                Width = 1280,
                Height = 720
            };

            window.Show();
            window.Activate();
            window.UpdateLayout();
            PumpDispatcher();
            return new MainWindowHarness(window);
        }

        private Workbook Workbook =>
            (Workbook)(_workbookField.GetValue(_window)
                ?? throw new InvalidOperationException("MainWindow workbook is not initialized."));

        private TextBox? InlineEditor => (TextBox?)_inlineEditorField.GetValue(_window);

        private bool IsFocused(IInputElement element) =>
            ReferenceEquals(Keyboard.FocusedElement, element) ||
            ReferenceEquals(FocusManager.GetFocusedElement(_window), element);

        public void Dispose()
        {
            MainWindowTestCleanup.CloseWithoutSavePrompt(_window);
            PumpDispatcher();
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new InvalidOperationException($"Sheet {sheetId} not found");
    }
}
