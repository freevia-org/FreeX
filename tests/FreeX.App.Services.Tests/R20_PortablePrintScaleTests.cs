using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R20-print-area-page-setup-2: <see cref="SheetPdfPageSetupResolver.ResolveCapacity"/> (the
/// Avalonia/portable-PDF page-capacity path used by Linux/macOS print and PDF export) must mirror
/// the WPF <c>PagePaginationPlanner</c>'s R18 uniform-scale-to-free-axis fix. When a sheet's
/// scale-to-fit constrains only ONE axis (e.g. "Fit to 1 page(s) wide by [Automatic] tall"), Excel
/// (and FreeX's WPF path) derive a single uniform shrink ratio from the constrained axis and apply
/// that SAME ratio to the other, unconstrained axis too -- so a sheet that naturally needs 3
/// column-pages x 3 row-pages at 100% collapses to 1x1 total pages, not 1x3.
///
/// Pre-fix, <c>ResolveCapacity</c>'s fit-to-pages branch touched each axis completely independently:
/// only <c>baseColsPerPage</c> was shrunk when <c>FitToPagesWide</c> was set, leaving
/// <c>baseRowsPerPage</c> at its unscaled natural capacity when <c>FitToPagesTall</c> was null. That
/// produced a wrong page count on Linux/macOS (3 total pages) vs. Excel/Windows (1 page).
/// </summary>
public sealed class R20_print_avalonia_portable_Tests
{
    [Fact]
    public void ResolveCapacity_FitToPagesWideOnly_UniformlyShrinksFreeRowAxisToo()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit  = WorksheetScaleToFit.Default; // 100%, no fit-to-pages constraint yet.

        // First discover the sheet's natural (unscaled) per-page capacity using a large probe range
        // (large enough that neither axis is starved by the range itself).
        var probeRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 60));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);

        natural.RowsPerPage.Should().BeGreaterThan(0u);
        natural.ColumnsPerPage.Should().BeGreaterThan(0u);

        // Build a print range whose body needs EXACTLY 3 column-pages and 3 row-pages at the
        // sheet's natural (100%) per-page capacity.
        var bodyCols = natural.ColumnsPerPage * 3;
        var bodyRows = natural.RowsPerPage * 3;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, bodyRows, bodyCols));

        // "Fit to 1 page(s) wide by [Automatic] tall" -- only the column axis is constrained;
        // FitToPagesTall is null (the common Excel default for this setting).
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: null);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        // The constrained column axis collapses onto exactly 1 page (all body columns fit).
        capacity.ColumnsPerPage.Should().BeGreaterThanOrEqualTo(bodyCols,
            "FitToPagesWide=1 must fit every body column onto a single column-page");

        // R20-print-area-page-setup-2: the SAME uniform shrink ratio (1/3, derived from the
        // constrained column axis: natural columns / 1-page columns) must also apply to the free
        // row axis, so all body rows also collapse onto a single row-page -- matching Excel/WPF's
        // 1x1 total page count. Pre-fix, RowsPerPage stayed at `natural.RowsPerPage` (unscaled),
        // which is only 1/3 of bodyRows and would still require 3 row-pages. A 1-row tolerance
        // absorbs floating-point rounding in the uniform-scale-fraction math; the bug this guards
        // against leaves RowsPerPage at roughly 1/3 of bodyRows, far outside that tolerance.
        capacity.RowsPerPage.Should().BeGreaterThanOrEqualTo(bodyRows - 1,
            "the uniform scale derived from the constrained wide axis must shrink the free row axis " +
            "enough that all body rows also fit on a single page, mirroring PagePaginationPlanner's " +
            "R18 uniform-scale-to-free-axis fix and Excel's own Fit-to behavior");
    }

    [Fact]
    public void ResolveCapacity_FitToPagesTallOnly_UniformlyShrinksFreeColumnAxisToo()
    {
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit  = WorksheetScaleToFit.Default;

        var probeRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 60));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);

        var bodyCols = natural.ColumnsPerPage * 3;
        var bodyRows = natural.RowsPerPage * 3;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, bodyRows, bodyCols));

        // "Fit to [Automatic] page(s) wide by 1 tall" -- only the row axis is constrained.
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: null, FitToPagesTall: 1);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        capacity.RowsPerPage.Should().BeGreaterThanOrEqualTo(bodyRows,
            "FitToPagesTall=1 must fit every body row onto a single row-page");

        capacity.ColumnsPerPage.Should().BeGreaterThanOrEqualTo(bodyCols - 1,
            "the uniform scale derived from the constrained tall axis must shrink the free column " +
            "axis enough that all body columns also fit on a single page, mirroring " +
            "PagePaginationPlanner's R18 uniform-scale-to-free-axis fix");
    }

    [Fact]
    public void ResolveCapacity_BothAxesConstrained_AppliesUniformMinScaleToBothAxes()
    {
        // R100-services-print-scale-uniform-both-axes: when BOTH FitToPagesWide and FitToPagesTall
        // are explicitly set, Excel derives ONE uniform scale -- min(widthScale, heightScale) -- and
        // applies it to BOTH axes (matching PagePaginationPlanner's dedicated
        // "wideConstrained && tallConstrained" branch, R20-print-area-page-setup-3). Resolving each
        // axis to its own exact page count independently (the pre-fix behavior this test used to
        // assert) produces a non-uniform per-axis scale Excel's rendering model can never apply, and
        // over-paginates whichever axis needed less shrink.
        var workbook = new Workbook("W");
        var sheet    = workbook.AddSheet("S");
        sheet.PaperSize   = WorksheetPaperSize.A4;
        sheet.PageMargins = WorksheetPageMargins.Narrow;
        sheet.ScaleToFit  = WorksheetScaleToFit.Default;

        // Discover the natural (unscaled) per-page capacity so the range can be sized in terms of it
        // instead of hardcoding brittle pixel-derived row/column counts.
        var probeRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2000, 2000));
        var natural = SheetPdfPageSetupResolver.ResolveCapacity(sheet, probeRange);

        // Size the range so 100%-scale pagination is exactly 6 column-pages x 5 row-pages -- i.e. the
        // row axis is ALREADY exactly at its FitToPagesTall=5 target unscaled, while the column axis
        // needs real shrink to hit FitToPagesWide=2.
        var totalCols = natural.ColumnsPerPage * 6;
        var totalRows = natural.RowsPerPage * 5;
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, totalRows, totalCols));

        var naturalCapacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);
        ((uint)Math.Ceiling(totalCols / (double)naturalCapacity.ColumnsPerPage)).Should().Be(6u,
            "sanity check: range sized to 6 natural column-pages");
        ((uint)Math.Ceiling(totalRows / (double)naturalCapacity.RowsPerPage)).Should().Be(5u,
            "sanity check: range sized to 5 natural row-pages -- already exactly at the FitToPagesTall target");

        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 2, FitToPagesTall: 5);
        var capacity = SheetPdfPageSetupResolver.ResolveCapacity(sheet, range);

        var columnPages = (uint)Math.Ceiling(totalCols / (double)capacity.ColumnsPerPage);
        var rowPages = (uint)Math.Ceiling(totalRows / (double)capacity.RowsPerPage);

        columnPages.Should().Be(2u, "the column axis must hit its explicit Fit-to-2-wide target");
        rowPages.Should().BeLessThan(5u,
            "Excel applies the SAME uniform scale (min(2/6, 5/5) = 33%) to the row axis too, so it " +
            "shrinks along with the column axis instead of staying at its already-met 5-page target");
        (columnPages * rowPages).Should().BeLessThan(10u,
            "the pre-fix independent-per-axis resolution produces 2x5=10 pages; the uniform-scale fix " +
            "produces roughly 2x2=4");
    }
}
