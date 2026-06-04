using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageMarginsMetadataWriterPerformanceTests
{
    [Fact]
    public void Save_SkipsSheetsWithoutPageMarginsMetadataWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetPageMarginsMetadataWriter.cs"));

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("var metadata = sheet.PageMarginsMetadata;");
        source.Should().Contain("if (metadata is null)");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "worksheet page-margins metadata saving should avoid allocating a LINQ filter iterator over workbook sheets");
    }

    private static string FindWorkspaceFile(params string[] relativeParts) => TestWorkspaceFiles.FindRepoFile(relativeParts);
}
