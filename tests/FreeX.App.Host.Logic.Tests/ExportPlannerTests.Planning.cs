using FluentAssertions;

namespace FreeX.App.Host.Tests;

public partial class ExportPlannerTests
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
    public void InferExportFormat_DefaultsToPdfForMalformedPath()
    {
        ExportPlanner.InferExportFormat("bad\0report.xps").Should().Be(ExportFormat.Pdf);
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
    public void PlanExport_AppendsPdfExtensionForExtensionlessPdfRequests()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report");

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.pdf",
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.ActualPath.Should().Be(@"C:\temp\report.pdf");
    }

    [Theory]
    [InlineData(@"C:\temp\report.export", @"C:\temp\report.pdf")]
    [InlineData(@"C:\temp\report.xlsx", @"C:\temp\report.pdf")]
    public void PlanExport_NormalizesMismatchedExtensionForInferredPdfRequests(
        string path,
        string expectedPath)
    {
        var request = ExportPlanner.PlanExport(path);

        request.Should().Be(new ExportRequest(
            expectedPath,
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.ActualPath.Should().Be(expectedPath);
    }

    [Fact]
    public void PlanExport_KeepsMalformedInferredPdfPathForHandledExportFailure()
    {
        var path = "bad\0report.xlsx";

        var request = ExportPlanner.PlanExport(path);

        request.Should().Be(new ExportRequest(
            path,
            ExportFormat.Pdf,
            ExportOptions.ExcelLikeDefault,
            null));
        request.ActualPath.Should().Be(path);
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
    public void PlanExport_AppendsXpsExtensionForExplicitExtensionlessXpsRequests()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report", ExportFormat.Xps, ExportOptions.ExcelLikeDefault);

        request.Should().Be(new ExportRequest(
            @"C:\temp\report.xps",
            ExportFormat.Xps,
            ExportOptions.ExcelLikeDefault,
            null));
        request.UsesXpsFallback.Should().BeFalse();
        request.ActualPath.Should().Be(@"C:\temp\report.xps");
    }

    [Theory]
    [InlineData(@"C:\temp\report.pdf", "xps", @"C:\temp\report.xps")]
    [InlineData(@"C:\temp\report.xps", "pdf", @"C:\temp\report.pdf")]
    [InlineData(@"C:\temp\report.export", "pdf", @"C:\temp\report.pdf")]
    [InlineData(@"C:\temp\report.export", "xps", @"C:\temp\report.xps")]
    public void PlanExport_NormalizesMismatchedExtensionForExplicitFormatRequests(
        string path,
        string explicitFormat,
        string expectedPath)
    {
        var format = explicitFormat == "xps"
            ? ExportFormat.Xps
            : ExportFormat.Pdf;

        var request = ExportPlanner.PlanExport(path, format, ExportOptions.ExcelLikeDefault);

        request.Path.Should().Be(expectedPath);
        request.Format.Should().Be(format);
        request.ActualPath.Should().Be(expectedPath);
    }

    [Fact]
    public void PlanExport_KeepsMalformedExplicitFormatPathForHandledExportFailure()
    {
        var path = "bad\0report.pdf";

        var request = ExportPlanner.PlanExport(path, ExportFormat.Xps, ExportOptions.ExcelLikeDefault);

        request.Path.Should().Be(path);
        request.Format.Should().Be(ExportFormat.Xps);
        request.ActualPath.Should().Be(path);
    }

    [Fact]
    public void ShouldPromptForNormalizedOverwrite_WhenDialogPathChangesToExistingTarget()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.txt", ExportFormat.Pdf, ExportOptions.ExcelLikeDefault);

        ExportPlanner.ShouldPromptForNormalizedOverwrite(
                @"C:\temp\report.txt",
                request,
                path => path.Equals(@"C:\temp\report.pdf", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeTrue();
    }

    [Fact]
    public void ShouldPromptForNormalizedOverwrite_SkipsUnchangedOrMissingTarget()
    {
        var request = ExportPlanner.PlanExport(@"C:\temp\report.pdf", ExportFormat.Pdf, ExportOptions.ExcelLikeDefault);

        ExportPlanner.ShouldPromptForNormalizedOverwrite(@"C:\temp\report.pdf", request, _ => true)
            .Should()
            .BeFalse();

        var normalized = ExportPlanner.PlanExport(@"C:\temp\report.txt", ExportFormat.Pdf, ExportOptions.ExcelLikeDefault);
        ExportPlanner.ShouldPromptForNormalizedOverwrite(@"C:\temp\report.txt", normalized, _ => false)
            .Should()
            .BeFalse();
    }
}
