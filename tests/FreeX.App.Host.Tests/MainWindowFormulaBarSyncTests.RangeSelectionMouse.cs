using System.Windows.Input;
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
    public void FormulaRangeSelection_ReverseExtension_PreservesDirectionalAnchor()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "formulaBar");
            harness.ApplyFormulaRangeSelection(2, 2, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(1, 1, extend: true).Should().BeTrue();

            harness.FormulaBarText.Should().Be("=SUM(A1:B2");
            harness.SelectionAnchor.Should().Be(new CellAddress(harness.CurrentSheetId, 2, 2));
            harness.SelectionCursor.Should().Be(new CellAddress(harness.CurrentSheetId, 1, 1));
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

    [Fact]
    public void FormulaBarShiftF8_AddMode_AppendsKeyboardCreatedAreas()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "formulaBar");
            harness.ToggleFormulaRangeEntrySelectionMode(ModifierKeys.Shift);

            harness.PressFormulaBarKey(Key.Right).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM(B1");
            harness.PressFormulaBarKey(Key.Down).Should().BeTrue();
            harness.FormulaBarText.Should().Be("=SUM(B1,B2");
        });
    }

    [Theory]
    [InlineData("inline", "row", 4u, 0u, "=SUM(4:4", 4u, 1u, 4u, CellAddress.MaxCol)]
    [InlineData("formulaBar", "column", 0u, 3u, "=SUM(C:C", 1u, 3u, CellAddress.MaxRow, 3u)]
    [InlineData("inline", "grid", 0u, 0u, "=SUM(A1:XFD1048576", 1u, 1u, CellAddress.MaxRow, CellAddress.MaxCol)]
    public void Issue130FormulaRangeSelection_WholeHeaderOrGridSelection_InsertsReferenceAndKeepsEditing(
        string editor,
        string selectionKind,
        uint row,
        uint col,
        string expectedText,
        uint expectedStartRow,
        uint expectedStartCol,
        uint expectedEndRow,
        uint expectedEndCol)
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, editor);

            switch (selectionKind)
            {
                case "row":
                    harness.SelectWholeRow(row);
                    break;
                case "column":
                    harness.SelectWholeColumn(col);
                    break;
                case "grid":
                    harness.SelectWholeGrid();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(selectionKind), selectionKind, "Unknown selection kind.");
            }

            AssertFormulaEditTextAndFocus(harness, editor, expectedText);
            AssertFormulaEditCaret(harness, editor, expectedText.Length);
            harness.CellFormula(1, 1).Should().BeNull("whole-range selection must not commit formula editing");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, expectedStartRow, expectedStartCol),
                new CellAddress(harness.CurrentSheetId, expectedEndRow, expectedEndCol)));
        });
    }

    [Fact]
    public void FormulaRangeSelection_SelectAllCorner_InsertsWholeGridReferenceAndKeepsEditing()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "formulaBar");
            harness.SelectWholeGrid();

            harness.FormulaBarText.Should().Be("=SUM(A1:XFD1048576");
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().BeNull("whole-grid selection must not commit formula editing");
            harness.SelectedRange.Should().Be(new GridRange(
                new CellAddress(harness.CurrentSheetId, 1, 1),
                new CellAddress(harness.CurrentSheetId, CellAddress.MaxRow, CellAddress.MaxCol)));
        });
    }

    [Fact]
    public void Issue131FormulaRangeSelection_InlineEditorSeparatorThenSecondRange_InsertsNextArgument()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "inline");
            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(5, 1, extend: true).Should().BeTrue();

            harness.SetInlineEditorText("=SUM(A3:A5,");
            harness.SetInlineEditorCaretIndex("=SUM(A3:A5,".Length);

            harness.ApplyFormulaRangeSelection(2, 2, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(4, 2, extend: true).Should().BeTrue();

            const string expected = "=SUM(A3:A5,B2:B4";
            harness.InlineEditorText.Should().Be(expected);
            harness.FormulaBarText.Should().Be(expected);
            harness.InlineEditorCaretIndex.Should().Be(expected.Length);
            harness.InlineEditorFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().BeNull();
        });
    }

    [Fact]
    public void Issue131FormulaRangeSelection_FormulaBarOperatorThenSecondRange_InsertsNextOperand()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "formulaBar", "=");
            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();

            harness.SetFormulaBarText("=A3+");
            harness.SetFormulaBarCaretIndex("=A3+".Length);

            harness.ApplyFormulaRangeSelection(2, 2, extend: false).Should().BeTrue();

            const string expected = "=A3+B2";
            harness.FormulaBarText.Should().Be(expected);
            harness.FormulaBarCaretIndex.Should().Be(expected.Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().BeNull();
        });
    }

    [Fact]
    public void Issue131FormulaRangeSelection_DifferentRangeWhileReferenceActive_ReplacesLiveReference()
    {
        StaTestRunner.Run(() =>
        {
            using var harness = MainWindowHarness.Create();

            BeginFormulaEdit(harness, "formulaBar");
            harness.ApplyFormulaRangeSelection(3, 1, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(5, 1, extend: true).Should().BeTrue();

            harness.ApplyFormulaRangeSelection(2, 3, extend: false).Should().BeTrue();
            harness.ApplyFormulaRangeSelection(4, 4, extend: true).Should().BeTrue();

            const string expected = "=SUM(C2:D4";
            harness.FormulaBarText.Should().Be(expected);
            harness.FormulaBarCaretIndex.Should().Be(expected.Length);
            harness.FormulaBarFocused.Should().BeTrue();
            harness.CellFormula(1, 1).Should().BeNull();
        });
    }

    private static void BeginFormulaEdit(MainWindowHarness harness, string editor, string text = "=SUM(")
    {
        harness.SelectActiveCell(1, 1);
        if (editor == "inline")
        {
            harness.ShowInlineEditor(1, 1);
            harness.SetInlineEditorText("=");
            harness.SetInlineEditorText(text);
            harness.SetInlineEditorCaretIndex(text.Length);
            return;
        }

        harness.SetFormulaEditCell(1, 1);
        harness.FocusFormulaBar();
        harness.SetFormulaBarText("=");
        harness.SetFormulaBarText(text);
        harness.SetFormulaBarCaretIndex(text.Length);
    }

    private static void AssertFormulaEditTextAndFocus(MainWindowHarness harness, string editor, string expectedText)
    {
        if (editor == "inline")
        {
            harness.InlineEditorText.Should().Be(expectedText);
            harness.FormulaBarText.Should().Be(expectedText);
            harness.InlineEditorVisible.Should().BeTrue();
            harness.InlineEditorFocused.Should().BeTrue();
            return;
        }

        harness.FormulaBarText.Should().Be(expectedText);
        harness.FormulaBarFocused.Should().BeTrue();
    }

    private static void AssertFormulaEditCaret(MainWindowHarness harness, string editor, int expectedCaretIndex)
    {
        if (editor == "inline")
        {
            harness.InlineEditorCaretIndex.Should().Be(expectedCaretIndex);
            return;
        }

        harness.FormulaBarCaretIndex.Should().Be(expectedCaretIndex);
    }
}
