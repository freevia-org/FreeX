using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowFormulaBarSyncTests
{
    // Issue 116: while writing a formula, clicking a cell inserts its coordinate but must keep the
    // edit focus/caret in the editor so the rest of the formula can be typed.
    // Issue 114: mouse drag while writing a formula must extend the inserted range reference.
    [Fact]
    public void FormulaRangeSelection_InlineEditorClick_InsertsReferenceAndKeepsFocus()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("=");      // enters point mode
            harness.SetInlineEditorText("=SUM(");  // point mode stays active mid-formula
            harness.SetInlineEditorCaretIndex("=SUM(".Length);

            // Single grid click during formula entry inserts the clicked cell's reference.
            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();

            harness.InlineEditorText.Should().Be("=SUM(A3");
            harness.InlineEditorVisible.Should().BeTrue();
            harness.InlineEditorFocused.Should().BeTrue("focus must stay in the editor so typing can continue");
            harness.CellFormula(1, 1).Should().BeNull("the reference insertion must not commit the formula");
        });
    }

    [Fact]
    public void FormulaRangeSelection_InlineEditorDrag_ExtendsRangeReference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("=");
            harness.SetInlineEditorText("=SUM(");
            harness.SetInlineEditorCaretIndex("=SUM(".Length);

            // Mouse-down anchors on A3, drag-move extends to B5 (extendSelection: true).
            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(5, 2, extend: true).Should().BeTrue();

            harness.InlineEditorText.Should().Be("=SUM(A3:B5");
            harness.InlineEditorFocused.Should().BeTrue();
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 3, 1),
                new CellAddress(harness.CurrentSheetId, 5, 2)));
        });
    }

    [Fact]
    public void FormulaRangeSelection_FormulaBarClick_InsertsReferenceAndKeepsFocus()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.SelectActiveCell(1, 1);
            harness.SetFormulaEditCell(1, 1);
            harness.FocusFormulaBar();
            harness.SetFormulaBarText("=");
            harness.SetFormulaBarText("=SUM(");
            harness.SetFormulaBarCaretIndex("=SUM(".Length);

            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(A3");
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().BeNull();
        });
    }
}
