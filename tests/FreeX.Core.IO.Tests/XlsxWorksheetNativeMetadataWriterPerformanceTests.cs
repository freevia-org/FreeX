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
        var source = File.ReadAllText(FindWorkspaceFile("src", "FreeX.Core.IO", fileName));

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain($"var metadata = sheet.{propertyName};");
        source.Should().Contain("if (metadata is null)");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "worksheet native metadata saving should avoid allocating a LINQ filter iterator over workbook sheets");
    }

    [Fact]
    public void AdditionalWorksheetViews_SaveSkipsSheetsWithoutViewsWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetAdditionalViewMapper.cs"));

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("var additionalViews = sheet.AdditionalViews;");
        source.Should().Contain("if (additionalViews is null)");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "additional worksheet view saving should avoid allocating a LINQ filter iterator over workbook sheets");
        source.Should().NotContain(
            ".Views.Select(ToXml).OfType<XElement>()",
            "additional worksheet view saving should avoid LINQ projection/filter iterators while serializing views");
    }

    private static string FindWorkspaceFile(params string[] relativeParts) => TestWorkspaceFiles.FindRepoFile(relativeParts);
}
