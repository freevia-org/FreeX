using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R88-render-rtl-bidi-5-3 (MainWindow.Editing.cs's
/// <c>ShowInlineEditor</c>/<c>EditActiveCellInFormulaBar</c>): the in-cell editor and Formula Bar
/// set <c>TextAlignment</c> for an RTL-reading-order cell but never <c>FlowDirection</c>, so bidi
/// reordering/caret behavior while editing stayed LTR-based even though the text was right-aligned.
/// </summary>
public sealed class R88_InlineEditorRtlFlowDirectionTests
{
    [Fact]
    public void ShowInlineEditor_ForRtlSheetCell_SetsRightToLeftFlowDirectionOnEditorAndFormulaBar()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.IsRightToLeft = true;
                var addr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(addr, new TextValue("Some label"));

                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", addr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", addr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor.Should().NotBeNull();
                inlineEditor!.FlowDirection.Should().Be(
                    FlowDirection.RightToLeft,
                    "an RTL-reading-order cell must edit with a true right-to-left paragraph direction, " +
                    "not just right-aligned text (R88-render-rtl-bidi-5-3)");

                var formulaBar = (TextBox)window.FindName("FormulaBar")!;
                formulaBar.FlowDirection.Should().Be(
                    FlowDirection.RightToLeft,
                    "the Formula Bar edits the same cell and must match the in-cell editor's direction");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: editing an ordinary LTR cell right after an RTL one must reset both the
    // reused in-cell editor and the Formula Bar back to left-to-right, not leave the previous RTL
    // direction stuck.
    [Fact]
    public void ShowInlineEditor_AfterRtlCell_ThenLtrCell_ResetsFlowDirectionToLeftToRight()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                sheet.IsRightToLeft = true;
                var rtlAddr = new CellAddress(sheet.Id, 1, 1);
                sheet.SetCell(rtlAddr, new TextValue("RTL"));
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", rtlAddr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", rtlAddr, (double?)null);
                GetInlineEditor(window)!.FlowDirection.Should().Be(FlowDirection.RightToLeft);

                sheet.IsRightToLeft = false;
                var ltrAddr = new CellAddress(sheet.Id, 2, 1);
                sheet.SetCell(ltrAddr, new TextValue("LTR"));
                R49MainWindowTestHarness.Invoke(window, "SetActiveCell", ltrAddr);
                R49MainWindowTestHarness.Invoke(window, "ShowInlineEditor", ltrAddr, (double?)null);

                var inlineEditor = GetInlineEditor(window);
                inlineEditor!.FlowDirection.Should().Be(
                    FlowDirection.LeftToRight,
                    "switching to an LTR cell must reset the reused in-cell editor's flow direction, " +
                    "not leave it stuck right-to-left");

                var formulaBar = (TextBox)window.FindName("FormulaBar")!;
                formulaBar.FlowDirection.Should().Be(FlowDirection.LeftToRight);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    private static TextBox? GetInlineEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_inlineEditor", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(MainWindow), "_inlineEditor");
        return (TextBox?)field.GetValue(window);
    }
}
