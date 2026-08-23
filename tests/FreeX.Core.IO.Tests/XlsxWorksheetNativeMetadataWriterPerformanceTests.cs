using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetNativeMetadataWriterPerformanceTests
{
    [Theory]
    [InlineData("XlsxWorksheetDimensionMetadataWriter.cs", "DimensionMetadata")]
    [InlineData("XlsxWorksheetHeaderFooterMetadataWriter.cs", "HeaderFooterMetadata")]
    [InlineData("XlsxWorksheetPrimaryViewMetadataWriter.cs", "PrimaryViewMetadata")]
    [InlineData("XlsxWorksheetSheetPropertiesMetadataWriter.cs", "SheetPropertiesMetadata")]
    public void Save_SkipsSheetsWithoutNativeMetadataWithoutLinqFiltering(string fileName, string propertyName)
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource(fileName);

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain($"var metadata = sheet.{propertyName};");
        // A plain substring (no required closing paren) so this still matches
        // XlsxWorksheetPrimaryViewMetadataWriter's extended guard ("if (metadata is null &&
        // !isActiveSheet)", which additionally still visits the active sheet even with no metadata
        // so tabSelected can be synced -- see R58-services-zoom-view-state-6-1) as well as the
        // plain "if (metadata is null)" early-exit used by the other writers here; the actual
        // performance contract under test is the absence of a LINQ filter iterator below.
        source.Should().Contain("if (metadata is null");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "worksheet native metadata saving should avoid allocating a LINQ filter iterator over workbook sheets");
    }

    [Fact]
    public void AdditionalWorksheetViews_SaveSkipsSheetsWithoutViewsWithoutLinqFiltering()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetAdditionalViewMapper.cs");
        var traversal = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetPackageEditTraversal.cs");

        source.Should().Contain("XlsxWorksheetPackageEditTraversal.Edit");
        traversal.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("var additionalViews = sheet.AdditionalViews;");
        source.Should().Contain("if (additionalViews is null)");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "additional worksheet view saving should avoid allocating a LINQ filter iterator over workbook sheets");
        source.Should().NotContain(
            ".Views.Select(ToXml).OfType<XElement>()",
            "additional worksheet view saving should avoid LINQ projection/filter iterators while serializing views");
    }

}
