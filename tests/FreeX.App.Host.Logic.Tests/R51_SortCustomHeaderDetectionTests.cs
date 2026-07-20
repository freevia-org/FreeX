using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R51-commands-sort-custom-multilevel-3-1
/// (src/FreeX.App.Host/MainWindow.DataFilterCommands.cs, DetectSortDialogHasHeaders /
/// SortCustomButton_Click).
///
/// Before the fix: the Custom Sort dialog always defaulted its "My data has headers" checkbox to
/// checked (SortDialog's `hasHeaders = true` default was never overridden by the caller), so a
/// pure headerless data selection had its first row silently excluded from the sort. Real Excel
/// auto-detects whether the selection has a header row using the same heuristic already used
/// elsewhere in this file (ExcludeHeaderRowForQuickSort) and by Quick Analysis.
///
/// After the fix, SortCustomButton_Click calls the new DetectSortDialogHasHeaders helper (which
/// reuses QuickAnalysisSelectionReader.Describe(...).HasHeaderRow) and passes the real result into
/// the dialog instead of a hardcoded `true`.
/// </summary>
public sealed class R51_SortCustomHeaderDetectionTests
{
    [Fact]
    public void DetectSortDialogHasHeaders_HeaderlessAllDataSelection_ReturnsFalse()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // A1:B5 with pure data, no header row: column A is numeric on every row (including
                // row 1), so the "first row is all text" heuristic correctly reports no header.
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new NumberValue(50));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Xray"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new NumberValue(10));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new TextValue("Apple"));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new NumberValue(30));
                sheet.SetCell(new CellAddress(sheetId, 3, 2), new TextValue("Mango"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));

                var hasHeaders = (bool)R49MainWindowTestHarness.Invoke(window, "DetectSortDialogHasHeaders", range)!;

                hasHeaders.Should().BeFalse(
                    "row 1 is ordinary numeric/text data (column A is numeric on row 1 too), not a header " +
                    "label row, so the dialog must not default to excluding it from the sort");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: a genuine "labels over values" selection is still detected as having
    // a header row, matching the pre-existing ExcludeHeaderRowForQuickSort heuristic behavior.
    [Fact]
    public void DetectSortDialogHasHeaders_GenuineHeaderRow_ReturnsTrue()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                // Row 1 is all-text labels; data rows below are numeric -- the classic header shape.
                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Name"));
                sheet.SetCell(new CellAddress(sheetId, 1, 2), new TextValue("Score"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("Alice"));
                sheet.SetCell(new CellAddress(sheetId, 2, 2), new NumberValue(90));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("Bob"));
                sheet.SetCell(new CellAddress(sheetId, 3, 2), new NumberValue(80));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2));

                var hasHeaders = (bool)R49MainWindowTestHarness.Invoke(window, "DetectSortDialogHasHeaders", range)!;

                hasHeaders.Should().BeTrue("row 1 is all-text labels over numeric data -- a genuine header row");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
