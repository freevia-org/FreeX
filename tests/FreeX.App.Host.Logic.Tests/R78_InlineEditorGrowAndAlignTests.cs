using System.Reflection;
using FluentAssertions;
using FreeX.Core.Model;

using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R78-render-inplace-editor-5-3 / R78-render-inplace-editor-5-4
/// (<c>MainWindow.Editing.cs</c>'s <c>ShowInlineEditor</c>/<c>RefreshInlineEditorTextSurface</c>):
///
/// (1) the WPF in-cell editor never grew vertically for Alt+Enter-inserted line breaks -- the
/// editor's on-screen Height was set exactly once from the single cell's row height when the edit
/// session opened and never recomputed from the current line count, unlike its width (which
/// RefreshInlineEditorTextSurface already re-measured on every keystroke).
///
/// (2) the editor was always left-aligned regardless of the cell's own horizontal alignment,
/// unlike the Avalonia shell's CreateInlineCellEditor, which threads the resolved alignment in.
/// </summary>
public sealed class R78_InlineEditorGrowAndAlignTests
{
    [Fact]
    public void InsertLineBreak_ThenRefresh_GrowsEditorHeightByLineCount()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new TextValue("Line one"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                var singleLineHeight = inlineEditor!.Height;

                // Simulate what Alt+Enter drives (InlineEditor_KeyDown's InsertLineBreak(_inlineEditor!)
                // branch): insert a line break into the live editor TextBox. Setting .Text fires the
                // editor's own TextChanged handler synchronously, which is what must now recompute Height.
                InvokeInsertLineBreak(inlineEditor);

                inlineEditor.Text.Should().Contain(Environment.NewLine);
                inlineEditor.Height.Should().BeGreaterThan(
                    singleLineHeight,
                    "Alt+Enter inserts a second line, so the editor box must grow downward to show " +
                    "it instead of clipping line 2+ below the fixed single-row height " +
                    "(R78-render-inplace-editor-5-3)");
                inlineEditor.Height.Should().BeApproximately(
                    singleLineHeight * 2,
                    0.01,
                    "two display lines should grow the box by one whole row-height unit per line");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: typing more single-line text (no line break) must not grow the
    // editor's height, only its width (RefreshInlineEditorTextSurface's pre-existing behavior).
    [Fact]
    public void TypingMoreSingleLineText_KeepsSingleRowHeight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new TextValue("Line one"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                var singleLineHeight = inlineEditor!.Height;

                inlineEditor!.Text += " and quite a lot more text, but still only one line";

                inlineEditor.Height.Should().Be(
                    singleLineHeight,
                    "widening text on a single line must not grow the editor's height");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ShowInlineEditor_ForRightAlignedCell_UsesRightTextAlignment()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new TextValue("Some label"));
                sheet.GetCell(addr)!.StyleId = workbook.RegisterStyle(new CellStyle { HorizontalAlignment = CellHAlign.Right });

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.TextAlignment.Should().Be(
                    System.Windows.TextAlignment.Right,
                    "an explicitly right-aligned cell must edit with right-aligned text, matching " +
                    "the Avalonia shell (R78-render-inplace-editor-5-4)");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void ShowInlineEditor_ForGeneralAlignedNumericCell_UsesRightTextAlignment()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new NumberValue(1234));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.TextAlignment.Should().Be(
                    System.Windows.TextAlignment.Right,
                    "Excel general-aligns numeric content to the right, so editing it in place must " +
                    "keep it right-aligned instead of defaulting to left");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: plain text with the default "General" alignment must keep editing
    // left-aligned, matching Excel's own General-alignment rule for text content.
    [Fact]
    public void ShowInlineEditor_ForGeneralAlignedTextCell_UsesLeftTextAlignment()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new TextValue("Some label"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.TextAlignment.Should().Be(System.Windows.TextAlignment.Left);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static void InvokeInsertLineBreak(System.Windows.Controls.TextBox editor)
    {
        var method = typeof(MainWindow).GetMethod("InsertLineBreak", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(MainWindow), "InsertLineBreak");
        method.Invoke(null, [editor]);
    }

    private static System.Windows.Controls.TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (System.Windows.Controls.TextBox?)field.GetValue(window);
    }
}
