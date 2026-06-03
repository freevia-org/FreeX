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
    public void FormulaBarEnter_WhileInlineEditorVisible_CommitsDraftAndHidesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("draft edit");
            harness.FocusFormulaBar();

            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("draft edit");
            harness.InlineEditorVisible.Should().BeFalse();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarEnter_WhileInlineEditorVisible_CommitsFormulaBarDraftAndHidesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("formula bar draft");

            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("formula bar draft");
            harness.InlineEditorVisible.Should().BeFalse();
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
    public void FormulaBarDown_WithPlainDraft_CommitsEditMovesSelectionAndRefreshesEditors()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("down from formula bar");

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("down from formula bar");
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
    public void FormulaBarTab_WhileInlineEditorVisible_CommitsDraftAndHidesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("tab draft");
            harness.FocusFormulaBar();

            harness.PressFormulaBarKey(Key.Tab).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("tab draft");
            harness.InlineEditorVisible.Should().BeFalse();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));
            harness.CellAddressBoxText.Should().Be("B1");
            harness.FormulaBarText.Should().BeEmpty();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarTab_WhileInlineEditorVisible_CommitsFormulaBarDraftAndHidesInlineEditor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("formula bar tab draft");

            harness.PressFormulaBarKey(Key.Tab).Should().BeTrue();

            harness.CellText(1, 1).Should().Be("formula bar tab draft");
            harness.InlineEditorVisible.Should().BeFalse();
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
}
