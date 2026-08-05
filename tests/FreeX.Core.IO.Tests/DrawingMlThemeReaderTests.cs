using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;

namespace FreeX.Core.IO.Tests;

public sealed class DrawingMlThemeReaderTests
{
    [Fact]
    public void Read_UsesSharedColorAndFontContract()
    {
        var theme = DrawingMlThemeReader.Read(XDocument.Parse(RepresentativeThemeXml));

        theme.Name.Should().Be("Contract Theme");
        theme.FontScheme.MajorLatinTypeface.Should().Be("Aptos Display");
        theme.FontScheme.MinorLatinTypeface.Should().Be("Aptos");
        theme.FormatSchemeName.Should().Be("Contract Effects");
        theme.ColorScheme[DrawingMlThemeColorSlot.Dark1]!.Value.ResolvedColor.Should().Be(new DrawingMlRgbColor(10, 20, 30));
        theme.ColorScheme[DrawingMlThemeColorSlot.Light1]!.Value.FallbackValue.Should().Be("FAFBFC");
        theme.ColorScheme[DrawingMlThemeColorSlot.Light1]!.Value.ResolvedColor.Should().Be(new DrawingMlRgbColor(250, 251, 252));
        theme.ColorScheme[DrawingMlThemeColorSlot.Accent1]!.Value.ResolvedColor.Should().Be(new DrawingMlRgbColor(144, 160, 176));
        theme.ColorScheme[DrawingMlThemeColorSlot.Accent2]!.Value.ResolvedColor.R.Should().Be(188);
        theme.ColorScheme[DrawingMlThemeColorSlot.Accent3]!.Value.ResolvedColor.Should().Be(new DrawingMlRgbColor(0, 128, 0));
        theme.ColorScheme[DrawingMlThemeColorSlot.Accent1]!.Value.Kind.Should().Be(DrawingMlThemeColorKind.Srgb);
        theme.ColorScheme[DrawingMlThemeColorSlot.Light1]!.Value.Kind.Should().Be(DrawingMlThemeColorKind.System);
    }

    [Fact]
    public void ReadColor_AppliesCombinedLuminanceTintAndShadeTransforms()
    {
        var drawing = XNamespace.Get(DrawingMlThemeReader.DrawingNamespaceUri);
        var colorContainer = new XElement(
            drawing + "accent1",
            new XElement(
                drawing + "srgbClr",
                new XAttribute("val", "000000"),
                new XElement(drawing + "lumMod", new XAttribute("val", "50000")),
                new XElement(drawing + "lumOff", new XAttribute("val", "10000")),
                new XElement(drawing + "tint", new XAttribute("val", "80000")),
                new XElement(drawing + "shade", new XAttribute("val", "50000"))));

        DrawingMlThemeReader.ReadColor(colorContainer)!.Value.ResolvedColor
            .Should()
            .Be(new DrawingMlRgbColor(36, 36, 36));
    }

    [Fact]
    public void SharedSlotMapper_PreservesAliasesAndCanonicalNames()
    {
        DrawingMlThemeColorSlotMapper.TryMapRole(" tx1 ", out var tx1).Should().BeTrue();
        DrawingMlThemeColorSlotMapper.TryMapRole("bg2", out var bg2).Should().BeTrue();

        tx1.Should().Be(DrawingMlThemeColorSlot.Dark1);
        bg2.Should().Be(DrawingMlThemeColorSlot.Light2);
        DrawingMlThemeColorSlotMapper.ToSchemeColorValue(DrawingMlThemeColorSlot.FollowedHyperlink).Should().Be("folHlink");
    }

    [Fact]
    public void TryReadThemePart_ResolvesRelationshipRelativeToOwnerAndFallsBack()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTheme" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="themes/custom%20theme.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "word/themes/custom theme.xml", RepresentativeThemeXml);
            WriteEntry(archive, "word/theme/theme1.xml", """<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Fallback" />""");
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        DrawingMlThemeReader.ResolveThemePartPath(readArchive, "word/document.xml", "word/theme/theme1.xml")
            .Should().Be("word/themes/custom theme.xml");
        DrawingMlThemeReader.TryReadThemePart(readArchive, "word/document.xml", "word/theme/theme1.xml")!.Name
            .Should().Be("Contract Theme");
    }

    [Fact]
    public void TryReadThemePart_UsesFallbackWhenThemeRelationshipTargetIsMissing()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "word/_rels/document.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdTheme" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="themes/missing-theme.xml" />
                </Relationships>
                """);
            WriteEntry(archive, "word/theme/theme1.xml", """<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Fallback" />""");
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        DrawingMlThemeReader.TryReadThemePart(readArchive, "word/document.xml", "word/theme/theme1.xml")!.Name
            .Should()
            .Be("Fallback");
    }

    [Fact]
    public void ProductReaders_StayThinThemeAdapters()
    {
        var root = FindRepositoryRoot();
        var xlsx = File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.IO", "XlsxWorkbookThemeReader.cs"));
        var docx = File.ReadAllText(Path.Combine(root, "freew", "FreeW.Core.IO", "DocxReader.cs"));
        var pptx = File.ReadAllText(Path.Combine(root, "freep", "FreeP.Core.IO", "PptxPackageReader.cs"));

        xlsx.Should().Contain("DrawingMlThemeReader.Read").And.NotContain("ReadThemeTypeface").And.NotContain("ReadThemeColor(");
        docx.Should().Contain("DrawingMlThemeReader.TryReadThemePart").And.NotContain("ResolveThemePartPath");
        pptx.Should().Contain("DrawingMlThemeReader.TryReadThemePart").And.NotContain("ReadColorSlot").And.NotContain("ReadColorScheme");
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(path).Open());
        writer.Write(text);
    }

    private static string FindRepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

    private const string RepresentativeThemeXml = """
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Contract Theme">
          <a:themeElements>
            <a:clrScheme name="Contract Colors">
              <a:dk1><a:srgbClr val="0A141E" /></a:dk1>
              <a:lt1><a:sysClr val="window" lastClr="FAFBFC" /></a:lt1>
              <a:dk2><a:srgbClr val="203040" /></a:dk2>
              <a:lt2><a:srgbClr val="E0E0E0" /></a:lt2>
              <a:accent1><a:srgbClr val="204060"><a:tint val="50000" /></a:srgbClr></a:accent1>
              <a:accent2><a:scrgbClr r="50000" g="0" b="0" /></a:accent2>
              <a:accent3><a:prstClr val="green" /></a:accent3>
            </a:clrScheme>
            <a:fontScheme name="Contract Fonts">
              <a:majorFont><a:latin typeface="Aptos Display" /><a:ea typeface="Major East Asia" /></a:majorFont>
              <a:minorFont><a:latin typeface="Aptos" /><a:cs typeface="Minor Complex" /></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Contract Effects" />
          </a:themeElements>
        </a:theme>
        """;
}
