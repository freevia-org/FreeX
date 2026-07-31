using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R103: GetFittingRowHeight must clamp against the pixel-space row-height ceiling
/// (AutoFitSizingService.MaximumRowHeight = 409.5pt * 96/72 = 546px), not the raw points
/// value 409.5, so a large font size is not squeezed into a row shorter than the font itself.
/// </summary>
public sealed class WorkbookSessionFontSizeRowHeightClampTests
{
    [Fact]
    public void R103_SetSelectedRangeFontSize_LargeFontBelowPixelCeiling_FitsRowToFontNotToStalePointsCap()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        // fontSize=320: EstimateFittingRowHeight = ceil(320*96/72 + 5) = 432px, which is above
        // the stale 409.5 (points-as-pixels) clamp but below the correct 546px pixel ceiling.
        const double fontSize = 320;
        var expectedRowHeight = FontSizePlanner.EstimateFittingRowHeight(fontSize);
        expectedRowHeight.Should().Be(432);

        var result = session.SetSelectedRangeFontSize(fontSize);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(expectedRowHeight);
        sheet.RowHeights[1].Should().BeGreaterThan(409.5, "409.5 is Excel's ceiling in points, not the pixel unit Sheet.RowHeights stores");
    }

    [Fact]
    public void R103_ApplySelectedRangeCompactFormat_LargeFontBelowPixelCeiling_FitsRowToFontNotToStalePointsCap()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var session = CreateSession(workbook);
        session.SelectRange(new GridRange(a1, b2));

        const double fontSize = 350;
        var expectedRowHeight = FontSizePlanner.EstimateFittingRowHeight(fontSize);

        var result = session.ApplySelectedRangeCompactFormat(new StyleDiff(FontSize: fontSize), borderPreset: null);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(expectedRowHeight);
        sheet.RowHeights[2].Should().Be(expectedRowHeight);
    }

    [Fact]
    public void R103_SetSelectedRangeFontSize_ExtremeFontAbovePixelCeiling_ClampsToAutoFitMaximumRowHeight()
    {
        // No-regression sibling: a font so large that even the correct pixel-space fitting
        // height would exceed Excel's true ceiling must still clamp -- just to the right
        // (pixel) value, not be left unclamped.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        const double fontSize = 409; // WorkbookFactory.MaxDefaultFontSize
        var uncappedEstimate = FontSizePlanner.EstimateFittingRowHeight(fontSize);
        uncappedEstimate.Should().BeGreaterThan(AutoFitSizingService.MaximumRowHeight);

        var result = session.SetSelectedRangeFontSize(fontSize);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(AutoFitSizingService.MaximumRowHeight);
    }

    [Fact]
    public void R103_SetSelectedRangeFontSize_SmallFont_UnaffectedByClampChange()
    {
        // No-regression sibling covering the common/small-font path, which never neared
        // either clamp and must keep behaving identically.
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var result = session.SetSelectedRangeFontSize(24);

        result.Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(37); // ceil(24*96/72+5) = 37, well under both clamps
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
