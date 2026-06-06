using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetPageBreaksMetadataReaderPerformanceTests
{
    [Fact]
    public void Read_WalksBreakElementsWithoutLinqFiltering()
    {
        var source = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorksheetPageBreaksMetadataReader.cs");

        source.Should().Contain("foreach (var breakElement in pageBreaks.Elements())");
        source.Should().Contain("breakElement.Name.LocalName");
        source.Should().NotContain(
            ".Elements().Where(",
            "page-break metadata reading should avoid allocating a LINQ filter iterator for worksheet break elements");
    }

}
