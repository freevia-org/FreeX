using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageBreaksMetadataReaderPerformanceTests
{
    [Fact]
    public void Read_WalksBreakElementsWithoutLinqFiltering()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.IO", "XlsxWorksheetPageBreaksMetadataReader.cs"));

        source.Should().Contain("foreach (var breakElement in pageBreaks.Elements())");
        source.Should().Contain("breakElement.Name.LocalName");
        source.Should().NotContain(
            ".Elements().Where(",
            "page-break metadata reading should avoid allocating a LINQ filter iterator for worksheet break elements");
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
