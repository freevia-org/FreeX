using System.IO.Compression;
using System.Xml.Linq;
using FreeP.Core.IO;

using PresentationModel = FreeP.Core.Model.Presentation;

namespace FreeP.App.Compositor.Tests;

public sealed class DrawingMlThemeFontSchemeTests
{
    private static readonly XNamespace DrawingNamespace =
        "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void PptxWriter_PatchesLatinFontsAndPreservesNativeFontSchemeDetails()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Theme.FontScheme.MajorLatinFont = "New Major";
        presentation.Theme.FontScheme.MinorLatinFont = "New Minor";
        presentation.Theme.NativeFontSchemeXml = """
            <a:fontScheme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Custom">
              <a:majorFont>
                <a:latin typeface="Old Major"/>
                <a:ea typeface="Yu Gothic"/>
                <a:cs typeface="Times New Roman"/>
                <a:font script="Jpan" typeface="Yu Gothic UI"/>
              </a:majorFont>
              <a:minorFont>
                <a:latin typeface="Old Minor"/>
                <a:ea typeface="Yu Mincho"/>
                <a:cs typeface="Arial"/>
              </a:minorFont>
              <a:extLst><a:ext uri="urn:preserve-me"/></a:extLst>
            </a:fontScheme>
            """;

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);
        output.Position = 0;

        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        using var themeStream = archive.GetEntry("ppt/theme/theme1.xml")!.Open();
        var theme = XDocument.Load(themeStream);
        var fontScheme = theme.Root!
            .Element(DrawingNamespace + "themeElements")!
            .Element(DrawingNamespace + "fontScheme")!;
        var major = fontScheme.Element(DrawingNamespace + "majorFont")!;
        var minor = fontScheme.Element(DrawingNamespace + "minorFont")!;

        major.Element(DrawingNamespace + "latin")!.Attribute("typeface")!.Value.Should().Be("New Major");
        minor.Element(DrawingNamespace + "latin")!.Attribute("typeface")!.Value.Should().Be("New Minor");
        major.Element(DrawingNamespace + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic");
        major.Element(DrawingNamespace + "cs")!.Attribute("typeface")!.Value.Should().Be("Times New Roman");
        major.Element(DrawingNamespace + "font")!.Attribute("typeface")!.Value.Should().Be("Yu Gothic UI");
        minor.Element(DrawingNamespace + "ea")!.Attribute("typeface")!.Value.Should().Be("Yu Mincho");
        minor.Element(DrawingNamespace + "cs")!.Attribute("typeface")!.Value.Should().Be("Arial");
        fontScheme.Element(DrawingNamespace + "extLst")!.Should().NotBeNull();
    }

    [Fact]
    public void PptxWriter_InvalidNativeFontSchemeXml_FallsBackToSyntheticFontScheme()
    {
        var presentation = PresentationModel.CreateEmpty();
        presentation.Theme.FontScheme.MajorLatinFont = "New Major";
        presentation.Theme.FontScheme.MinorLatinFont = "New Minor";
        presentation.Theme.NativeFontSchemeXml = "\uD800";

        using var output = new MemoryStream();
        PptxPackageWriter.Write(presentation, output);
        output.Position = 0;

        using var archive = new ZipArchive(output, ZipArchiveMode.Read);
        using var themeStream = archive.GetEntry("ppt/theme/theme1.xml")!.Open();
        var theme = XDocument.Load(themeStream);
        var fontScheme = theme.Root!
            .Element(DrawingNamespace + "themeElements")!
            .Element(DrawingNamespace + "fontScheme")!;

        fontScheme.Attribute("name")!.Value.Should().Be(presentation.Theme.Name);
        fontScheme.Element(DrawingNamespace + "majorFont")!
            .Element(DrawingNamespace + "latin")!
            .Attribute("typeface")!
            .Value.Should().Be("New Major");
        fontScheme.Element(DrawingNamespace + "minorFont")!
            .Element(DrawingNamespace + "latin")!
            .Attribute("typeface")!
            .Value.Should().Be("New Minor");
    }
}
