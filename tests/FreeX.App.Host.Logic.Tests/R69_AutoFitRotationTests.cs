using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R69-commands-autofit-6-1 / R69-commands-autofit-6-2
/// (src/FreeX.App.Host/MainWindow.CellsCommands.cs's GetAutoFitCellText, and the shared
/// src/FreeX.App.Services/Ribbon/RowColumnSizingPlanner.cs + FreeX.Core.Commands/AutoFitSizingService.cs).
///
/// Before the fix: GetAutoFitCellText always built <c>new AutoFitCellText(text, style.WrapText)</c>
/// -- the 2-arg constructor, leaving TextRotation at 0 regardless of the cell's actual style, so
/// AutoFit Row Height never reached AutoFitSizingService's rotation-aware branch for a stacked
/// (Orientation 255) or angled cell. Column Width had the same gap one layer down:
/// RowColumnSizingPlanner.CollectColumnTexts collapsed each AutoFitCellText to a bare
/// <c>List&lt;string&gt;</c>, dropping TextRotation before it ever reached EstimateColumnWidth.
///
/// After the fix, GetAutoFitCellText passes the style's TextRotation through, CollectColumnTexts
/// carries the full AutoFitCellText, and EstimateColumnWidth has a rotation-aware overload that
/// narrows stacked/angled text instead of measuring its unrotated string length.
/// </summary>
public sealed class R69_AutoFitRotationTests
{
    [Fact]
    public void FormatAutoRowMenuItem_Click_StackedVerticalText_GrowsRowFromRotation()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                var address = new CellAddress(sheetId, 1, 1);
                sheet.SetCell(address, new TextValue(new string('A', 20)));
                sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = 255 });

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

                R49MainWindowTestHarness.Invoke(window, "FormatAutoRowMenuItem_Click", null, null);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().BeGreaterThan(sheet.DefaultRowHeight * 2,
                    "stacked/vertical text (TextRotation 255) needs one line-height per character, " +
                    "so AutoFit Row Height must grow the row far beyond the unrotated single-line height");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoRowMenuItem_Click_HorizontalText_StaysAtDefaultHeight()
    {
        // Sibling no-regression: a normal (unrotated) single-line cell must still autofit to the
        // default row height, exactly as before the TextRotation wiring fix.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultRowHeight = 20;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue(new string('A', 20)));

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

                R49MainWindowTestHarness.Invoke(window, "FormatAutoRowMenuItem_Click", null, null);

                sheet.RowHeights.Should().ContainKey(1u);
                sheet.RowHeights[1].Should().Be(sheet.DefaultRowHeight);
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoColMenuItem_Click_StackedVerticalText_NarrowsInsteadOfWideningToFullTextLength()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultColumnWidth = 3.0;
                var address = new CellAddress(sheetId, 1, 1);
                sheet.SetCell(address, new TextValue("PRODUCT CATEGORY"));
                sheet.GetCell(address)!.StyleId = workbook.RegisterStyle(new CellStyle { TextRotation = 255 });

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

                R49MainWindowTestHarness.Invoke(window, "FormatAutoColMenuItem_Click", null, null);

                sheet.ColumnWidths.Should().ContainKey(1u);
                sheet.ColumnWidths[1].Should().BeApproximately(3.0, 0.01,
                    "stacked text only needs ~1 glyph's width per column, not the 16-character string length");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    [Fact]
    public void FormatAutoColMenuItem_Click_HorizontalText_StillWidensToFullTextLength()
    {
        // Sibling no-regression: an unrotated cell with the same text must still widen the column
        // to its full character length, exactly as before the rotation-aware estimate was added.
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;
                sheet.DefaultColumnWidth = 3.0;
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("PRODUCT CATEGORY"));

                window.SheetGrid.SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

                R49MainWindowTestHarness.Invoke(window, "FormatAutoColMenuItem_Click", null, null);

                sheet.ColumnWidths.Should().ContainKey(1u);
                sheet.ColumnWidths[1].Should().BeApproximately(18.0, 0.01); // 16 chars + 2.0 padding
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
