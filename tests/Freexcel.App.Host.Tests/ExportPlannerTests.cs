using System.IO;
using FluentAssertions;

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
            : ExportFormat.PdfViaWindowsPrinter;

        ExportPlanner.InferExportFormat(path).Should().Be(expected);
    }

    [Fact]
    public void PlanExport_CarriesInferredFormatWithRequestedPath()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf");

        request.Should().Be(new ExportRequest(@"C:\temp\report.pdf", ExportFormat.PdfViaWindowsPrinter));
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
            "Direct PDF file export is limited by the Windows print pipeline. Exported XPS instead; use a PDF printer or convert the XPS file.");
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
}
