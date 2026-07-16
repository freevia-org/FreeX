using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for R44-io-print-scaling-headerfooter-3-1: Sheet.FitToPage is an independent,
/// load-time flag that SetPageSetupCommand never updates -- only Sheet.ScaleToFit changes when the
/// user toggles the Page Setup dialog's fit-to-page/scale-% switch. XlsxWorksheetPageSetupMetadataWriter
/// used to blindly write the (possibly stale) Sheet.FitToPage value into the saved
/// sheetPr/pageSetUpPr/@fitToPage attribute, silently corrupting it in both directions:
///   - Scenario A: a sheet loaded in scale mode (FitToPage=false) that the user then switches to
///     fit-to-page mode (ScaleToFit=(null, wide, tall)) saved fitToPage="0" over ClosedXML's correct
///     "1", so real Excel ignored the new fit-to-page setting and reused the stale scale.
///   - Scenario B: a sheet loaded in fit-to-page mode (FitToPage=true) that the user then switches to
///     scale mode (ScaleToFit=(percent, null, null)) got an injected fitToPage="1" pageSetUpPr element
///     that ClosedXML never wrote, so real Excel ignored the new scale% and squeezed onto one page.
/// </summary>
public sealed class R44_PageSetupFitToPageScaleSyncTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement SaveAndGetWorksheetRoot(Workbook workbook)
    {
        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
        using var stream = entry.Open();
        return XDocument.Load(stream).Root!;
    }

    private static (Workbook Workbook, Sheet Sheet) CreateSheet()
    {
        var workbook = new Workbook("PageSetupFitToPageScaleSync");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("data"));
        return (workbook, sheet);
    }

    [Fact]
    public void SwitchingToFitToPage_WritesFitToPageTrue_EvenWithStaleFalseFlag()
    {
        // R44-io-print-scaling-headerfooter-3-1, Scenario A: the sheet was loaded with a stale
        // FitToPage=false (scale mode) but the user has since switched Sheet.ScaleToFit to an
        // explicit fit-to-page axis via the Page Setup dialog (SetPageSetupCommand, which never
        // touches Sheet.FitToPage). The saved file must reflect fit-to-page mode, not the stale flag.
        // Uses a non-default 2x1 axis (rather than 1x1) so fitToWidth/fitToHeight are written
        // explicitly instead of being omitted as schema defaults.
        var (workbook, sheet) = CreateSheet();
        sheet.FitToPage = false;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, 1);

        var root = SaveAndGetWorksheetRoot(workbook);

        var pageSetupProperties = root.Element(WorksheetNs + "sheetPr")?.Element(WorksheetNs + "pageSetUpPr");
        pageSetupProperties.Should().NotBeNull("the sheet is in fit-to-page mode and must record fitToPage");
        pageSetupProperties!.Attribute("fitToPage")!.Value.Should().Be(
            "1",
            "the sheet's current ScaleToFit is fit-to-page mode, so fitToPage must not stay stuck at the stale false value");

        var pageSetup = root.Element(WorksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        pageSetup!.Attribute("fitToWidth")!.Value.Should().Be("2");
    }

    [Fact]
    public void SwitchingToScalePercent_DoesNotInjectFitToPageTrue_EvenWithStaleTrueFlag()
    {
        // R44-io-print-scaling-headerfooter-3-1, Scenario B (the mirror/worse case): the sheet was
        // loaded with a stale FitToPage=true (fit-to-page mode) but the user has since switched
        // Sheet.ScaleToFit to an explicit scale percentage. The saved file must not gain an injected
        // pageSetUpPr/@fitToPage="1" that would make real Excel squeeze everything onto one page
        // instead of honoring the chosen scale%.
        var (workbook, sheet) = CreateSheet();
        sheet.FitToPage = true;
        sheet.ScaleToFit = new WorksheetScaleToFit(75, null, null);

        var root = SaveAndGetWorksheetRoot(workbook);

        var pageSetupProperties = root.Element(WorksheetNs + "sheetPr")?.Element(WorksheetNs + "pageSetUpPr");
        if (pageSetupProperties is not null)
        {
            pageSetupProperties.Attribute("fitToPage")?.Value.Should().NotBe(
                "1",
                "the sheet's current ScaleToFit is scale mode, so a stale fitToPage=true must not be injected");
        }

        var pageSetup = root.Element(WorksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        pageSetup!.Attribute("scale")!.Value.Should().Be("75");
        pageSetup.Attribute("fitToWidth").Should().BeNull("an injected fit-to-page constraint would override the chosen scale%");
        pageSetup.Attribute("fitToHeight").Should().BeNull("an injected fit-to-page constraint would override the chosen scale%");
    }

    [Fact]
    public void ConsistentFitToPageState_StillWritesFitToPageTrue_NoRegression()
    {
        // Sibling no-regression case: when Sheet.FitToPage already agrees with Sheet.ScaleToFit
        // (the ordinary, non-stale case), the saved file must still correctly record fit-to-page mode.
        var (workbook, sheet) = CreateSheet();
        sheet.FitToPage = true;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, 3);

        var root = SaveAndGetWorksheetRoot(workbook);

        var pageSetupProperties = root.Element(WorksheetNs + "sheetPr")?.Element(WorksheetNs + "pageSetUpPr");
        pageSetupProperties.Should().NotBeNull();
        pageSetupProperties!.Attribute("fitToPage")!.Value.Should().Be("1");

        var pageSetup = root.Element(WorksheetNs + "pageSetup");
        pageSetup!.Attribute("fitToWidth")!.Value.Should().Be("2");
        pageSetup.Attribute("fitToHeight")!.Value.Should().Be("3");
    }

    [Fact]
    public void AutoPageBreaksAlone_StillCreatesPageSetupPropertiesElement_NoRegression()
    {
        // Sibling no-regression case: AutoPageBreaks (unrelated to fit-to-page/scale) must still force
        // creation of the pageSetUpPr element on its own, with fitToPage correctly derived as false
        // (the sheet is left in the default 100% scale mode).
        var (workbook, sheet) = CreateSheet();
        sheet.AutoPageBreaks = false;

        var root = SaveAndGetWorksheetRoot(workbook);

        var pageSetupProperties = root.Element(WorksheetNs + "sheetPr")?.Element(WorksheetNs + "pageSetUpPr");
        pageSetupProperties.Should().NotBeNull("AutoPageBreaks alone must still force the element to be written");
        pageSetupProperties!.Attribute("autoPageBreaks")!.Value.Should().Be("0");
        pageSetupProperties.Attribute("fitToPage")?.Value.Should().NotBe(
            "1",
            "the sheet is in default 100% scale mode, so fitToPage must be false/absent");
    }
}
