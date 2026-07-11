using FluentAssertions;
using System.Text.RegularExpressions;

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
    public void DrawingColorReaderAndWriter_UseSharedDrawingMlRgbHelper()
    {
        var colorReaderSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxColorReader.cs");
        ExtractMethod(colorReaderSource, "public static bool TryParseHexColor(")
            .Should()
            .Contain("DrawingMlRgbColor.TryParseHexRgb")
            .And.NotContain("byte.TryParse");

        var colorWriterSource = TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxDrawingColorWriter.cs");
        ExtractMethod(colorWriterSource, "public static string FormatRgb(")
            .Should()
            .Contain("DrawingMlRgbColor")
            .And.Contain(".ToHexRgb()")
            .And.NotContain("$\"{color.R:X2}");

        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookThemeReader.cs")
            .Should()
            .Contain("XlsxColorReader.TryParseHexColor(srgb, out var color)");
        TestWorkspaceFiles.ReadCoreIoRepoSource("XlsxWorkbookThemeWriter.cs")
            .Should()
            .Contain("XlsxDrawingColorWriter.FormatRgb(theme.GetColor(color.Slot))")
            .And.Contain("XlsxDrawingColorWriter.FormatRgb(scheme.Colors[color.Slot])");
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
            .And.Contain("DrawingMlColorTransform.ApplyLuminance");
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"method '{signature}' should exist");

        var nextMethod = Regex.Match(
            source[(start + signature.Length)..],
            @"\r?\n    (private|internal|public) static ");

        return nextMethod.Success
            ? source[start..(start + signature.Length + nextMethod.Index)]
            : source[start..];
    }
}
