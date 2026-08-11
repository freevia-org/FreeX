using FluentAssertions;
using FreeX.App.Presentation.Filtering;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R78-commands-sort-multikey-5-1
/// (src/FreeX.App.Host/MainWindow.DataFilterCommands.cs, ApplyAutoFilterDialogResult).
///
/// Before the fix: the AutoFilter dropdown's Sort A-Z / Z-A / Sort-by-Color entries passed the
/// header-inclusive `range` (it always starts at the header row -- see
/// AutoFilterRangeResolver/AutoFilterDropdownMenuPlanner.HasActiveFilter) straight into SortCommand
/// with no header exclusion, unlike SortCustomButton_Click and the quick ribbon Sort Asc/Desc
/// buttons which both strip the header row first. So sorting from the dropdown pulled the header
/// text into the data set and promoted a data row to row 1.
///
/// After the fix, ApplyAutoFilterDialogResult excludes the range's first row
/// (ExcludeHeaderRowForAutoFilterSort) before building the SortCommand for all three dropdown sort
/// entries, so the header stays pinned at row 1 and only the data rows below it reorder.
/// </summary>
public sealed class R78_AutoFilterDropdownSortExcludesHeaderTests
{
    [Fact]
    public void ApplyAutoFilterDialogResult_SortAscending_HeaderStaysPutAndDataSorts()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Fruit"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("Cherry"));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("Apple"));
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("Banana"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1)); // A1:A4
                window.SheetGrid.SelectedRange = range;

                var ascending = new AutoFilterDialogResult(
                    AutoFilterSortDirection.Ascending,
                    SelectedValues: [],
                    SearchText: "",
                    CriteriaText: "");

                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, ascending, "Sort")!)
                    .Should().BeTrue();

                sheet.GetValue(1, 1).Should().Be(new TextValue("Fruit"), "the header row must stay pinned at row 1");
                sheet.GetValue(2, 1).Should().Be(new TextValue("Apple"));
                sheet.GetValue(3, 1).Should().Be(new TextValue("Banana"));
                sheet.GetValue(4, 1).Should().Be(new TextValue("Cherry"));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: Sort Z to A must also leave the header untouched, sorting only the
    // data rows (in descending order this time).
    [Fact]
    public void ApplyAutoFilterDialogResult_SortDescending_HeaderStaysPutAndDataSorts()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Fruit"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("Cherry"));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("Apple"));
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("Banana"));

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1)); // A1:A4
                window.SheetGrid.SelectedRange = range;

                var descending = new AutoFilterDialogResult(
                    AutoFilterSortDirection.Descending,
                    SelectedValues: [],
                    SearchText: "",
                    CriteriaText: "");

                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, descending, "Sort")!)
                    .Should().BeTrue();

                sheet.GetValue(1, 1).Should().Be(new TextValue("Fruit"), "the header row must stay pinned at row 1");
                sheet.GetValue(2, 1).Should().Be(new TextValue("Cherry"));
                sheet.GetValue(3, 1).Should().Be(new TextValue("Banana"));
                sheet.GetValue(4, 1).Should().Be(new TextValue("Apple"));
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }

    // Sibling no-regression: the new r76 "Sort by Color" dropdown entry must also leave the header
    // untouched -- only the colored data row should move to the top of the data range (row 2).
    [Fact]
    public void ApplyAutoFilterDialogResult_SortByColor_HeaderStaysPutAndColoredRowMovesToTopOfData()
    {
        StaTestRunner.Run(() =>
        {
            var (window, workbook) = R49MainWindowTestHarness.CreateWindow();
            try
            {
                var sheet = workbook.GetSheetAt(0);
                var sheetId = sheet.Id;

                sheet.SetCell(new CellAddress(sheetId, 1, 1), new TextValue("Fruit"));
                sheet.SetCell(new CellAddress(sheetId, 2, 1), new TextValue("Cherry"));
                sheet.SetCell(new CellAddress(sheetId, 3, 1), new TextValue("Apple"));
                sheet.SetCell(new CellAddress(sheetId, 4, 1), new TextValue("Banana"));

                var greenFill = new CellColor(0x21, 0x73, 0x46);
                var fillStyle = CellStyle.Default.Clone();
                fillStyle.FillColor = greenFill;
                sheet.GetCell(4, 1)!.StyleId = workbook.RegisterStyle(fillStyle); // "Banana" is green

                var range = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 4, 1)); // A1:A4
                window.SheetGrid.SelectedRange = range;

                var sortByColor = new AutoFilterDialogResult(
                    AutoFilterSortDirection.None,
                    SelectedValues: [],
                    SearchText: "",
                    CriteriaText: "",
                    SortByColorFilter: new AutoFilterColorFilter(AutoFilterColorFilterKind.CellFillColor, greenFill));

                ((bool)R49MainWindowTestHarness.Invoke(
                        window, "ApplyAutoFilterDialogResult", range, (uint)0, sortByColor, "Sort by Color")!)
                    .Should().BeTrue();

                sheet.GetValue(1, 1).Should().Be(new TextValue("Fruit"), "the header row must stay pinned at row 1");
                sheet.GetValue(2, 1).Should().Be(new TextValue("Banana"), "the green-filled data row moves to the top of the DATA range, not row 1");
            }
            finally
            {
                R49MainWindowTestHarness.Close(window);
            }
        });
    }
}
