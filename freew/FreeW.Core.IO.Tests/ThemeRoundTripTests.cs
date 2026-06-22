using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Round-trip coverage for the real document theme part (roadmap item Z2): every <see cref="TextDocument"/>
/// materialises a <c>word/theme/theme1.xml</c> (a:theme → a:clrScheme + a:fontScheme + a:fmtScheme) with a
/// content-type Override and a "theme" document relationship, and the document's
/// <see cref="DocumentTheme"/> preset (colour scheme + major/minor fonts) survives write→read.
/// </summary>
public class ThemeRoundTripTests
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private const string ThemeContentType = "application/vnd.openxmlformats-officedocument.theme+xml";
    private const string ThemeRelType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    private static byte[] WriteBytes(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static XDocument EntryXml(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var entry = zip.GetEntry(entryPath)!.Open();
        return XDocument.Load(entry);
    }

    private static bool HasEntry(byte[] docx, string entryPath)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        return zip.GetEntry(entryPath) is not null;
    }

    private static TextDocument DocumentWithTheme(DocumentTheme theme)
    {
        var doc = new TextDocument { Theme = theme };
        doc.Blocks.Add(new Paragraph("Body"));
        return doc;
    }

    [Theory]
    [InlineData("Office")]
    [InlineData("Slate")]
    [InlineData("Berlin")]
    [InlineData("Ion")]
    public void EachPreset_RoundTripsThroughTheThemePart(string name)
    {
        var theme = DocumentTheme.FindByName(name)!;

        var reloaded = RoundTrip(DocumentWithTheme(theme));

        reloaded.Theme.Should().BeSameAs(theme);
    }

    [Fact]
    public void DefaultDocument_AlwaysEmitsAThemePart_WithOverrideAndRelationship()
    {
        var docx = WriteBytes(DocumentWithTheme(DocumentTheme.Default));

        // The part itself is present (the writer always emits theme1.xml, mirroring real Word documents).
        HasEntry(docx, "word/theme/theme1.xml").Should().BeTrue();

        // A content-type Override declares the theme content type.
        var overrides = EntryXml(docx, "[Content_Types].xml").Root!.Elements(Ct + "Override");
        overrides.Should().Contain(o =>
            o.Attribute("PartName")!.Value == "/word/theme/theme1.xml"
            && o.Attribute("ContentType")!.Value == ThemeContentType);

        // A "theme" relationship points at the part.
        var rels = EntryXml(docx, "word/_rels/document.xml.rels").Root!.Elements(Rel + "Relationship");
        rels.Should().Contain(r =>
            r.Attribute("Type")!.Value == ThemeRelType
            && r.Attribute("Target")!.Value.EndsWith("theme/theme1.xml"));
    }

    [Fact]
    public void ThemePart_CarriesClrSchemeFontSchemeAndFmtScheme()
    {
        var berlin = DocumentTheme.FindByName("Berlin")!;
        var theme = EntryXml(WriteBytes(DocumentWithTheme(berlin)), "word/theme/theme1.xml");
        var elements = theme.Root!.Element(A + "themeElements")!;

        elements.Element(A + "clrScheme").Should().NotBeNull();
        elements.Element(A + "fontScheme").Should().NotBeNull();
        elements.Element(A + "fmtScheme").Should().NotBeNull();

        // accent1..3 carry the preset's palette colours (bare uppercase RRGGBB).
        var clr = elements.Element(A + "clrScheme")!;
        string Accent(string slot) => clr.Element(A + slot)!.Element(A + "srgbClr")!.Attribute("val")!.Value;
        Accent("accent1").Should().Be("C00000");
        Accent("accent2").Should().Be("D2691E");
        Accent("accent3").Should().Be("8B2500");

        // The font scheme records the preset's heading (major) and body (minor) faces.
        var fonts = elements.Element(A + "fontScheme")!;
        string Latin(string font) => fonts.Element(A + font)!.Element(A + "latin")!.Attribute("typeface")!.Value;
        Latin("majorFont").Should().Be(berlin.HeadingFont);
        Latin("minorFont").Should().Be(berlin.BodyFont);

        // The format scheme has at least one fill/line/effect/bg entry (Word requires three of each).
        var fmt = elements.Element(A + "fmtScheme")!;
        fmt.Element(A + "fillStyleLst")!.Elements().Should().NotBeEmpty();
        fmt.Element(A + "lnStyleLst")!.Elements().Should().NotBeEmpty();
        fmt.Element(A + "effectStyleLst")!.Elements().Should().NotBeEmpty();
        fmt.Element(A + "bgFillStyleLst")!.Elements().Should().NotBeEmpty();
    }

    [Fact]
    public void FontSetThemeCombination_RoundTripsThroughThemeFontScheme()
    {
        var doc = DocumentWithTheme(DocumentTheme.FindByName("Berlin")!);
        DocumentFontSet.Apply(doc, DocumentFontSet.FindByName("Georgia")!);

        var reloaded = RoundTrip(doc);

        reloaded.Theme.Name.Should().Be("Custom");
        reloaded.Theme.PrimaryColorHex.Should().Be("#C00000");
        reloaded.Theme.HeadingColorHex.Should().Be("#D2691E");
        reloaded.Theme.HeadingFont.Should().Be("Georgia");
        reloaded.Theme.BodyFont.Should().Be("Georgia");
    }

    [Fact]
    public void ForeignTheme_PreservesReadableThemeDataAsCustom_OnRead()
    {
        // A document whose theme part FreeW did not write (unrecognised accents/fonts) reads back as the
        // default Office preset, while the rest of the document still round-trips.
        var docx = WriteBytes(DocumentWithTheme(DocumentTheme.FindByName("Ion")!));

        // Tamper with the accent colours so no preset matches.
        using var stream = new MemoryStream();
        using (var src = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read))
        using (var dst = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in src.Entries)
            {
                var copy = dst.CreateEntry(entry.FullName);
                using var input = entry.Open();
                using var output = copy.Open();
                if (entry.FullName == "word/theme/theme1.xml")
                {
                    var xml = XDocument.Load(input);
                    var clr = xml.Root!.Element(A + "themeElements")!.Element(A + "clrScheme")!;
                    clr.Element(A + "accent1")!.Element(A + "srgbClr")!.SetAttributeValue("val", "ABCDEF");
                    xml.Save(output);
                }
                else
                {
                    input.CopyTo(output);
                }
            }
        }

        stream.Position = 0;
        var theme = DocxReader.Read(stream).Theme;
        theme.Name.Should().Be("Custom");
        theme.PrimaryColorHex.Should().Be("#ABCDEF");
        theme.HeadingFont.Should().Be(DocumentTheme.FindByName("Ion")!.HeadingFont);
    }
}
