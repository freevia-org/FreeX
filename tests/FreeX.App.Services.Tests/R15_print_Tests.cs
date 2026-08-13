using FluentAssertions;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Services;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Round 15 fix verification for the portable/Skia PDF header-footer path:
/// <list type="bullet">
///   <item>
///     R15-header-footer-print-titles-1: <c>WorkbookPdfContentBuilder.ExpandHF</c> must expand the
///     <c>&amp;Z</c> short code (and bracketed <c>&amp;[Path]</c>) to the workbook directory instead
///     of leaking the literal token text, matching the WPF <c>PagePrintTextPlanner</c> path.
///   </item>
///   <item>
///     R15-header-footer-print-titles-2: the real workbook directory must actually reach
///     <c>ExpandHF</c> end to end via <see cref="WorkbookPdfContentBuilder.BuildPageWithPageSetup"/>'s
///     new <c>workbookDirectory</c> parameter, rather than always being the empty string.
///   </item>
/// </list>
/// </summary>
public sealed class R15_print_Tests
{
    [Fact]
    public void ResolveWorkbookDirectoryTokenValue_ReturnsContainingDirectoryWithTrailingSeparator()
    {
        var filePath = Path.Combine("root", "reports", "Q3.xlsx");
        var expected = Path.GetDirectoryName(filePath) + Path.DirectorySeparatorChar;

        PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue(filePath).Should().Be(expected);
        PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue(null).Should().BeEmpty();
        PagePrintTextPlanner.ResolveWorkbookDirectoryTokenValue("Q3.xlsx").Should().BeEmpty();
    }

    [Fact]
    public void ExpandHF_ShortCodeZ_ExpandsToWorkbookDirectory_NotLiteralToken()
    {
        var result = WorkbookPdfContentBuilder.ExpandHF(
            "&Z",
            pageNumber: 1,
            totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: @"C:\R",
            sheetName: "Sheet1",
            now: DateTime.Now);

        result.Should().Be(@"C:\R", "&Z must expand to the workbook directory, not leak as literal text");
    }

    [Fact]
    public void ExpandHF_BracketedPath_ExpandsToWorkbookDirectory_NotLiteralToken()
    {
        var result = WorkbookPdfContentBuilder.ExpandHF(
            "&[Path]",
            pageNumber: 1,
            totalPages: 1,
            workbookName: "Book.xlsx",
            workbookDirectory: @"C:\R",
            sheetName: "Sheet1",
            now: DateTime.Now);

        result.Should().Be(@"C:\R", "&[Path] must expand to the workbook directory, not leak as literal text");
    }

    [Fact]
    public void BuildPageWithPageSetup_HeaderPathAndFile_ExpandsToFullSavedWorkbookPath()
    {
        // Workbook.Name mirrors the saved file name ("Q3.xlsx"); workbookDirectory is the folder
        // that contains it, with a trailing separator -- concatenated, &[Path]&[File] must equal
        // the full saved path, exactly like Excel and the WPF PagePrintTextPlanner path.
        var workbook = new Workbook("Q3.xlsx");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageMargins = WorksheetPageMargins.Normal;
        sheet.PageHeader = new WorksheetHeaderFooter("", "&[Path]&[File]", "");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Hello"));

        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(
                WorkbookExportPrintScope.ActiveSheet,
                WorkbookExportPrintOutputKind.Pdf,
                ActiveSheetIndex: 0),
            WorkbookExportPrintSurface.MacOs);
        printPlan.IsReady.Should().BeTrue(printPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(printPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var document = WorkbookPdfContentBuilder.BuildWithPageSetup(
            workbook,
            pdfPlan,
            workbookDirectory: @"C:\Reports\");

        document.Pages.Should().NotBeEmpty();
        var allText = document.Pages[0].Ops
            .OfType<Free.Shared.Pdf.PdfText>()
            .Select(t => t.Text)
            .ToList();

        allText.Should().Contain(@"C:\Reports\Q3.xlsx",
            "&[Path]&[File] must expand to the full saved workbook path, not drop the directory");
    }
}
