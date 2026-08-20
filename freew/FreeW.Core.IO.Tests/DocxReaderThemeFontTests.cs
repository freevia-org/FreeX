using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Coverage for DocxReader resolving <c>w:rFonts</c>'s theme-token attributes
/// (<c>w:asciiTheme</c>/<c>w:hAnsiTheme</c>/<c>w:eastAsiaTheme</c>/<c>w:cstheme</c>) against the
/// package's theme part. Real Word documents commonly bind their default body font
/// (<c>w:docDefaults/w:rPrDefault</c>) this way instead of via a literal <c>w:ascii</c>, so a reader
/// that only reads the literal attributes silently substitutes FreeW's hardcoded "Calibri" fallback
/// for the document's real theme font — both on screen and, because the writer bakes
/// <see cref="TextDocument.DefaultRun"/> back out as a literal font, permanently on save/reopen too.
/// </summary>
public sealed class DocxReaderThemeFontTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static byte[] Write(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static TextDocument Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return DocxReader.Read(stream);
    }

    /// <summary>
    /// Rewrites word/styles.xml's <c>w:docDefaults/w:rPrDefault/w:rPr/w:rFonts</c> element to whatever the
    /// caller supplies, leaving every other part (including word/theme/theme1.xml) untouched.
    /// </summary>
    private static byte[] ReplaceDocDefaultRFonts(byte[] docx, XElement newRFonts)
    {
        using var input = new MemoryStream(docx);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var dest = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                var copy = dest.CreateEntry(entry.FullName);
                if (entry.FullName == "word/styles.xml")
                {
                    using var entryStream = entry.Open();
                    var stylesXml = XDocument.Load(entryStream);
                    var rFonts = stylesXml.Root!
                        .Element(W + "docDefaults")!
                        .Element(W + "rPrDefault")!
                        .Element(W + "rPr")!
                        .Element(W + "rFonts")!;
                    rFonts.ReplaceWith(newRFonts);
                    using var writer = copy.Open();
                    stylesXml.Save(writer);
                }
                else
                {
                    using var src = entry.Open();
                    using var dst = copy.Open();
                    src.CopyTo(dst);
                }
            }
        }

        return output.ToArray();
    }

    private static byte[] DocumentWithThemeAndDocDefaultRFonts(XElement rFonts)
    {
        // "Berlin" gives the theme a body font ("Trebuchet MS") distinct from FreeW's hardcoded
        // "Calibri" model default, so a reader that silently falls back to the hardcoded default is
        // distinguishable from one that actually resolved the theme.
        var doc = new TextDocument { Theme = DocumentTheme.FindByName("Berlin")! };
        doc.Blocks.Add(new Paragraph("Body"));

        return ReplaceDocDefaultRFonts(Write(doc), rFonts);
    }

    [Fact]
    public void DocDefaults_AsciiThemeOnly_ResolvesToTheThemesMinorLatinFont()
    {
        // The standard shape of Word's own blank-document docDefaults: only theme tokens, no literal
        // w:ascii/w:hAnsi at all.
        var rFonts = new XElement(W + "rFonts",
            new XAttribute(W + "asciiTheme", "minorHAnsi"),
            new XAttribute(W + "hAnsiTheme", "minorHAnsi"),
            new XAttribute(W + "eastAsiaTheme", "minorEastAsia"),
            new XAttribute(W + "cstheme", "minorBidi"));

        var reloaded = Read(DocumentWithThemeAndDocDefaultRFonts(rFonts));

        // Sanity: FreeW's separate theme-inference pass already got this right.
        reloaded.Theme.BodyFont.Should().Be("Trebuchet MS");

        // The bug: DefaultRun.FontFamily must also be the theme's real body font, not the hardcoded
        // "Calibri" fallback that TextDocument.DefaultRun starts from.
        reloaded.DefaultRun.FontFamily.Should().Be("Trebuchet MS");
    }

    [Fact]
    public void DocDefaults_MajorThemeToken_ResolvesToTheThemesMajorLatinFont()
    {
        var rFonts = new XElement(W + "rFonts",
            new XAttribute(W + "asciiTheme", "majorHAnsi"),
            new XAttribute(W + "hAnsiTheme", "majorHAnsi"));

        var reloaded = Read(DocumentWithThemeAndDocDefaultRFonts(rFonts));

        reloaded.DefaultRun.FontFamily.Should().Be(DocumentTheme.FindByName("Berlin")!.HeadingFont);
    }

    /// <summary>
    /// Sibling no-regression case: a literal <c>w:ascii</c> alongside a theme token (the shape of a run
    /// that explicitly overrides the theme font) must still win — the fix only fills in the font when no
    /// literal is present, so it must never override an explicit literal choice.
    /// </summary>
    [Fact]
    public void DocDefaults_LiteralAsciiAlongsideThemeToken_LiteralWins()
    {
        var rFonts = new XElement(W + "rFonts",
            new XAttribute(W + "ascii", "Arial"),
            new XAttribute(W + "hAnsi", "Arial"),
            new XAttribute(W + "asciiTheme", "minorHAnsi"),
            new XAttribute(W + "hAnsiTheme", "minorHAnsi"));

        var reloaded = Read(DocumentWithThemeAndDocDefaultRFonts(rFonts));

        reloaded.DefaultRun.FontFamily.Should().Be("Arial");
        reloaded.Theme.BodyFont.Should().Be("Trebuchet MS");
    }

    /// <summary>
    /// Sibling no-regression case: a document with no theme part at all (or an unresolvable theme
    /// token) must not throw and must fall back to FreeW's existing behaviour (the hardcoded model
    /// default), exactly as before this fix.
    /// </summary>
    [Fact]
    public void DocDefaults_ThemeTokenWithNoThemePart_FallsBackWithoutThrowing()
    {
        var rFonts = new XElement(W + "rFonts",
            new XAttribute(W + "asciiTheme", "minorHAnsi"));

        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        var bytes = Write(doc);

        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var dest = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                // Drop the theme part and its relationship/content-type entries so the package has no
                // theme at all, then apply the theme-token-only docDefaults rFonts.
                if (entry.FullName == "word/theme/theme1.xml")
                    continue;

                var copy = dest.CreateEntry(entry.FullName);
                if (entry.FullName == "word/styles.xml")
                {
                    using var entryStream = entry.Open();
                    var stylesXml = XDocument.Load(entryStream);
                    var existing = stylesXml.Root!
                        .Element(W + "docDefaults")!
                        .Element(W + "rPrDefault")!
                        .Element(W + "rPr")!
                        .Element(W + "rFonts")!;
                    existing.ReplaceWith(rFonts);
                    using var writer = copy.Open();
                    stylesXml.Save(writer);
                }
                else
                {
                    using var src = entry.Open();
                    using var dst = copy.Open();
                    src.CopyTo(dst);
                }
            }
        }

        var act = () => Read(output.ToArray());
        act.Should().NotThrow();
        act().DefaultRun.FontFamily.Should().Be("Calibri");
    }
}
