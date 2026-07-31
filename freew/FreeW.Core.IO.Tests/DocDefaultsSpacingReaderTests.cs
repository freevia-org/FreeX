using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Reader coverage for the document default paragraph spacing (w:docDefaults) and automatic spacing
/// (w:beforeAutospacing/w:afterAutospacing). FreeW previously ignored both, rendering every paragraph at
/// 0 space-after regardless of the document — which drifts vs Word down the page.
/// </summary>
public class DocDefaultsSpacingReaderTests
{
    private const string Wns = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace W = Wns;

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }

    private static TextDocument Read(string bodyXml, string? docDefaultsSpacing = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "word/document.xml", $"<w:document xmlns:w=\"{Wns}\"><w:body>{bodyXml}</w:body></w:document>");
            var dd = docDefaultsSpacing is null
                ? ""
                : $"<w:docDefaults><w:pPrDefault><w:pPr>{docDefaultsSpacing}</w:pPr></w:pPrDefault></w:docDefaults>";
            Add(zip, "word/styles.xml", $"<w:styles xmlns:w=\"{Wns}\">{dd}</w:styles>");
        }
        ms.Position = 0;
        return DocxReader.Read(ms);
    }

    private static ParagraphFormatting FirstFormatting(TextDocument doc) =>
        doc.Blocks.OfType<Paragraph>().First().Formatting;

    private static byte[] Write(TextDocument document)
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

    [Fact]
    public void DocDefaultSpacing_AppliesToParagraphWithoutOwnSpacing()
    {
        var doc = Read(
            "<w:p><w:r><w:t>body</w:t></w:r></w:p>",
            docDefaultsSpacing: "<w:spacing w:after=\"200\" w:line=\"276\" w:lineRule=\"auto\"/>");
        var f = FirstFormatting(doc);
        Assert.Equal(10, f.SpaceAfterPt, 1);            // after=200 dxa = 10 pt
        Assert.Equal(1.15, f.LineSpacing, 2);           // line=276 / 240
    }

    [Fact]
    public void ParagraphOwnSpacing_WinsOverDocDefault()
    {
        var doc = Read(
            "<w:p><w:pPr><w:spacing w:after=\"40\"/></w:pPr><w:r><w:t>body</w:t></w:r></w:p>",
            docDefaultsSpacing: "<w:spacing w:after=\"200\"/>");
        Assert.Equal(2, FirstFormatting(doc).SpaceAfterPt, 1); // own after=40 dxa = 2 pt
    }

    [Fact]
    public void NoDocDefaults_ParagraphWithoutSpacing_StaysZero()
    {
        // Documents without docDefaults keep the prior behaviour (no extra space-after).
        var doc = Read("<w:p><w:r><w:t>body</w:t></w:r></w:p>");
        Assert.Equal(0, FirstFormatting(doc).SpaceAfterPt, 1);
    }

    [Fact]
    public void ImportedDocument_MarksMissingLineRuleForWordApplicationDefault()
    {
        var doc = Read("<w:p><w:r><w:t>body</w:t></w:r></w:p>");
        var formatting = FirstFormatting(doc);

        doc.UseWordApplicationDefaultLineSpacing.Should().BeTrue();
        formatting.LineSpacing.Should().Be(1.15);
        formatting.LineSpacingIsSet.Should().BeFalse();
    }

    [Fact]
    public void ExplicitSingleLineRule_RemainsAuthoritativeOverWordApplicationDefault()
    {
        var doc = Read("<w:p><w:pPr><w:spacing w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:t>body</w:t></w:r></w:p>");
        var formatting = FirstFormatting(doc);

        doc.UseWordApplicationDefaultLineSpacing.Should().BeTrue();
        formatting.LineSpacing.Should().Be(1.0);
        formatting.LineSpacingIsSet.Should().BeTrue();
    }

    [Fact]
    public void Autospacing_OverridesLiteralValue()
    {
        // w:afterAutospacing means Word ignores the literal after value and uses automatic (~one line)
        // spacing; the reader applies the auto approximation rather than the tiny literal 100 dxa = 5 pt.
        var doc = Read("<w:p><w:pPr><w:spacing w:after=\"100\" w:afterAutospacing=\"1\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var formatting = FirstFormatting(doc);
        Assert.True(formatting.SpaceAfterPt >= 12, "auto spacing should be ~one line, not the 5pt literal");
        Assert.True(formatting.AfterAutoSpacing);
    }

    [Fact]
    public void ConsecutiveAutospacingParagraphs_SuppressSpaceBetween()
    {
        // Word suppresses automatic spacing between two consecutive auto-spaced paragraphs; the auto space
        // survives only before the first and after the last of the block.
        const string auto = "<w:pPr><w:spacing w:before=\"100\" w:beforeAutospacing=\"1\" w:after=\"100\" w:afterAutospacing=\"1\"/></w:pPr>";
        var doc = Read($"<w:p>{auto}<w:r><w:t>one</w:t></w:r></w:p><w:p>{auto}<w:r><w:t>two</w:t></w:r></w:p>");
        var paras = doc.Blocks.OfType<Paragraph>().ToList();

        Assert.Equal(0, paras[0].Formatting.SpaceAfterPt, 1);    // between: suppressed
        Assert.Equal(0, paras[1].Formatting.SpaceBeforePt, 1);   // between: suppressed
        Assert.True(paras[0].Formatting.SpaceBeforePt >= 12);    // block start: kept
        Assert.True(paras[1].Formatting.SpaceAfterPt >= 12);     // block end: kept
        Assert.All(paras, paragraph =>
        {
            Assert.True(paragraph.Formatting.BeforeAutoSpacing);
            Assert.True(paragraph.Formatting.AfterAutoSpacing);
        });
    }

    [Fact]
    public void Autospacing_RoundTripsAsAutomaticTokenWithoutConflictingNumericAxis()
    {
        var source = Read("<w:p><w:pPr><w:spacing w:before=\"120\" w:beforeAutospacing=\"on\" w:after=\"100\" w:afterAutospacing=\"1\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");

        var bytes = Write(source);
        var spacing = EntryXml(bytes, "word/document.xml").Descendants(W + "spacing").Single();

        spacing.Attribute(W + "beforeAutospacing")?.Value.Should().Be("1");
        spacing.Attribute(W + "afterAutospacing")?.Value.Should().Be("1");
        spacing.Attribute(W + "before").Should().BeNull();
        spacing.Attribute(W + "after").Should().BeNull();

        var reread = DocxReader.Read(new MemoryStream(bytes));
        var formatting = FirstFormatting(reread);
        formatting.BeforeAutoSpacing.Should().BeTrue();
        formatting.AfterAutoSpacing.Should().BeTrue();
        formatting.SpaceBeforePt.Should().BeGreaterThanOrEqualTo(12);
        formatting.SpaceAfterPt.Should().BeGreaterThanOrEqualTo(12);
    }

    [Fact]
    public void NumericSpacing_RemainsNumericWithoutAutomaticTokens()
    {
        var document = new TextDocument();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                SpaceBeforePt = 6,
                SpaceAfterPt = 10,
                SpaceBeforeIsSet = true,
                SpaceAfterIsSet = true
            }
        };
        paragraph.Runs.Add(new Run("body"));
        document.Blocks.Add(paragraph);

        var spacing = EntryXml(Write(document), "word/document.xml").Descendants(W + "spacing").Single();

        spacing.Attribute(W + "before")?.Value.Should().Be("120");
        spacing.Attribute(W + "after")?.Value.Should().Be("200");
        spacing.Attribute(W + "beforeAutospacing").Should().BeNull();
        spacing.Attribute(W + "afterAutospacing").Should().BeNull();
    }

    [Fact]
    public void StyleAndDocDefaultAutospacing_RoundTripAsAutomaticTokens()
    {
        var document = new TextDocument
        {
            DefaultParagraph = ParagraphFormatting.Default with
            {
                BeforeAutoSpacing = true,
                AfterAutoSpacing = true,
                SpaceBeforePt = 14,
                SpaceAfterPt = 14
            }
        };
        document.Styles["AutoBody"] = new DocumentStyle
        {
            Id = "AutoBody",
            Name = "Auto Body",
            Paragraph = ParagraphFormatting.Default with
            {
                AfterAutoSpacing = true,
                SpaceAfterPt = 14,
                SpaceAfterIsSet = true
            }
        };
        var paragraph = new Paragraph { StyleId = "AutoBody" };
        paragraph.Runs.Add(new Run("body"));
        document.Blocks.Add(paragraph);

        var bytes = Write(document);
        var styles = EntryXml(bytes, "word/styles.xml");
        var defaultSpacing = styles.Descendants(W + "docDefaults").Descendants(W + "spacing").Single();
        var styleSpacing = styles.Descendants(W + "style").Single(style => style.Attribute(W + "styleId")?.Value == "AutoBody")
            .Descendants(W + "spacing").Single();

        defaultSpacing.Attribute(W + "beforeAutospacing")?.Value.Should().Be("1");
        defaultSpacing.Attribute(W + "afterAutospacing")?.Value.Should().Be("1");
        defaultSpacing.Attribute(W + "before").Should().BeNull();
        defaultSpacing.Attribute(W + "after").Should().BeNull();
        styleSpacing.Attribute(W + "afterAutospacing")?.Value.Should().Be("1");
        styleSpacing.Attribute(W + "after").Should().BeNull();

        var reread = DocxReader.Read(new MemoryStream(bytes));
        reread.DefaultParagraph.BeforeAutoSpacing.Should().BeTrue();
        reread.DefaultParagraph.AfterAutoSpacing.Should().BeTrue();
        reread.Styles["AutoBody"].Paragraph.AfterAutoSpacing.Should().BeTrue();
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("<w:contextualSpacing/>", true)]
    [InlineData("<w:contextualSpacing w:val=\"0\"/>", false)]
    public void ContextualSpacing_RetainsAbsentEnabledAndExplicitOffStates(string token, bool? expected)
    {
        var source = Read($"<w:p><w:pPr>{token}</w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        FirstFormatting(source).ContextualSpacing.Should().Be(expected);

        var bytes = Write(source);
        if (expected is null)
        {
            EntryXml(bytes, "word/document.xml").Descendants(W + "contextualSpacing").Should().BeEmpty();
        }
        else
        {
            var pPr = EntryXml(bytes, "word/document.xml").Descendants(W + "pPr").Single();
            var contextual = pPr.Element(W + "contextualSpacing");
            contextual.Should().NotBeNull();
            contextual!.Attribute(W + "val")?.Value.Should().Be(expected.Value ? null : "0");
        }

        FirstFormatting(DocxReader.Read(new MemoryStream(bytes))).ContextualSpacing.Should().Be(expected);
    }

    [Fact]
    public void StyleAndDocDefaultContextualSpacing_RoundTrip()
    {
        var document = new TextDocument
        {
            DefaultParagraph = ParagraphFormatting.Default with { ContextualSpacing = true }
        };
        document.Styles["ContextBody"] = new DocumentStyle
        {
            Id = "ContextBody",
            Name = "Context Body",
            Paragraph = ParagraphFormatting.Default with { ContextualSpacing = false }
        };
        var paragraph = new Paragraph { StyleId = "ContextBody" };
        paragraph.Runs.Add(new Run("body"));
        document.Blocks.Add(paragraph);

        var bytes = Write(document);
        var styles = EntryXml(bytes, "word/styles.xml");
        var defaultContextual = styles.Descendants(W + "docDefaults").Descendants(W + "contextualSpacing").Single();
        var styleContextual = styles.Descendants(W + "style")
            .Single(style => style.Attribute(W + "styleId")?.Value == "ContextBody")
            .Descendants(W + "contextualSpacing").Single();

        defaultContextual.Attribute(W + "val").Should().BeNull();
        styleContextual.Attribute(W + "val")?.Value.Should().Be("0");

        var reread = DocxReader.Read(new MemoryStream(bytes));
        reread.DefaultParagraph.ContextualSpacing.Should().BeTrue();
        reread.Styles["ContextBody"].Paragraph.ContextualSpacing.Should().BeFalse();
    }
}
