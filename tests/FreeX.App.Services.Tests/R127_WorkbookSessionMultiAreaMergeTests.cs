using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

// R127-services-multiarea-merge-1: WorkbookSession.MergeAndCenterSelectedRange and
// UnmergeSelectedRange (the Avalonia shell's Merge & Center / Unmerge Cells entry points, via
// MainWindow.cs's MergeAndCenterSelectedRangeAsync/UnmergeSelectedRange) used to build their
// command against only the single active SelectedRange, silently ignoring every other disjoint
// area of a Ctrl+click multi-area selection (SelectedRanges) -- unlike Excel, and unlike the WPF
// host's MainWindow.HomeFormatting.cs fix for the identical defect (R127-homeformatting-
// multiarea-merge-1). Fixed via the same GetCurrentSelectedRanges choke point the R127
// style/fill/clear-contents multi-area fixes in this class already use.
public sealed class R127_WorkbookSessionMultiAreaMergeTests
{
    [Fact]
    public void MergeAndCenterSelectedRange_MultiAreaSelection_MergesEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        var areaB = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)); // B1:C1
        var areaE = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)); // E1:F1 -- active

        // Ctrl+click B1:C1 then E1:F1 (disjoint): SelectedRange is the active/last-clicked area
        // (E1:F1), SelectedRanges holds both -- exactly what a real multi-area Ctrl+click leaves
        // behind (see R127_MultiAreaStyleAndClearContentsTests for the same shape).
        session.SelectRanges(areaE, [areaB, areaE]);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only E1:F1 (the active area) was merged; B1:C1 was silently left
        // untouched.
        sheet.MergedRegions.Should().Contain(areaB, "B1:C1's disjoint area must also be merged by Merge & Center");
        sheet.MergedRegions.Should().Contain(areaE, "E1:F1 (the active area) must be merged");
    }

    [Fact]
    public void UnmergeSelectedRange_MultiAreaSelection_UnmergesEveryDisjointArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        var areaB = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)); // B1:C1
        var areaE = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)); // E1:F1
        sheet.AddMergedRegion(areaB);
        sheet.AddMergedRegion(areaE);

        session.SelectRanges(areaE, [areaB, areaE]);

        var result = session.UnmergeSelectedRange();

        result.Success.Should().BeTrue(result.ErrorMessage);
        // Before the fix, only E1:F1 (the active area) was unmerged; B1:C1 silently stayed merged.
        sheet.MergedRegions.Should().NotContain(areaB, "B1:C1's disjoint area must also be unmerged");
        sheet.MergedRegions.Should().NotContain(areaE, "E1:F1 (the active area) must be unmerged");
    }

    // No-regression sibling: a plain single active-range Merge & Center (no Ctrl+click multi-area
    // selection) must keep merging exactly that one range, unaffected by routing the command
    // construction through the ranges-aware plumbing.
    [Fact]
    public void MergeAndCenterSelectedRange_SingleActiveRange_StillMergesOnlyThatRange_NoRegression()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var session = CreateSession(workbook);

        var areaB = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 3)); // B1:C1
        var areaE = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 1, 6)); // E1:F1 -- never selected

        session.SelectRange(areaB);
        session.SelectedRanges.Count.Should().BeLessThanOrEqualTo(1);

        var result = session.MergeAndCenterSelectedRange();

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.MergedRegions.Should().Contain(areaB);
        sheet.MergedRegions.Should().NotContain(areaE, "E1:F1 was never selected and must stay untouched");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
