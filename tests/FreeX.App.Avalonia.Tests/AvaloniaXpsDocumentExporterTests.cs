extern alias ProductionAvalonia;

using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;
using AvaloniaXpsDocumentExporter = ProductionAvalonia::FreeX.App.Avalonia.Pdf.AvaloniaXpsDocumentExporter;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaXpsDocumentExporterTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Publish_AppliesWorkbookPageRangeAndOptionalCoreProperties(bool includeProperties)
    {
        var workbook = new Workbook("Quarterly report")
        {
            FileSharing = new WorkbookFileSharingModel { UserName = "Mira" }
        };
        var details = workbook.AddSheet("Details");
        details.PaperSize = WorksheetPaperSize.Letter;
        details.SetPrintAreas([GridRange.Parse("A1:A1", details.Id), GridRange.Parse("B2:B2", details.Id)]);
        details.SetCell(new CellAddress(details.Id, 1, 1), new TextValue("First page"));
        details.SetCell(new CellAddress(details.Id, 2, 2), new TextValue("Second page"));
        var summary = workbook.AddSheet("Summary");
        summary.PaperSize = WorksheetPaperSize.Letter;
        summary.PageOrientation = WorksheetPageOrientation.Landscape;
        summary.SetCell(new CellAddress(summary.Id, 1, 1), new TextValue("Third page"));

        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.VisibleWorkbook, WorkbookExportPrintOutputKind.Xps),
            WorkbookExportPrintSurface.MacOs);
        var plan = PortablePdfExportPlanner.CreateXpsPlan(printPlan, workbook);
        var options = ExportOptions.ExcelLikeDefault with
        {
            Scope = ExportContentScope.EntireWorkbook,
            IncludeDocumentProperties = includeProperties,
            PageRange = new ExportPageRange(2, 3),
            IncludeDocumentStructureTags = true,
            BitmapTextWhenFontsMayNotBeEmbedded = true,
        };

        PortablePdfExportPlanner.TryApplyOptions(plan, options, out var effectivePlan, out var error)
            .Should().BeTrue(error);
        effectivePlan.PageRequests.Select(page => page.SheetName).Should().Equal("Details", "Summary");
        effectivePlan.PdfLanguage.Should().BeNull();
        effectivePlan.RasterDpi.Should().BeNull();
        effectivePlan.IncludeSelectableText.Should().BeTrue();

        using var output = new MemoryStream();
        AvaloniaXpsDocumentExporter.Save(workbook, effectivePlan, output).PageCount.Should().Be(2);
        output.Position = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        XNamespace xps = "http://schemas.microsoft.com/xps/2005/06";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        var fixedDocument = ReadXml(archive, "Documents/1/FixedDocument.fdoc");
        fixedDocument.Elements(xps + "PageContent").Should().HaveCount(2);
        var firstPage = ReadXml(archive, "Documents/1/Pages/1.fpage");
        firstPage.Attribute("Width")!.Value.Should().Be("816");
        firstPage.Attribute("Height")!.Value.Should().Be("1056");
        var secondPage = ReadXml(archive, "Documents/1/Pages/2.fpage");
        secondPage.Attribute("Width")!.Value.Should().Be("1056");
        secondPage.Attribute("Height")!.Value.Should().Be("816");

        if (includeProperties)
        {
            var properties = ReadXml(archive, "docProps/core.xml");
            properties.Element(dc + "title")!.Value.Should().Be("Quarterly report");
            properties.Element(dc + "creator")!.Value.Should().Be("Mira");
        }
        else
        {
            archive.GetEntry("docProps/core.xml").Should().BeNull();
        }
    }

    private static XElement ReadXml(ZipArchive archive, string path)
    {
        using var entry = archive.GetEntry(path)!.Open();
        return XElement.Load(entry);
    }
}
