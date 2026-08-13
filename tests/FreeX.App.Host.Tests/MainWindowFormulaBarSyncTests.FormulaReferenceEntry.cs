using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.IO;
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
    public void FormulaBarTypedFormula_UsesEnterModeUntilReferenceSelectionStarts()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 3);
            harness.SetFormulaEditCell(1, 3);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EnterMode"));

            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=C2");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
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
    public void InlineEditorTypedFormula_UsesEnterModeUntilReferenceSelectionStarts()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 3);
            harness.ShowInlineEditor(1, 3);
            harness.SetInlineEditorText("=");
            harness.SetInlineEditorCaretIndex("=".Length);

            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EnterMode"));

            harness.PressInlineEditorKey(Key.Down).Should().BeTrue();

            harness.InlineEditorText.Should().Be("=C2");
            harness.FormulaBarText.Should().Be("=C2");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
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
    public void FormulaBarEnter_AfterReferenceSelection_MovesFromEditedCellNotReferenceCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 3);
            harness.SetFormulaEditCell(1, 3);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex("=".Length);

            harness.ApplyFormulaRangeSelection(1, 1, extend: false).Should().BeTrue();
            harness.PressFormulaBarKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 3).Should().Be("A1");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 3),
                new CellAddress(harness.CurrentSheetId, 2, 3)));
            harness.CellAddressBoxText.Should().Be("C2");
        });
    }

    [Fact]
    public void InlineEditorEnter_AfterReferenceSelection_MovesFromEditedCellNotReferenceCell()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 3);
            harness.ShowInlineEditor(1, 3);
            harness.SetInlineEditorText("=");
            harness.SetInlineEditorCaretIndex("=".Length);

            harness.ApplyFormulaRangeSelection(1, 1, extend: false).Should().BeTrue();
            harness.PressInlineEditorKey(Key.Enter).Should().BeTrue();

            harness.CellFormula(1, 3).Should().Be("A1");
            harness.InlineEditorVisible.Should().BeFalse();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 2, 3),
                new CellAddress(harness.CurrentSheetId, 2, 3)));
            harness.CellAddressBoxText.Should().Be("C2");
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
            harness.FormulaRangeEntryMode.Should().BeFalse();
            harness.FormulaEditCell.Should().BeNull();
        });
    }

    [Fact]
    public void FormulaBarPointMode_ShiftSheetTabSelection_EmitsThreeDSheetSpanAndCancelsToSource()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            var sourceSheet = harness.CurrentSheetId;
            var startSheet = harness.AddSheet("Sheet2");
            var endSheet = harness.AddSheet("Sheet3");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=SUM(");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);
            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();
            harness.FormulaRangeEntryMode.Should().BeTrue();

            harness.SelectFormulaSheetTab(startSheet.Id, ModifierKeys.None);
            harness.SelectFormulaSheetTab(endSheet.Id, ModifierKeys.Shift);

            harness.ApplyFormulaRangeSelection(endSheet.Id, 2, 2, extend: false).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM(Sheet2:Sheet3!B2");
            harness.CurrentSheetId.Should().Be(endSheet.Id);

            harness.PressFormulaBarKey(Key.Escape).Should().BeTrue();
            harness.CurrentSheetId.Should().Be(sourceSheet);
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(sourceSheet, 1, 1),
                new CellAddress(sourceSheet, 1, 1)));
        });
    }

    [Fact]
    public void FormulaBarPointMode_AfterTypingComma_DropsAbandonedSheetSpan()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            var sourceSheet = harness.CurrentSheetId;
            var startSheet = harness.AddSheet("Sheet2");
            var endSheet = harness.AddSheet("Sheet3");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=SUM(");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);
            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();

            harness.SelectFormulaSheetTab(startSheet.Id, ModifierKeys.None);
            harness.SelectFormulaSheetTab(endSheet.Id, ModifierKeys.Shift);
            harness.ApplyFormulaRangeSelection(endSheet.Id, 2, 2, extend: false).Should().BeTrue();

            harness.SetFormulaBarText("=SUM(Sheet2:Sheet3!B2,");
            harness.SetFormulaBarCaretIndex("=SUM(Sheet2:Sheet3!B2,".Length);
            harness.ApplyFormulaRangeSelection(endSheet.Id, 3, 3, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(Sheet2:Sheet3!B2,Sheet3!C3");
            harness.CurrentSheetId.Should().Be(endSheet.Id);
            sourceSheet.Should().NotBe(endSheet.Id);
        });
    }

    [Fact]
    public void FormulaBarPointMode_ExtendingLiveReference_PreservesThreeDSheetSpan()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            var startSheet = harness.AddSheet("Sheet2");
            var endSheet = harness.AddSheet("Sheet3");
            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=SUM(");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);
            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();

            harness.SelectFormulaSheetTab(startSheet.Id, ModifierKeys.None);
            harness.SelectFormulaSheetTab(endSheet.Id, ModifierKeys.Shift);
            harness.ApplyFormulaRangeSelection(endSheet.Id, 2, 2, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(endSheet.Id, 4, 3, extend: true).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(Sheet2:Sheet3!B2:C4");
        });
    }

    [Fact]
    public void InlineEditorEscape_AfterFormulaReferenceSelection_CancelsEditAndLeavesReadyMode()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellText(1, 1, "original");
            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("=");
            harness.SetInlineEditorCaretIndex("=".Length);

            harness.PressInlineEditorKey(Key.Down).Should().BeTrue();
            harness.PressInlineEditorKey(Key.Escape).Should().BeTrue();

            harness.InlineEditorVisible.Should().BeFalse();
            harness.FormulaBarText.Should().Be("original");
            harness.CellText(1, 1).Should().Be("original");
            harness.CellFormula(1, 1).Should().BeNull();
            harness.CellText(2, 1).Should().BeNull();
            harness.CellFormula(2, 1).Should().BeNull();
            harness.SheetGridFocused.Should().BeTrue();
            harness.StatusReadyText.Should().Be(UiText.Get("MainWindow_Text_Ready"));
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
    public void FormulaBarPointMode_SelectedReferenceText_ReplacesSelectedReference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            const string formulaText = "=SUM(A1:A2)";
            var referenceStart = formulaText.IndexOf("A1:A2", StringComparison.Ordinal);

            harness.SetCellFormula(1, 3, "SUM(A1:A2)");
            harness.SelectActiveCell(1, 3);
            harness.EditActiveCellInFormulaBar();
            harness.SetFormulaBarSelection(referenceStart, "A1:A2".Length);

            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(1, 2, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(B1)");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));
        });
    }

    [Fact]
    public void InlineEditorPointMode_SelectedReferenceText_ReplacesSelectedReference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            const string formulaText = "=SUM(A1:A2)";
            var referenceStart = formulaText.IndexOf("A1:A2", StringComparison.Ordinal);

            harness.SetCellFormula(1, 3, "SUM(A1:A2)");
            harness.SelectActiveCell(1, 3);
            harness.ShowInlineEditor(1, 3);
            harness.SetInlineEditorSelection(referenceStart, "A1:A2".Length);

            harness.PressInlineEditorKey(Key.F2).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(1, 2, extend: false).Should().BeTrue();

            harness.InlineEditorText.Should().Be("=SUM(B1)");
            harness.FormulaBarText.Should().Be("=SUM(B1)");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));
        });
    }

    [Fact]
    public void FormulaBarPointMode_AfterTypedArgumentSeparator_InsertsReferenceAtCaret()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            const string formulaText = "=SUM(A1:A2,";

            harness.SelectActiveCell(1, 3);
            harness.SetFormulaEditCell(1, 3);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText(formulaText);
            harness.SetFormulaBarCaretIndex(formulaText.Length);

            harness.PressFormulaBarKey(Key.F2).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(1, 2, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(A1:A2,B1");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
        });
    }

    [Fact]
    public void FormulaBarPointMode_CrossSheetWholeRowAppend_PreservesSheetQualifier()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            var targetSheet = harness.AddSheet("Revenue Data");

            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarCaretIndex(1);
            harness.ApplyFormulaRangeSelection(2, 2, extend: false).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=B2");

            harness.SetCurrentSheetForFormulaPoint(targetSheet.Id);
            harness.AddWholeRowFormulaReference(3);

            harness.FormulaBarText.Should().Be("=B2,'Revenue Data'!3:3");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(targetSheet.Id, 3, 1),
                new CellAddress(targetSheet.Id, 3, CellAddress.MaxCol)));
        });
    }

    [Fact]
    public void FormulaBarPointMode_HeaderClicks_InsertWholeColumnAndWholeRowReferencesAndRoundTrip()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetCellNumber(2, 2, 10); // B2
            harness.SetCellNumber(3, 2, 20); // B3
            harness.SetCellNumber(3, 3, 30); // C3

            harness.SetFormulaEditCell(10, 7);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarText("=SUM()");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);
            harness.SelectWholeColumn(2);

            harness.FormulaBarText.Should().Be("=SUM(B:B)");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, CellAddress.MaxRow, 2)));
            harness.CommitEdit().Should().BeTrue();
            harness.CellFormula(10, 7).Should().Be("SUM(B:B)");
            harness.CellValue(10, 7).Should().Be(new NumberValue(30));

            harness.SetFormulaEditCell(11, 7);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarText("=SUM()");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);
            harness.SelectWholeRow(3);

            harness.FormulaBarText.Should().Be("=SUM(3:3)");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 3, 1),
                new CellAddress(harness.CurrentSheetId, 3, CellAddress.MaxCol)));
            harness.CommitEdit().Should().BeTrue();
            harness.CellFormula(11, 7).Should().Be("SUM(3:3)");
            harness.CellValue(11, 7).Should().Be(new NumberValue(50));

            using var stream = new MemoryStream();
            new NativeJsonAdapter().Save(harness.ActiveWorkbook, stream);
            stream.Position = 0;
            var reopened = new NativeJsonAdapter().Load(stream);
            var reopenedSheet = reopened.Sheets.Single(sheet => sheet.Name == harness.FirstSheet.Name);
            reopenedSheet.GetCell(new CellAddress(reopenedSheet.Id, 10, 7))!.FormulaText.Should().Be("SUM(B:B)");
            reopenedSheet.GetCell(new CellAddress(reopenedSheet.Id, 11, 7))!.FormulaText.Should().Be("SUM(3:3)");
        });
    }

    [Fact]
    public void FormulaBarF2_TogglesFormulaBetweenCaretEditingAndPointSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 3, "SUM(A1:A2)");
            harness.SelectActiveCell(1, 3);
            harness.EditActiveCellInFormulaBar();
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EditMode"));

            harness.PressFormulaBarKey(Key.Left).Should().BeFalse("Edit mode should leave caret movement to the TextBox");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 3),
                new CellAddress(harness.CurrentSheetId, 1, 3)));

            harness.PressFormulaBarKey(Key.F2).Should().BeTrue("F2 toggles formula editing into Point mode");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.PressFormulaBarKey(Key.Left).Should().BeTrue("Point mode should turn arrows into reference selection");

            harness.FormulaBarText.Should().EndWith("B1");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));

            var textAfterPointSelection = harness.FormulaBarText;
            harness.PressFormulaBarKey(Key.F2).Should().BeTrue("F2 toggles Point mode back off");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EditMode"));
            harness.PressFormulaBarKey(Key.Right).Should().BeFalse("Edit mode should again leave caret movement to the TextBox");
            harness.FormulaBarText.Should().Be(textAfterPointSelection);
        });
    }

    [Fact]
    public void InlineEditorF2_TogglesFormulaBetweenCaretEditingAndPointSelection()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SetCellFormula(1, 3, "SUM(A1:A2)");
            harness.SelectActiveCell(1, 3);
            harness.ShowInlineEditor(1, 3);
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EditMode"));

            harness.PressInlineEditorKey(Key.Left).Should().BeFalse("Edit mode should leave caret movement to the TextBox");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 3),
                new CellAddress(harness.CurrentSheetId, 1, 3)));

            harness.PressInlineEditorKey(Key.F2).Should().BeTrue("F2 toggles formula editing into Point mode");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.PressInlineEditorKey(Key.Left).Should().BeTrue("Point mode should turn arrows into reference selection");

            harness.InlineEditorText.Should().EndWith("B1");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_PointMode"));
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 2),
                new CellAddress(harness.CurrentSheetId, 1, 2)));

            var textAfterPointSelection = harness.InlineEditorText;
            harness.PressInlineEditorKey(Key.F2).Should().BeTrue("F2 toggles Point mode back off");
            harness.StatusReadyText.Should().Be(UiText.Get("StatusBar_EditMode"));
            harness.PressInlineEditorKey(Key.Right).Should().BeFalse("Edit mode should again leave caret movement to the TextBox");
            harness.InlineEditorText.Should().Be(textAfterPointSelection);
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
