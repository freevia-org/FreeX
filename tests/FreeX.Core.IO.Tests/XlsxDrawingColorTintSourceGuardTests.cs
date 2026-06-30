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
            "XlsxWorksheetDrawingObjectWriter.cs",
            "XlsxColorReader.cs"
        })
        {
            var source = TestWorkspaceFiles.ReadCoreIoRepoSource(fileName);

            if (fileName != "XlsxColorReader.cs")
                source.Should().Contain("XlsxDrawingColorWriter.ToSolidFill");
            source.Should().NotContain(
                "ApplyTint(",
                $"{fileName} should not grow another local tint writer");
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
        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingColorWriter.cs")
            .Should()
            .Contain("XlsxDrawingThemeColorSlots.ToSchemeColorValue");

        var readerSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingColorReader.cs");
        readerSource
            .Should()
            .Contain("XlsxDrawingColorTint.ReadFrom");
        readerSource
            .Should()
            .Contain("XlsxDrawingThemeColorSlots.TryMapRole");
        readerSource
            .Should()
            .Contain("XlsxColorReader.TryParseHexColor(value, out color)");

        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingThemeColorSlots.cs")
            .Should()
            .Contain("DrawingMlThemeColorSlotMapper");
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

        TestWorkspaceFiles.ReadCoreModelRepoSource("WorkbookThemeTint.cs")
            .Should()
            .Contain("DrawingMlColorTransform.ApplyTint")
            .And.Contain("DrawingMlColorTransform.ApplyShade");
    }
}
