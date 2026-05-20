using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;
using PdfSharp.Pdf.IO;

namespace Freexcel.App.Host.Tests;

public class ExportPlannerTests
{
    [Theory]
    [InlineData(@"C:\temp\book.xps", "xps")]
    [InlineData(@"C:\temp\book.XPS", "xps")]
    [InlineData(@"C:\temp\book.pdf", "pdf")]
    [InlineData(@"C:\temp\book", "pdf")]
    [InlineData(@"C:\temp\book.export", "pdf")]
    public void InferExportFormat_UsesXpsOnlyForXpsExtension(string path, string expectedFormat)
    {
        var expected = expectedFormat == "xps"
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

        ExportPlanner.InferExportFormat(path).Should().Be(expected);
    }

    [Fact]
    public void PlanExport_CarriesInferredFormatWithRequestedPath()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.pdf");
    }

    [Fact]
    public void PlanExport_XpsRequestKeepsRequestedPathAndDoesNotUseFallback()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.xps");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.xps",
            ExportFormat.Xps,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.xps");
    }

    [Fact]
    public void ExportOptions_DefaultsToActiveSheetWithoutDocumentProperties()
    {
        ExportOptions.ExcelLikeDefault.Should().Be(new ExportOptions(
            ExportContentScope.ActiveSheet,
            IncludeDocumentProperties: false,
            OpenAfterPublish: false));

        ExportPlanner.DescribeOptions(ExportOptions.ExcelLikeDefault)
            .Should().Be("Active sheet only; document properties are not included.");
    }

    [Fact]
    public void ExportOptions_DescribeSelectionAndOpenAfterPublish()
    {
        var options = new ExportOptions(
            ExportContentScope.Selection,
            IncludeDocumentProperties: true,
            OpenAfterPublish: true);

        ExportPlanner.DescribeOptions(options)
            .Should().Be("Selection; document properties are included; open after publishing.");
    }

    [Fact]
    public void ExportOptionsDialog_CreateResult_NormalizesExcelOptions()
    {
        ExportOptionsDialog.CreateResult(
                ExportContentScope.Selection,
                includeDocumentProperties: true,
                openAfterPublish: true)
            .Should()
            .Be(new ExportOptions(
                ExportContentScope.Selection,
                IncludeDocumentProperties: true,
                OpenAfterPublish: true));
    }

    [Fact]
    public void DescribeRequest_ExplainsPdfFallbackAndSupportedOptions()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        ExportPlanner.DescribeRequest(request).Should().Be(
            "Options: Active sheet only; document properties are not included.");
    }

    [Theory]
    [InlineData(@"C:\temp\report.pdf", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report.output", @"C:\temp\report.xps")]
    public void GetFallbackXpsPath_ChangesRequestedPathToXps(string requestedPath, string expected)
    {
        ExportPlanner.GetFallbackXpsPath(requestedPath).Should().Be(expected);
    }

    [Fact]
    public void PdfFallbackMessage_ExplainsWindowsPrintPipelineAndXpsConversion()
    {
        ExportPlanner.PdfFallbackMessage.Should().Be(
            "PDF export uses Freexcel's print renderer. XPS export remains available for Windows print-pipeline workflows.");
    }

    [Fact]
    public void PdfDocumentExporter_WritesPdfFileFromFixedDocument()
    {
        StaTestRunner.Run(() =>
        {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
            var document = CreateOnePageDocument();

            try
            {
                PdfDocumentExporter.Save(document, path);

                var bytes = File.ReadAllBytes(path);
                Encoding.ASCII.GetString(bytes[..Math.Min(bytes.Length, 8)]).Should().StartWith("%PDF-");
                Encoding.ASCII.GetString(bytes).Should().Contain("%%EOF");

                using var pdf = PdfReader.Open(path, PdfDocumentOpenMode.Import);
                pdf.PageCount.Should().Be(1);
                pdf.Pages[0].Width.Point.Should().BeApproximately(120, 0.01);
                pdf.Pages[0].Height.Point.Should().BeApproximately(90, 0.01);
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    private static FixedDocument CreateOnePageDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new System.Windows.Size(160, 120);
        var page = new FixedPage
        {
            Width = 160,
            Height = 120,
            Background = Brushes.White
        };
        page.Children.Add(new TextBlock { Text = "Freexcel PDF", Margin = new System.Windows.Thickness(12) });
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        document.Pages.Add(content);
        return document;
    }

    [Fact]
    public void PrintPreviewDialog_CreateTitle_IncludesWorkbookName()
    {
        PrintPreviewDialog.CreateTitle("Book1").Should().Be("Print Preview - Book1");
    }

    [Fact]
    public void PrintPreviewDialog_ContainsNativePrintCommandButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));

        source.Should().Contain("Content = \"Print...\"");
        source.Should().Contain("ShowNativePrintDialog");
        source.Should().Contain("PrintDocument(document.DocumentPaginator");
    }

    [Fact]
    public void PrintSettingsPlanner_SummarizesExcelLikeActiveSheetSettings()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            PageOrientation = WorksheetPageOrientation.Landscape,
            PaperSize = WorksheetPaperSize.Letter,
            PrintGridlines = true,
            PrintHeadings = true,
            ScaleToFit = new WorksheetScaleToFit(85, 1, 2)
        };

        var plan = PrintSettingsPlanner.Build(sheet);

        plan.Lines.Should().Equal(
            "Print active sheet",
            "Orientation: Landscape",
            "Paper size: Letter",
            "Scaling: 85%; fit 1 page wide by 2 tall",
            "Gridlines: on",
            "Headings: on");
        plan.Summary.Should().Be("Print active sheet; Orientation: Landscape; Paper size: Letter; Scaling: 85%; fit 1 page wide by 2 tall; Gridlines: on; Headings: on");
    }

    [Fact]
    public void PrintPreviewDialog_DisplaysPrintSettingsSummary()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "PrintPreviewDialog.cs"));
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        source.Should().Contain("PrintSettingsPlan settings");
        source.Should().Contain("settings.Summary");
        printExport.Should().Contain("PrintSettingsPlanner.Build(sheet)");
        printExport.Should().Contain("new PrintPreviewDialog(_workbook.Name, doc, settings)");
    }

    [Fact]
    public void ExportWorkflow_UsesOptionsDialogSelectionRangeAndOpenAfterPublish()
    {
        var printExport = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.PrintExport.cs"));

        printExport.Should().Contain("new ExportOptionsDialog(SheetGrid.SelectedRange is not null)");
        printExport.Should().Contain("ExportPlanner.PlanExport(saveDlg.FileName, optionsDialog.Result)");
        printExport.Should().Contain("ResolveExportRange(request.Options)");
        printExport.Should().Contain("OpenExportedFile(request.ActualPath)");
    }
}
