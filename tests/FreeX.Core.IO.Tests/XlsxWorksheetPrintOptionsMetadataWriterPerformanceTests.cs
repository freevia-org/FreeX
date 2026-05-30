using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPrintOptionsMetadataWriterPerformanceTests
{
    [Fact]
    public void Save_SkipsSheetsWithoutPrintOptionsMetadataWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetPrintOptionsMetadataWriter.cs"));

        source.Should().Contain("foreach (var sheet in workbook.Sheets)");
        source.Should().Contain("var metadata = sheet.PrintOptionsMetadata;");
        source.Should().Contain("if (metadata is null)");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "worksheet print-options metadata saving should avoid allocating a LINQ filter iterator over workbook sheets");
    }

    private static string FindWorkspaceFile(params string[] relativeParts)
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current.FullName }.Concat(relativeParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        return Path.Combine(new[] { Directory.GetCurrentDirectory() }.Concat(relativeParts).ToArray());
    }
}
