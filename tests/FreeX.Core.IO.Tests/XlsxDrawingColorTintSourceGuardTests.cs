using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDrawingColorTintSourceGuardTests
{
    [Fact]
    public void ChartThemeAndDrawingWriters_DelegateTintedSolidFillsToDrawingColorWriter()
    {
        foreach (var fileName in new[]
        {
            "XlsxChartXmlWriter.Format.cs",
            "XlsxWorkbookThemeWriter.cs",
            "XlsxWorksheetDrawingObjectWriter.cs"
        })
        {
            var source = TestWorkspaceFiles.ReadCoreIoRepoSource(fileName);

            source.Should().Contain("XlsxDrawingColorWriter.ToSolidFill");
            source.Should().NotContain(
                "ApplyTint(",
                $"{fileName} should not grow another local DrawingML tint writer");
            source.Should().NotContain(
                "new XElement(drawingNs + \"lumMod\"",
                $"{fileName} should delegate modeled tint XML through the shared color writer");
        }
    }

    [Fact]
    public void DrawingColorReaderAndWriter_ShareDrawingMlTintConversion()
    {
        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingColorWriter.cs")
            .Should()
            .Contain("XlsxDrawingColorTint.ApplyTo");

        var readerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingColorReader.cs");
        readerSource
            .Should()
            .Contain("XlsxDrawingColorTint.ReadFrom");
        readerSource
            .Should()
            .Contain("XlsxColorReader.TryParseHexColor(value, out color)");
    }

    [Fact]
    public void SpreadsheetThemeColorResolution_UsesWorkbookThemeTintMath()
    {
        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxColorReader.cs")
            .Should()
            .Contain("WorkbookThemeTint.Apply(indexedColor, ReadTint(element))");

        TestWorkspaceFiles.ReadCoreModelRepoSource("WorkbookTheme.cs")
            .Should()
            .Contain("WorkbookThemeTint.Apply(color, tint)");
    }
}
