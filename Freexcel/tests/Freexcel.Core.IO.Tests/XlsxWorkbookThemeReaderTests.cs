using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;
using System.IO.Compression;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxWorkbookThemeReaderTests
{
    [Fact]
    public void Load_ReturnsOfficeThemeWhenThemePartIsMissing()
    {
        using var package = CreatePackage();

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Should().Be(WorkbookTheme.Office);
    }

    [Fact]
    public void Load_ReadsThemeNameFontsEffectsAndColorScheme()
    {
        using var package = CreatePackage(("xl/theme/theme1.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Freexcel Test Theme">
              <a:themeElements>
                <a:clrScheme name="Freexcel Colors">
                  <a:dk1><a:srgbClr val="010203"/></a:dk1>
                  <a:lt1><a:sysClr val="window" lastClr="FAFBFC"/></a:lt1>
                  <a:dk2><a:srgbClr val="111213"/></a:dk2>
                  <a:lt2><a:srgbClr val="E0E1E2"/></a:lt2>
                  <a:accent1><a:srgbClr val="0C2238"/></a:accent1>
                  <a:accent2><a:srgbClr val="456789"/></a:accent2>
                  <a:accent3><a:srgbClr val="ABCDEF"/></a:accent3>
                  <a:accent4><a:srgbClr val="102030"/></a:accent4>
                  <a:accent5><a:srgbClr val="405060"/></a:accent5>
                  <a:accent6><a:srgbClr val="708090"/></a:accent6>
                  <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
                  <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
                </a:clrScheme>
                <a:fontScheme name="Freexcel Fonts">
                  <a:majorFont><a:latin typeface="Major Test"/></a:majorFont>
                  <a:minorFont><a:latin typeface="Minor Test"/></a:minorFont>
                </a:fontScheme>
                <a:fmtScheme name="Effects Test"/>
              </a:themeElements>
            </a:theme>
            """));

        var theme = XlsxWorkbookThemeReader.Load(package);

        theme.Name.Should().Be("Freexcel Test Theme");
        theme.MajorFontName.Should().Be("Major Test");
        theme.MinorFontName.Should().Be("Minor Test");
        theme.EffectsName.Should().Be("Effects Test");
        theme.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(1, 2, 3));
        theme.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(250, 251, 252));
        theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));
    }

    [Theory]
    [InlineData("FF0C2238", 12, 34, 56)]
    [InlineData("#0C2238", 12, 34, 56)]
    public void TryReadCellColor_ReadsXlsxRgbAttributes(string rgb, byte r, byte g, byte b)
    {
        var element = System.Xml.Linq.XElement.Parse($"""<color rgb="{rgb}"/>""");

        XlsxColorReader.TryReadCellColor(element, out var color).Should().BeTrue();
        color.Should().Be(new CellColor(r, g, b));
    }

    private static MemoryStream CreatePackage(params (string Path, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
