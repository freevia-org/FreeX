using System.IO;

namespace FreeW.Core.IO.Tests;

public sealed class DocxColorHexNormalizationBoundaryTests
{
    [Fact]
    public void WordprocessingColorHexNormalizationStaysLocalToDocxReaderWriter()
    {
        var reader = ReadSource("freew", "FreeW.Core.IO", "DocxReader.cs");
        reader.Should().Contain("Keep this WordprocessingML color boundary local");
        reader.Should().Contain("is null or \"auto\" ? null : \"#\" +");
        reader.Should().Contain("HighlightTokenToHex(highlightNamedToken)");
        reader.Should().NotContain("using Free.Shared.Drawing;");
        reader.Should().NotContain("using Free.Shared.Theme;");

        var writer = ReadSource("freew", "FreeW.Core.IO", "DocxWriter.cs");
        writer.Should().Contain("Keep this WordprocessingML color boundary local");
        writer.Should().Contain("HexToHighlightToken(highlightToken)");
        writer.Should().Contain("new XAttribute(W + \"color\", \"auto\")");
        writer.Should().NotContain("using Free.Shared.Drawing;");
        writer.Should().NotContain("using Free.Shared.Theme;");
    }

    private static string ReadSource(params string[] relativePath)
    {
        var path = relativePath.Aggregate(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), Path.Combine);
        return File.ReadAllText(path);
    }

}
