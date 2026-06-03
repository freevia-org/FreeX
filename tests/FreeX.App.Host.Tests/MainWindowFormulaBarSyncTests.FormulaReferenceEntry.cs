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
    public void FormulaBarDown_WithFormulaReferenceDraft_InsertsReferenceWithoutCommitting()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=A2");
            harness.FormulaBarCaretIndex.Should().Be("=A2".Length);
            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.FormulaBarFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarDown_WithInlineFormulaReferenceDraft_SyncsInlineEditorWithoutCommitting()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);
            harness.SetInlineEditorCaretIndex("=".Length);

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=A2");
            harness.InlineEditorText.Should().Be("=A2");
            harness.InlineEditorVisible.Should().BeTrue();
            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 1),
                new CellAddress(harness.CurrentSheetId, 2, 1)));
            harness.CellAddressBoxText.Should().Be("A2");
            harness.InlineEditorFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaReferenceEntry_AfterFormulaBarSelection_CommitsOnlyOriginalEditCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();
            harness.CommitEditAcrossSelection(fillFormulaEditCellOnly: true).Should().BeTrue();

            harness.CellFormula(1, 1).Should().Be("A2");
            harness.CellText(1, 1).Should().BeNull();
            harness.CellFormula(2, 1).Should().BeNull();
            harness.FormulaBarText.Should().Be("=A2");
        });
    }

    [Fact]
    public void FormulaBarEscape_AfterFormulaReferenceSelection_RestoresOriginalEditCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();
            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();

            harness.FormulaBarText.Should().Be("original");
            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.CellText(2, 1).Should().BeNull();
            harness.CellFormula(2, 1).Should().BeNull();
            harness.SheetGridFocused.Should().BeTrue();
        });
    }

    [Fact]
    public void FormulaBarDown_WithExistingFormulaEdit_PreservesDraftAndSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 1, "SUM(B1:C1)");
            harness.SelectActiveCell(1, 1);
            harness.EditActiveCellInFormulaBar();

            harness.PressFormulaBarKey(Key.Down).Should().BeFalse();

            harness.FormulaBarText.Should().Be("=SUM(B1:C1)");
            harness.FormulaBarCaretIndex.Should().Be("=SUM(B1:C1)".Length);
            harness.CellFormula(1, 1).Should().Be("SUM(B1:C1)");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, 1, 1)));
            harness.CellAddressBoxText.Should().Be("A1");
            harness.FormulaBarFocused.Should().BeTrue();
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
}
