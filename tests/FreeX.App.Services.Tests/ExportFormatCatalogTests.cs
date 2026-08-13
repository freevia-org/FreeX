using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class ExportFormatCatalogTests
{
    [Fact]
    public void Catalog_OwnsPdfAndXpsFormatConversions()
    {
        ExportFormatCatalog.Get(WorkbookExportPrintOutputKind.Pdf)
            .Should().BeSameAs(ExportFormatCatalog.Pdf);
        ExportFormatCatalog.Get(ExportFormat.Xps)
            .Should().BeSameAs(ExportFormatCatalog.Xps);
        ExportFormatCatalog.FromPdfXpsFilterIndex(ExportFormatCatalog.PdfXpsDialogXpsFilterIndex)
            .Should().BeSameAs(ExportFormatCatalog.Xps);
        ExportFormatCatalog.FromPdfXpsFilterIndex(99)
            .Should().BeSameAs(ExportFormatCatalog.Pdf);
    }

    [Theory]
    [InlineData(WorkbookExportPrintScope.ActiveSheet, ExportContentScope.ActiveSheet)]
    [InlineData(WorkbookExportPrintScope.SelectedRange, ExportContentScope.Selection)]
    [InlineData(WorkbookExportPrintScope.VisibleWorkbook, ExportContentScope.EntireWorkbook)]
    public void ScopeConversions_RoundTrip(
        WorkbookExportPrintScope printScope,
        ExportContentScope contentScope)
    {
        ExportFormatCatalog.ToContentScope(printScope).Should().Be(contentScope);
        ExportFormatCatalog.ToPrintScope(contentScope).Should().Be(printScope);
    }
}
