using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageBreaksMetadataWriterPerformanceTests
{
    [Fact]
    public void Save_UsesSingleBreakLookupForModeledBreaksAndNativeAttributes()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetPageBreaksMetadataWriter.cs"));

        source.Should().Contain("BuildBreaksById(pageBreaks)");
        source.Should().Contain("breaksById[idText] = breakElement;");
        source.Should().NotContain(
            ".OrderBy(id => id)",
            "sheet page breaks are already sorted by the model and should not be re-sorted on save");
        source.Should().NotContain(
            ".Any(element => string.Equals(element.Attribute(\"id\")?.Value",
            "existing worksheet breaks should be indexed once instead of scanned once per modeled break");
        source.Should().NotContain(
            "workbook.Sheets.Where(",
            "the save loop should avoid allocating a LINQ iterator for metadata-bearing sheets");
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
