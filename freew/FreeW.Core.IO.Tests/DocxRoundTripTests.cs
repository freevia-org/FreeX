using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class DocxRoundTripTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    private static TextDocument RoundTrip(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    /// <summary>Writes the document and parses word/document.xml as an XDocument for structural assertions.</summary>
    private static XDocument WriteDocumentXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(entry);
    }

    private static XDocument? WriteSettingsXml(TextDocument document)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.GetEntry("word/settings.xml");
        if (entry is null)
            return null;
        using var reader = entry.Open();
        return XDocument.Load(reader);
    }

    private static TextDocument ReadHandAuthoredDocx(string bodyXml, string? documentRelsXml = null, string? settingsXml = null)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(xml);
            }

            Add("word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>{bodyXml}</w:body>
                </w:document>
                """);
            if (documentRelsXml is not null)
                Add("word/_rels/document.xml.rels", documentRelsXml);
            if (settingsXml is not null)
                Add("word/settings.xml", settingsXml);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    [Fact]
    public void RunProperties_EmittedInCanonicalSchemaOrder()
    {
        // A run carrying many formatting facets at once must emit w:rPr children in CT_RPr schema
        // order, else Word's strict validator rejects it (the order-independent reader hides this).
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("x", new RunFormatting
        {
            FontFamily = "Arial",
            Bold = true,
            Italic = true,
            AllCaps = true,
            SmallCaps = true,
            Strikethrough = true,
            Hidden = true,
            ColorHex = "#112233",
            FontSizePt = 14,
            Underline = true,
            HighlightColorHex = "#FFFF00",
            VerticalAlign = VerticalAlign.Superscript
        }));
        doc.Blocks.Add(paragraph);

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var rPr = WriteDocumentXml(doc).Descendants(ns + "rPr").First();
        var names = rPr.Elements().Select(e => e.Name.LocalName).ToList();

        // Canonical EG_RPrBase order for the elements FreeW emits.
        var canonical = new[] { "rFonts", "b", "i", "caps", "smallCaps", "strike", "vanish", "color", "sz", "szCs", "highlight", "u", "shd", "vertAlign" };
        var expected = canonical.Where(names.Contains).ToList();
        names.Should().Equal(expected);
    }

    [Fact]
    public void FootnoteMarker_PreservesRunFormatting()
    {
        var doc = new TextDocument();
        doc.Footnotes[1] = new Footnote(1, "note");
        var body = new Paragraph();
        body.Runs.Add(new Run("see "));
        body.Runs.Add(Run.FootnoteReference(1, new RunFormatting { Bold = true, ColorHex = "#C00000" }));
        doc.Blocks.Add(body);

        var marker = RoundTrip(doc).Paragraphs.First().Runs.Single(r => r.FootnoteId == 1);
        marker.Formatting.Bold.Should().BeTrue();
        marker.Formatting.ColorHex.Should().Be("#C00000");
        marker.Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    }

    [Fact]
    public void Paragraphs_And_Text_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Hello world"));
        doc.Blocks.Add(new Paragraph("Second paragraph"));

        var result = RoundTrip(doc);

        result.Paragraphs.Select(p => p.PlainText).Should().Equal("Hello world", "Second paragraph");
    }

    [Fact]
    public void ParagraphStyleId_RoundTrips_AndBuiltInStylesPersist()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Add(new Paragraph("A heading") { StyleId = "Heading2" });
        doc.Blocks.Add(new Paragraph("A subtitle") { StyleId = "Subtitle" });
        doc.Blocks.Add(new Paragraph("A quote") { StyleId = "Quote" });

        var result = RoundTrip(doc);

        // The paragraphs keep their StyleId reference.
        var styled = result.Paragraphs.Where(p => p.StyleId is not null).ToList();
        styled.Select(p => p.StyleId).Should().Contain(new[] { "Heading2", "Subtitle", "Quote" });

        // The new built-in styles survive in styles.xml (write -> read keeps the catalog entries).
        result.Styles.Keys.Should().Contain(new[]
        {
            "Heading2", "Heading3", "Subtitle", "Quote"
        });
        result.Styles["Heading2"].Name.Should().Be("Heading 2");
        result.Styles["Subtitle"].Run.Italic.Should().BeTrue();
    }

    [Fact]
    public void CustomStyle_CreatedViaStyleManager_RoundTrips()
    {
        var doc = TextDocument.CreateEmpty();

        // A custom style created through the pure StyleManager ops round-trips via the existing
        // styles.xml writer (no docx I/O changes needed) and the paragraph keeps its StyleId.
        var custom = StyleManager.CreateStyle(
            doc, "My Callout", basedOnId: "Normal",
            new RunFormatting { Bold = true, Italic = true, FontSizePt = 13, ColorHex = "#C00000" },
            new ParagraphFormatting { Alignment = TextAlignment.Center, SpaceBeforePt = 6 });

        doc.Blocks.Add(new Paragraph("Styled body") { StyleId = custom.Id });

        var result = RoundTrip(doc);

        // The custom style survives in the catalog (styles.xml) with its name, based-on chain and run
        // (character) formatting — the same properties the existing styles writer persists for built-ins.
        result.Styles.Should().ContainKey(custom.Id);
        var read = result.Styles[custom.Id];
        read.Name.Should().Be("My Callout");
        read.BasedOnStyleId.Should().Be("Normal");
        read.Run.Bold.Should().BeTrue();
        read.Run.Italic.Should().BeTrue();
        read.Run.FontSizePt.Should().Be(13);
        read.Run.ColorHex.Should().Be("#C00000");

        // The paragraph still references the custom style id after the round-trip.
        result.Paragraphs.Should().ContainSingle(p => p.StyleId == custom.Id);
    }

    [Fact]
    public void RunFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("styled", new RunFormatting
        {
            Bold = true,
            Italic = true,
            Underline = true,
            Strikethrough = true,
            FontFamily = "Arial",
            FontSizePt = 14,
            ColorHex = "#C0504D"
        }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.Bold.Should().BeTrue();
        formatting.Italic.Should().BeTrue();
        formatting.Underline.Should().BeTrue();
        formatting.Strikethrough.Should().BeTrue();
        formatting.FontFamily.Should().Be("Arial");
        formatting.FontSizePt.Should().Be(14);
        formatting.ColorHex.Should().Be("#C0504D");
    }

    [Fact]
    public void ThemeLinkedRunColor_RetainsSourceAttributesUntilFixedColorChanges()
    {
        var document = ReadHandAuthoredDocx(
            "<w:p><w:r><w:rPr><w:color w:val=\"7F6000\" w:themeColor=\"accent4\" " +
            "w:themeTint=\"99\" w:themeShade=\"80\"/></w:rPr><w:t>Theme</w:t></w:r></w:p>");
        var run = document.Paragraphs.Single().Runs.Single();

        run.Formatting.ColorHex.Should().Be("#7F6000");
        run.Formatting.ThemeColor.Should().Be(new WordThemeColor("accent4", "7F6000", "99", "80"));

        var color = WriteDocumentXml(document).Descendants(
            XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "color").Single();
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "val")!.Value.Should().Be("7F6000");
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeColor")!.Value.Should().Be("accent4");
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeTint")!.Value.Should().Be("99");
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeShade")!.Value.Should().Be("80");

        run.Formatting = run.Formatting with { ColorHex = "#FF0000" };
        color = WriteDocumentXml(document).Descendants(
            XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "color").Single();
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "val")!.Value.Should().Be("FF0000");
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeColor").Should().BeNull();
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeTint").Should().BeNull();
        color.Attribute(XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "themeShade").Should().BeNull();
    }

    [Fact]
    public void Superscript_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Superscript }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);
    }

    [Fact]
    public void Subscript_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Subscript }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.VerticalAlign.Should().Be(VerticalAlign.Subscript);
    }

    [Fact]
    public void SmallCaps_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("heading", new RunFormatting { SmallCaps = true }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.SmallCaps.Should().BeTrue();
        formatting.AllCaps.Should().BeFalse();
        formatting.VerticalAlign.Should().Be(VerticalAlign.Baseline);
    }

    [Fact]
    public void AllCaps_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("title", new RunFormatting { AllCaps = true }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.AllCaps.Should().BeTrue();
        formatting.SmallCaps.Should().BeFalse();
    }

    [Fact]
    public void RunHighlight_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("highlighted", new RunFormatting { HighlightColorHex = "#FFFF00" }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.HighlightColorHex.Should().Be("#FFFF00");
    }

    [Fact]
    public void RunForegroundColor_RoundTrips()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("coloured", new RunFormatting { ColorHex = "#2F5496" }));
        doc.Blocks.Add(paragraph);

        var formatting = RoundTrip(doc).Paragraphs.First().Runs[0].Formatting;

        formatting.ColorHex.Should().Be("#2F5496");
        formatting.HighlightColorHex.Should().BeNull();
    }

    [Fact]
    public void ParagraphFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("p")
        {
            Formatting = ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Center,
                SpaceBeforePt = 12,
                IndentLeftPt = 36
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Alignment.Should().Be(TextAlignment.Center);
        formatting.SpaceBeforePt.Should().Be(12);
        formatting.IndentLeftPt.Should().Be(36);
    }

    [Fact]
    public void TabStops_RoundTrip_WithAlignmentsAndPositions()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("tabbed")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(36, TabStopAlignment.Left),
                    new TabStop(108, TabStopAlignment.Center),
                    new TabStop(216, TabStopAlignment.Right),
                    new TabStop(324, TabStopAlignment.Decimal)
                ]
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.TabStops.Should().Equal(
            new TabStop(36, TabStopAlignment.Left),
            new TabStop(108, TabStopAlignment.Center),
            new TabStop(216, TabStopAlignment.Right),
            new TabStop(324, TabStopAlignment.Decimal));
    }

    [Fact]
    public void PlainParagraph_HasEmptyTabStops()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.TabStops.Should().BeEmpty();
    }

    [Fact]
    public void TabStops_RoundTrip_WithEachLeaderKind()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("leaders")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(36, TabStopAlignment.Left, TabLeader.Dots),
                    new TabStop(108, TabStopAlignment.Center, TabLeader.Dashes),
                    new TabStop(216, TabStopAlignment.Right, TabLeader.Underline),
                    new TabStop(324, TabStopAlignment.Decimal, TabLeader.None)
                ]
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        // TabStop is a record, so structural equality covers position + alignment + leader together.
        formatting.TabStops.Should().Equal(
            new TabStop(36, TabStopAlignment.Left, TabLeader.Dots),
            new TabStop(108, TabStopAlignment.Center, TabLeader.Dashes),
            new TabStop(216, TabStopAlignment.Right, TabLeader.Underline),
            new TabStop(324, TabStopAlignment.Decimal, TabLeader.None));
    }

    [Fact]
    public void TabStopClear_ReadsAndSurvivesReopenRoundTrip()
    {
        var loaded = ReadHandAuthoredDocx(
            """
            <w:p>
              <w:pPr>
                <w:tabs><w:tab w:val="clear" w:pos="1440"/></w:tabs>
              </w:pPr>
              <w:r><w:t>cleared inherited tab</w:t></w:r>
            </w:p>
            """);

        var read = loaded.Paragraphs.Single().Formatting.TabStops.Single();
        read.PositionPt.Should().BeApproximately(72, 0.001);
        read.IsClear.Should().BeTrue();
        read.Leader.Should().Be(TabLeader.None);

        var reopened = RoundTrip(loaded).Paragraphs.Single().Formatting.TabStops.Single();
        reopened.Should().Be(read);
    }

    [Fact]
    public void TabStopClear_WritesClearTokenPositionAndNoLeader()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("clear inherited stop")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(
                        72,
                        TabStopAlignment.Right,
                        TabLeader.Dots,
                        IsClear: true),
                ],
            },
        });

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var tab = WriteDocumentXml(doc).Descendants(ns + "tab").Single();

        tab.Attribute(ns + "val")!.Value.Should().Be("clear");
        tab.Attribute(ns + "pos")!.Value.Should().Be("1440");
        tab.Attribute(ns + "leader").Should().BeNull();
    }

    [Fact]
    public void DefaultTabStop_RoundTripsThroughSettings()
    {
        var doc = new TextDocument();
        doc.Page.DefaultTabStopPt = 42;
        doc.Blocks.Add(new Paragraph("tab defaults"));

        var roundTripped = RoundTrip(doc);

        roundTripped.Page.DefaultTabStopPt.Should().Be(42);
    }

    [Fact]
    public void DefaultTabStop_EmitsSettingsOnlyWhenChanged()
    {
        var unchanged = new TextDocument();
        unchanged.Blocks.Add(new Paragraph("plain"));

        WriteSettingsXml(unchanged).Should().BeNull();

        var changed = new TextDocument();
        changed.Page.DefaultTabStopPt = 42;
        changed.Blocks.Add(new Paragraph("custom"));

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var settings = WriteSettingsXml(changed)!.Root!;

        settings.Element(ns + "defaultTabStop")!.Attribute(ns + "val")!.Value.Should().Be("840");
    }

    [Fact]
    public void DefaultTabStop_ReadsWordAuthoredSettings()
    {
        var result = ReadHandAuthoredDocx(
            "<w:p><w:r><w:t>tab defaults</w:t></w:r></w:p>",
            settingsXml:
            """
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:defaultTabStop w:val="708"/>
            </w:settings>
            """);

        result.Page.DefaultTabStopPt.Should().Be(35.4);
    }

    [Fact]
    public void TabStops_WithoutLeader_DefaultToNone()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain stop")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(180, TabStopAlignment.Right)]
            }
        });

        var stop = RoundTrip(doc).Paragraphs.First().Formatting.TabStops.Single();

        stop.Leader.Should().Be(TabLeader.None);
    }

    [Fact]
    public void TabStopLeader_EmitsWLeaderAttribute_OnlyWhenSet()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("leaders")
        {
            Formatting = ParagraphFormatting.Default with
            {
                TabStops =
                [
                    new TabStop(36, TabStopAlignment.Left, TabLeader.Dots),
                    new TabStop(108, TabStopAlignment.Left, TabLeader.None)
                ]
            }
        });

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var tabs = WriteDocumentXml(doc).Descendants(ns + "tab").ToList();

        tabs.Should().HaveCount(2);
        tabs[0].Attribute(ns + "leader")!.Value.Should().Be("dot");
        // A None leader writes no w:leader attribute, so leaderless stops stay byte-identical to before.
        tabs[1].Attribute(ns + "leader").Should().BeNull();
    }

    [Fact]
    public void ParagraphBorder_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bordered")
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder("#FF0000", 1.5)
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().NotBeNull();
        formatting.Border!.ColorHex.Should().Be("#FF0000");
        formatting.Border.WidthPt.Should().BeApproximately(1.5, 0.001);
        formatting.ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void PageBorder_RoundTrips_ColorAndWidth()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("page with border"));
        doc.Page.PageBorder = new PageBorder("#0000FF", 2.0);

        var page = RoundTrip(doc).Page;

        page.PageBorder.Should().NotBeNull();
        page.PageBorder!.ColorHex.Should().Be("#0000FF");
        page.PageBorder.WidthPt.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void PageBorder_RoundTrips_TextOffsetAndNonDefaultSpace()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("page border offset"));
        doc.Page.PageBorder = new PageBorder("#1F4E79", 2.25)
        {
            OffsetFrom = PageBorderOffsetFrom.Text,
            SpacePt = 11,
        };

        var page = RoundTrip(doc).Page;

        page.PageBorder.Should().NotBeNull();
        page.PageBorder!.OffsetFrom.Should().Be(PageBorderOffsetFrom.Text);
        page.PageBorder.SpacePt.Should().BeApproximately(11, 0.001);

        var word = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var serialized = WriteDocumentXml(doc).Descendants(word + "pgBorders").Single();
        serialized.Attribute(word + "offsetFrom")!.Value.Should().Be("text");
        serialized.Elements().Should().OnlyContain(edge => edge.Attribute(word + "space")!.Value == "11");
    }

    [Fact]
    public void PageBorder_Reader_PreservesHandAuthoredTextOffsetAndSpace()
    {
        var doc = ReadHandAuthoredDocx(
            """
            <w:p><w:r><w:t>page border</w:t></w:r></w:p>
            <w:sectPr><w:pgBorders w:offsetFrom="text"><w:top w:val="single" w:sz="18" w:space="11" w:color="1F4E79" /><w:left w:val="single" w:sz="18" w:space="11" w:color="1F4E79" /><w:bottom w:val="single" w:sz="18" w:space="11" w:color="1F4E79" /><w:right w:val="single" w:sz="18" w:space="11" w:color="1F4E79" /></w:pgBorders></w:sectPr>
            """);

        doc.Page.PageBorder.Should().BeEquivalentTo(new PageBorder("#1F4E79", 2.25)
        {
            OffsetFrom = PageBorderOffsetFrom.Text,
            SpacePt = 11,
        });
    }

    [Fact]
    public void PageBorder_DefaultOffsetMetadata_RemainsWordDefault()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("default page border"));
        doc.Page.PageBorder = new PageBorder("#0000FF", 2.0);

        var word = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var serialized = WriteDocumentXml(doc).Descendants(word + "pgBorders").Single();

        serialized.Attribute(word + "offsetFrom")!.Value.Should().Be("page");
        serialized.Elements().Should().OnlyContain(edge => edge.Attribute(word + "space")!.Value == "24");
    }

    [Fact]
    public void ParagraphBorder_PerEdgeStyleColourAndWidth_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("custom border")
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder("#00B050", 2.25)
                {
                    LineStyle = BorderLineStyle.Dashed,
                    Top = true,
                    Left = false,
                    Bottom = true,
                    Right = false,
                }
            }
        });

        var border = RoundTrip(doc).Paragraphs.First().Formatting.Border;

        border.Should().NotBeNull();
        border!.ColorHex.Should().Be("#00B050");
        border.WidthPt.Should().BeApproximately(2.25, 0.001);
        border.LineStyle.Should().Be(BorderLineStyle.Dashed);
        border.Top.Should().BeTrue();
        border.Left.Should().BeFalse();
        border.Bottom.Should().BeTrue();
        border.Right.Should().BeFalse();
    }

    [Fact]
    public void ParagraphShading_WithPattern_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("shaded")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ShadingColorHex = "#DDDDDD",
                ShadingPattern = ShadingPattern.Pct25,
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ShadingColorHex.Should().Be("#DDDDDD");
        formatting.ShadingPattern.Should().Be(ShadingPattern.Pct25);
    }

    [Fact]
    public void PageBorder_LineStyle_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("page with dotted border"));
        doc.Page.PageBorder = new PageBorder("#7030A0", 3.0) { LineStyle = BorderLineStyle.Dotted };

        var page = RoundTrip(doc).Page;

        page.PageBorder.Should().NotBeNull();
        page.PageBorder!.ColorHex.Should().Be("#7030A0");
        page.PageBorder.WidthPt.Should().BeApproximately(3.0, 0.001);
        page.PageBorder.LineStyle.Should().Be(BorderLineStyle.Dotted);
    }

    [Fact]
    public void DefaultPage_HasNoPageBorderOrWatermark()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        var page = RoundTrip(doc).Page;

        page.PageBorder.Should().BeNull();
        page.Watermark.Should().BeNull();
    }

    [Fact]
    public void LineNumbers_Continuous_RoundTripsModeAndCountBy()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("numbered lines"));
        doc.Page.LineNumberMode = LineNumberMode.Continuous;
        doc.Page.LineNumberCountBy = 5;

        var page = RoundTrip(doc).Page;

        page.LineNumberMode.Should().Be(LineNumberMode.Continuous);
        page.LineNumberCountBy.Should().Be(5);
    }

    [Fact]
    public void LineNumbers_RestartEachPage_RoundTripsModeAndCountBy()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("numbered lines"));
        doc.Page.LineNumberMode = LineNumberMode.RestartEachPage;
        doc.Page.LineNumberCountBy = 2;

        var page = RoundTrip(doc).Page;

        page.LineNumberMode.Should().Be(LineNumberMode.RestartEachPage);
        page.LineNumberCountBy.Should().Be(2);
    }

    [Fact]
    public void LineNumbers_RestartEachSection_RoundTripsExactRestartToken()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("numbered lines"));
        doc.Page.LineNumberMode = LineNumberMode.RestartEachSection;
        doc.Page.LineNumberStartAt = 4;
        doc.Page.LineNumberCountBy = 2;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("w:restart=\"newSection\"");
            documentXml.Should().Contain("w:start=\"4\"");
        }

        stream.Position = 0;
        var page = DocxReader.Read(stream).Page;
        page.LineNumberMode.Should().Be(LineNumberMode.RestartEachSection);
        page.LineNumberStartAt.Should().Be(4);
        page.LineNumberCountBy.Should().Be(2);
    }

    [Fact]
    public void DefaultPage_HasNoLineNumbering()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        var page = RoundTrip(doc).Page;

        page.LineNumberMode.Should().Be(LineNumberMode.None);
        page.LineNumberCountBy.Should().Be(1);
    }

    [Fact]
    public void DefaultPage_EmitsNoLnNumTypeElement()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();

        documentXml.Should().NotContain("lnNumType");
    }

    [Fact]
    public void LineNumbers_EmitsLnNumTypeInSectPr()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Two-line counted body."));
        doc.Page.LineNumberMode = LineNumberMode.RestartEachPage;
        doc.Page.LineNumberCountBy = 3;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();

        documentXml.Should().Contain("w:lnNumType");
        documentXml.Should().Contain("w:countBy=\"3\"");
        documentXml.Should().Contain("w:restart=\"newPage\"");
    }

    [Fact]
    public void Watermark_RoundTrips_AsCustomProperty()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("watermarked"));
        doc.Page.Watermark = "CONFIDENTIAL";

        RoundTrip(doc).Page.Watermark.Should().Be("CONFIDENTIAL");
    }

    [Fact]
    public void BottomOnlyParagraphBorder_RoundTrips_AsHorizontalRule()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder("#808080", 0.75, BottomOnly: true)
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().NotBeNull();
        formatting.Border!.BottomOnly.Should().BeTrue();
        formatting.Border.ColorHex.Should().Be("#808080");
        formatting.Border.WidthPt.Should().BeApproximately(0.75, 0.001);
    }

    [Fact]
    public void BoxParagraphBorder_RoundTrips_AsBoxNotBottomOnly()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("boxed")
        {
            Formatting = ParagraphFormatting.Default with { Border = new ParagraphBorder("#000000", 1.0) }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().NotBeNull();
        formatting.Border!.BottomOnly.Should().BeFalse();
    }

    [Fact]
    public void PageBreakBefore_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("after break")
        {
            Formatting = ParagraphFormatting.Default with { PageBreakBefore = true }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.PageBreakBefore.Should().BeTrue();
    }

    [Fact]
    public void PlainParagraph_HasNoPageBreakBefore()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        RoundTrip(doc).Paragraphs.First().Formatting.PageBreakBefore.Should().BeFalse();
    }

    [Fact]
    public void KeepWithNext_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("keep with next")
        {
            Formatting = ParagraphFormatting.Default with { KeepWithNext = true }
        });

        RoundTrip(doc).Paragraphs.First().Formatting.KeepWithNext.Should().BeTrue();
    }

    [Fact]
    public void KeepLinesTogether_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("keep lines together")
        {
            Formatting = ParagraphFormatting.Default with { KeepLinesTogether = true }
        });

        RoundTrip(doc).Paragraphs.First().Formatting.KeepLinesTogether.Should().BeTrue();
    }

    [Fact]
    public void WidowControl_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("widow control")
        {
            Formatting = ParagraphFormatting.Default with { WidowControl = true }
        });

        RoundTrip(doc).Paragraphs.First().Formatting.WidowControl.Should().BeTrue();
    }

    [Fact]
    public void ExplicitOffWidowControl_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("widow control off")
        {
            Formatting = ParagraphFormatting.Default with { WidowControl = false, WidowControlIsSet = true }
        });

        var result = RoundTrip(doc).Paragraphs.First().Formatting;

        result.WidowControl.Should().BeFalse();
        result.WidowControlIsSet.Should().BeTrue();
    }

    [Fact]
    public void PlainParagraph_HasNoFlowControl()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.KeepWithNext.Should().BeFalse();
        formatting.KeepLinesTogether.Should().BeFalse();
        formatting.WidowControl.Should().BeFalse();
        formatting.WidowControlIsSet.Should().BeFalse();
    }

    [Fact]
    public void ParagraphShading_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("shaded")
        {
            Formatting = ParagraphFormatting.Default with { ShadingColorHex = "#FFFF00" }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ShadingColorHex.Should().Be("#FFFF00");
        formatting.Border.Should().BeNull();
    }

    [Fact]
    public void ParagraphWithoutBorderOrShading_HasNeither()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.Border.Should().BeNull();
        formatting.ShadingColorHex.Should().BeNull();
    }

    [Fact]
    public void Styles_And_StyleReference_RoundTrip()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Title") { StyleId = "Title" });

        var result = RoundTrip(doc);

        result.Styles.Should().ContainKey("Title");
        result.Styles["Title"].Run.Bold.Should().BeTrue();
        result.Paragraphs.First().StyleId.Should().Be("Title");
    }

    [Fact]
    public void Table_RoundTrips_RowsColumnsAndCellText()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Before table"));
        var table = new Table();
        for (var r = 0; r < 2; r++)
        {
            var row = new TableRow();
            for (var c = 0; c < 3; c++)
                row.Cells.Add(new TableCell($"r{r}c{c}"));
            table.Rows.Add(row);
        }
        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph("After table"));

        var result = RoundTrip(doc);

        result.Blocks.Should().HaveCount(3);
        result.Blocks[0].Should().BeOfType<Paragraph>();
        result.Blocks[2].Should().BeOfType<Paragraph>();

        var readTable = result.Blocks[1].Should().BeOfType<Table>().Subject;
        readTable.RowCount.Should().Be(2);
        readTable.ColumnCount.Should().Be(3);
        readTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("r0c0", "r0c1", "r0c2");
        readTable.Rows[1].Cells.Select(c => c.PlainText).Should().Equal("r1c0", "r1c1", "r1c2");
    }

    [Fact]
    public void Table_CellShadingAndColumnWidths_RoundTrip()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("shaded") { ShadingColorHex = "#FFFF00", WidthPt = 120 };
        table.Rows[0].Cells[1] = new TableCell("plain");
        table.ColumnWidthsPt.Add(120);
        table.ColumnWidthsPt.Add(180);
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.ColumnWidthsPt.Should().Equal(120, 180);

        var shadedCell = readTable.Rows[0].Cells[0];
        shadedCell.PlainText.Should().Be("shaded");
        shadedCell.ShadingColorHex.Should().Be("#FFFF00");
        shadedCell.WidthPt.Should().Be(120);

        var plainCell = readTable.Rows[0].Cells[1];
        plainCell.ShadingColorHex.Should().BeNull();
        plainCell.WidthPt.Should().BeNull();
    }

    [Fact]
    public void Table_FixedAutoFit_EmitsFixedLayoutBeforeCellMargins_AndRoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 2);
        table.AutoFit = AutoFitMode.Fixed;
        table.ColumnWidthsPt.AddRange([120.0, 180.0]);
        table.DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 6, BottomPt: 3, RightPt: 6);
        doc.Blocks.Add(table);

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var tableProperties = WriteDocumentXml(doc).Descendants(ns + "tblPr").Single();
        tableProperties.Element(ns + "tblLayout")?.Attribute(ns + "type")?.Value.Should().Be("fixed");
        tableProperties.Elements().ToList().IndexOf(tableProperties.Element(ns + "tblLayout")!)
            .Should().BeLessThan(tableProperties.Elements().ToList().IndexOf(tableProperties.Element(ns + "tblCellMar")!));

        var result = RoundTrip(doc);
        result.Blocks.OfType<Table>().Single().AutoFit.Should().Be(AutoFitMode.Fixed);
    }

    [Fact]
    public void Table_ContentAutoFit_EmitsAutofitLayout_AndRoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        table.AutoFit = AutoFitMode.Contents;
        doc.Blocks.Add(table);

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        WriteDocumentXml(doc).Descendants(ns + "tblLayout").Single().Attribute(ns + "type")?.Value.Should().Be("autofit");

        var result = RoundTrip(doc);
        result.Blocks.OfType<Table>().Single().AutoFit.Should().Be(AutoFitMode.Contents);
    }

    [Fact]
    public void Table_OmittedLayout_UsesWordDefaultContentAutoFit()
    {
        var result = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tblPr><w:tblW w:w="4600" w:type="dxa"/></w:tblPr>
              <w:tblGrid><w:gridCol w:w="2300"/><w:gridCol w:w="2300"/></w:tblGrid>
              <w:tr>
                <w:tc><w:p><w:r><w:t>short</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>longer content</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            """);

        var table = result.Blocks.OfType<Table>().Single();
        table.AutoFit.Should().Be(AutoFitMode.Contents);
        table.PreferredWidthPt.Should().Be(230);
        table.ColumnWidthsPt.Should().Equal(115, 115);
    }

    [Fact]
    public void Table_WithoutShadingOrWidths_StillRoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("a");
        table.Rows[0].Cells[1] = new TableCell("b");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.ColumnWidthsPt.Should().BeEmpty();
        readTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("a", "b");
        readTable.Rows[0].Cells.Should().OnlyContain(c => c.ShadingColorHex == null && c.WidthPt == null);
    }

    [Fact]
    public void Table_BorderlessFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 1);
        table.Formatting = TableFormatting.Default with { Borders = false };
        table.Rows[0].Cells[0] = new TableCell("x");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Formatting.Borders.Should().BeFalse();
        readTable.Rows[0].Cells[0].PlainText.Should().Be("x");
    }

    [Fact]
    public void Table_HorizontalMerge_GridSpanRoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(1, 3);
        // Merge the first two cells of the row: the survivor spans two grid columns, the absorbed cell
        // is dropped (mirroring DocumentView.MergeSelectedCells).
        table.Rows[0].Cells[0] = new TableCell("merged") { GridSpan = 2 };
        table.Rows[0].Cells.RemoveAt(1);
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Rows[0].Cells.Should().HaveCount(2);
        readTable.Rows[0].Cells[0].PlainText.Should().Be("merged");
        readTable.Rows[0].Cells[0].GridSpan.Should().Be(2);
        readTable.Rows[0].Cells[1].GridSpan.Should().Be(1);
    }

    [Fact]
    public void Table_VerticalMerge_VMergeRoundTrips()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        // Merge the left column down a row: top cell restarts, the one below continues.
        table.Rows[0].Cells[0] = new TableCell("top") { VerticalMerge = VerticalMergeState.Restart };
        table.Rows[1].Cells[0] = new TableCell(string.Empty) { VerticalMerge = VerticalMergeState.Continue };
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Rows[0].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Restart);
        readTable.Rows[0].Cells[0].PlainText.Should().Be("top");
        readTable.Rows[1].Cells[0].VerticalMerge.Should().Be(VerticalMergeState.Continue);
        // The other column is untouched.
        readTable.Rows[0].Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None);
        readTable.Rows[1].Cells[1].VerticalMerge.Should().Be(VerticalMergeState.None);
    }

    [Fact]
    public void Table_SplitCellSubdivision_RoundTripsWordCompatibleGrid()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("A") { WidthPt = 234 };
        table.Rows[0].Cells[1] = new TableCell("B") { WidthPt = 234 };
        table.Rows[1].Cells[0] = new TableCell("C") { WidthPt = 234 };
        table.Rows[1].Cells[1] = new TableCell("D") { WidthPt = 234 };
        table.ColumnWidthsPt.AddRange([234, 234]);
        doc.Blocks.Add(table);

        var bus = new DocumentCommandBus(new CommandContext(doc));
        bus.Execute(new SplitCellCommand(0, rowIndex: 0, columnIndex: 0, rows: 2, columns: 2));
        var readTable = RoundTrip(doc).Blocks.OfType<Table>().Single();

        readTable.ColumnWidthsPt.Should().Equal(117, 117, 234);
        readTable.Rows.Should().HaveCount(3);
        readTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("A", "", "B");
        readTable.Rows[0].Cells.Select(c => c.VerticalMerge).Should().Equal(
            VerticalMergeState.None, VerticalMergeState.None, VerticalMergeState.Restart);
        readTable.Rows[1].Cells.Select(c => c.VerticalMerge).Should().Equal(
            VerticalMergeState.None, VerticalMergeState.None, VerticalMergeState.Continue);
        readTable.Rows[2].Cells.Select(c => c.GridSpan).Should().Equal(2, 1);
    }

    [Fact]
    public void Table_PlainCells_HaveDefaultSpansAfterRoundTrip()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("a");
        table.Rows[0].Cells[1] = new TableCell("b");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Rows.SelectMany(r => r.Cells)
            .Should().OnlyContain(c => c.GridSpan == 1 && c.VerticalMerge == VerticalMergeState.None);
    }

    [Fact]
    public void Table_CellBorders_DistinctEdges_RoundTrip()
    {
        // A cell with per-edge borders (distinct style/colour/width on each edge) must survive the full
        // write→read cycle so w:tcBorders is correctly emitted and parsed back into the model.
        var doc = new TextDocument();
        var table = Table.Create(1, 2);
        table.Rows[0].Cells[0] = new TableCell("bordered")
        {
            Borders = new CellBorders
            {
                Top    = new CellBorderEdge(BorderLineStyle.Double,  "#FF0000", 1.0),
                Bottom = new CellBorderEdge(BorderLineStyle.Dashed,  "#0000FF", 0.75),
                Left   = new CellBorderEdge(BorderLineStyle.Thick,   "#008000", 2.0),
                Right  = new CellBorderEdge(BorderLineStyle.Dotted,  "#800000", 0.5),
            }
        };
        table.Rows[0].Cells[1] = new TableCell("plain");
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        var borderedCell = readTable.Rows[0].Cells[0];
        borderedCell.PlainText.Should().Be("bordered");
        borderedCell.Borders.Should().NotBeNull();
        borderedCell.Borders!.Top.Should().NotBeNull();
        borderedCell.Borders.Top!.Style.Should().Be(BorderLineStyle.Double);
        borderedCell.Borders.Top.ColorHex.Should().Be("#FF0000");
        borderedCell.Borders.Bottom.Should().NotBeNull();
        borderedCell.Borders.Bottom!.Style.Should().Be(BorderLineStyle.Dashed);
        borderedCell.Borders.Bottom.ColorHex.Should().Be("#0000FF");
        borderedCell.Borders.Left.Should().NotBeNull();
        borderedCell.Borders.Left!.Style.Should().Be(BorderLineStyle.Thick);
        borderedCell.Borders.Right.Should().NotBeNull();
        borderedCell.Borders.Right!.Style.Should().Be(BorderLineStyle.Dotted);

        // The plain cell must not pick up any borders.
        var plainCell = readTable.Rows[0].Cells[1];
        plainCell.PlainText.Should().Be("plain");
        plainCell.Borders.Should().BeNull();
    }

    [Fact]
    public void Table_CellTextDirection_AllValues_RoundTrip()
    {
        // Each CellTextDirection value (Horizontal / Rotate90 / Rotate270) must survive the full
        // write→read cycle: w:textDirection is emitted only for non-horizontal directions and parsed back.
        var doc = new TextDocument();
        var table = Table.Create(1, 3);
        table.Rows[0].Cells[0] = new TableCell("h")   { TextDirection = CellTextDirection.Horizontal };
        table.Rows[0].Cells[1] = new TableCell("r90")  { TextDirection = CellTextDirection.Rotate90 };
        table.Rows[0].Cells[2] = new TableCell("r270") { TextDirection = CellTextDirection.Rotate270 };
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Rows[0].Cells[0].TextDirection.Should().Be(CellTextDirection.Horizontal);
        readTable.Rows[0].Cells[1].TextDirection.Should().Be(CellTextDirection.Rotate90);
        readTable.Rows[0].Cells[2].TextDirection.Should().Be(CellTextDirection.Rotate270);
    }

    [Fact]
    public void Table_StyleToggles_RoundTrip()
    {
        var doc = new TextDocument();
        var table = Table.Create(3, 2);
        table.Rows[0].Cells[0] = new TableCell("H1");
        table.Rows[0].Cells[1] = new TableCell("H2");
        table.Rows[1].Cells[0] = new TableCell("a1");
        table.Rows[1].Cells[1] = new TableCell("a2");
        table.Rows[2].Cells[0] = new TableCell("b1");
        table.Rows[2].Cells[1] = new TableCell("b2");
        table.Formatting = TableFormatting.Default with
        {
            HeaderRow = true,
            BandedRows = true,
            RepeatHeaderRow = true
        };
        doc.Blocks.Add(table);

        var result = RoundTrip(doc);

        var readTable = result.Blocks.OfType<Table>().Single();
        readTable.Formatting.HeaderRow.Should().BeTrue();
        readTable.Formatting.BandedRows.Should().BeTrue();
        readTable.Formatting.RepeatHeaderRow.Should().BeTrue();

        // The style fills (header + banded) are style-derived, not explicit per-cell shading, so they
        // must not read back as ShadingColorHex on any cell.
        readTable.Rows.SelectMany(r => r.Cells)
            .Should().OnlyContain(c => c.ShadingColorHex == null);
        readTable.Rows[0].Cells.Select(c => c.PlainText).Should().Equal("H1", "H2");
        readTable.Rows[2].Cells.Select(c => c.PlainText).Should().Equal("b1", "b2");
    }

    [Fact]
    public void Table_RepeatHeader_ExplicitOffToggle_ReadsDisabledAndIsCanonicallyOmittedOnSave()
    {
        var document = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tblPr><w:tblLook w:firstRow="1"/></w:tblPr>
              <w:tr>
                <w:trPr><w:tblHeader w:val="0"/></w:trPr>
                <w:tc><w:p><w:r><w:t>Header</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            <w:sectPr/>
            """);

        var table = document.Blocks.OfType<Table>().Single();
        table.Formatting.HeaderRow.Should().BeTrue();
        table.Formatting.RepeatHeaderRow.Should().BeFalse("w:tblHeader is an OOXML on/off toggle");

        var xml = WriteDocumentXml(document);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        xml.Descendants(ns + "tblHeader").Should().BeEmpty();

        var reopened = RoundTrip(document).Blocks.OfType<Table>().Single();
        reopened.Formatting.RepeatHeaderRow.Should().BeFalse();
    }

    [Fact]
    public void Table_MultiRowRepeatHeader_RoundTrips()
    {
        // Word lets any number of leading, contiguous rows carry w:tblHeader to build a multi-row
        // repeating header (e.g. a title row plus a column-labels row). Reading must not collapse this
        // to just row 0, and writing must re-emit tblHeader on every repeating row so the document
        // doesn't degrade on open+save. See TableRow.IsRepeatingHeader.
        var document = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tr>
                <w:trPr><w:tblHeader/></w:trPr>
                <w:tc><w:p><w:r><w:t>Title</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:trPr><w:tblHeader/></w:trPr>
                <w:tc><w:p><w:r><w:t>Columns</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>Body</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            <w:sectPr/>
            """);

        var table = document.Blocks.OfType<Table>().Single();
        table.Rows.Should().HaveCount(3);
        table.Rows[0].IsRepeatingHeader.Should().BeTrue("row 0 carries w:tblHeader");
        table.Rows[1].IsRepeatingHeader.Should().BeTrue("row 1 also carries w:tblHeader");
        table.Rows[2].IsRepeatingHeader.Should().BeFalse("row 2 carries no w:tblHeader");
        // The single-flag convenience mirror still reflects row 0.
        table.Formatting.RepeatHeaderRow.Should().BeTrue();

        var xml = WriteDocumentXml(document);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var writtenRows = xml.Descendants(ns + "tbl").Single().Elements(ns + "tr").ToList();
        writtenRows.Should().HaveCount(3);
        writtenRows[0].Element(ns + "trPr")?.Element(ns + "tblHeader").Should().NotBeNull(
            "row 0 must still repeat after a write");
        writtenRows[1].Element(ns + "trPr")?.Element(ns + "tblHeader").Should().NotBeNull(
            "row 1 must still repeat after a write — this is the bug: only row 0 used to be emitted");
        writtenRows[2].Element(ns + "trPr")?.Element(ns + "tblHeader").Should().BeNull(
            "the plain body row must not gain a repeat flag");

        // A second read→write cycle (full round-trip) must keep both header rows intact.
        var reopened = RoundTrip(document).Blocks.OfType<Table>().Single();
        reopened.Rows[0].IsRepeatingHeader.Should().BeTrue();
        reopened.Rows[1].IsRepeatingHeader.Should().BeTrue();
        reopened.Rows[2].IsRepeatingHeader.Should().BeFalse();
    }

    [Fact]
    public void Table_SingleRowRepeatHeader_RoundTrips_NoRegression()
    {
        // Sibling no-regression: a plain single-row repeating header (the common case, and the only
        // shape the table-level RepeatHeaderRow convenience flag alone can express) must keep working
        // exactly as before the multi-row fix.
        var doc = new TextDocument();
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0] = new TableCell("Head");
        table.Rows[1].Cells[0] = new TableCell("Body");
        table.Formatting = TableFormatting.Default with { RepeatHeaderRow = true };
        doc.Blocks.Add(table);

        var xml = WriteDocumentXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var writtenRows = xml.Descendants(ns + "tbl").Single().Elements(ns + "tr").ToList();
        writtenRows[0].Element(ns + "trPr")?.Element(ns + "tblHeader").Should().NotBeNull();
        writtenRows[1].Element(ns + "trPr")?.Element(ns + "tblHeader").Should().BeNull();

        var reopened = RoundTrip(doc).Blocks.OfType<Table>().Single();
        reopened.Rows[0].IsRepeatingHeader.Should().BeTrue();
        reopened.Rows[1].IsRepeatingHeader.Should().BeFalse();
        reopened.Formatting.RepeatHeaderRow.Should().BeTrue();
    }

    [Fact]
    public void Table_LookOnOffLexicalValues_ReadAndCanonicalize()
    {
        var document = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tblPr>
                <w:tblLook w:firstRow="true" w:lastRow="on" w:firstColumn="true" w:lastColumn="on"
                           w:noHBand="false" w:noVBand="off"/>
              </w:tblPr>
              <w:tr><w:tc><w:p><w:r><w:t>Body</w:t></w:r></w:p></w:tc></w:tr>
            </w:tbl>
            <w:sectPr/>
            """);

        var formatting = document.Blocks.OfType<Table>().Single().Formatting;
        formatting.HeaderRow.Should().BeTrue();
        formatting.LastRow.Should().BeTrue();
        formatting.FirstColumn.Should().BeTrue();
        formatting.LastColumn.Should().BeTrue();
        formatting.BandedRows.Should().BeTrue();
        formatting.BandedColumns.Should().BeTrue();

        var xml = WriteDocumentXml(document);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var look = xml.Descendants(ns + "tblLook").Single();
        look.Attribute(ns + "firstRow")?.Value.Should().Be("1");
        look.Attribute(ns + "lastRow")?.Value.Should().Be("1");
        look.Attribute(ns + "firstColumn")?.Value.Should().Be("1");
        look.Attribute(ns + "lastColumn")?.Value.Should().Be("1");
        look.Attribute(ns + "noHBand")?.Value.Should().Be("0");
        look.Attribute(ns + "noVBand")?.Value.Should().Be("0");

        RoundTrip(document).Blocks.OfType<Table>().Single().Formatting.Should().Be(formatting);
    }

    [Fact]
    public void Table_RowCantSplit_ExplicitOffToggle_AllowsBreakAndIsCanonicallyOmittedOnSave()
    {
        var document = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tr>
                <w:trPr><w:cantSplit w:val="0"/></w:trPr>
                <w:tc><w:p><w:r><w:t>Body</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            <w:sectPr/>
            """);

        var row = document.Blocks.OfType<Table>().Single().Rows.Single();
        row.AllowBreakAcrossPages.Should().BeTrue("w:cantSplit is an OOXML on/off toggle");

        var xml = WriteDocumentXml(document);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        xml.Descendants(ns + "cantSplit").Should().BeEmpty();

        var reopened = RoundTrip(document).Blocks.OfType<Table>().Single().Rows.Single();
        reopened.AllowBreakAcrossPages.Should().BeTrue();
    }

    [Fact]
    public void Table_HeaderRow_EmitsBoldShadedTblHeader()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 1);
        table.Rows[0].Cells[0] = new TableCell("Head");
        table.Rows[1].Cells[0] = new TableCell("Body");
        table.Formatting = TableFormatting.Default with { HeaderRow = true, RepeatHeaderRow = true };
        doc.Blocks.Add(table);

        var xml = WriteDocumentXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var tbl = xml.Descendants(ns + "tbl").Single();

        // tblLook persists the HeaderRow flag, the first row carries tblHeader (repeat), and its cell is
        // shaded with the header fill and contains a bold run.
        tbl.Element(ns + "tblPr")!.Element(ns + "tblLook")!.Attribute(ns + "firstRow")!.Value.Should().Be("1");
        var firstRow = tbl.Elements(ns + "tr").First();
        firstRow.Element(ns + "trPr")!.Element(ns + "tblHeader").Should().NotBeNull();
        firstRow.Descendants(ns + "shd").First().Attribute(ns + "fill")!.Value.Should().Be("D9E2F3");
        firstRow.Descendants(ns + "b").Should().NotBeEmpty();
    }

    [Fact]
    public void Table_BandedRows_EmitsFirstBodyRowBanding()
    {
        var doc = new TextDocument();
        var table = Table.Create(3, 1);
        table.Rows[0].Cells[0] = new TableCell("Head");
        table.Rows[1].Cells[0] = new TableCell("Body 1");
        table.Rows[2].Cells[0] = new TableCell("Body 2");
        table.Formatting = TableFormatting.Default with { HeaderRow = true, BandedRows = true };
        doc.Blocks.Add(table);

        var xml = WriteDocumentXml(doc);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var rows = xml.Descendants(ns + "tbl").Single().Elements(ns + "tr").ToList();

        rows[1].Descendants(ns + "shd")
            .Any(shd => shd.Attribute(ns + "fill")?.Value == "F2F2F2")
            .Should().BeTrue("Word band 1 starts on the first body row");
        rows[2].Descendants(ns + "shd")
            .Any(shd => shd.Attribute(ns + "fill")?.Value == "F2F2F2")
            .Should().BeFalse("the second body row is the alternate unfilled band");
    }

    [Fact]
    public void Table_HeaderRowCell_InlineImage_RoundTrips()
    {
        // Regression: a header-row cell's runs are re-rendered bold via BoldHeaderParagraph, which clones
        // each run. BuildRun resolves a run's image media part from a per-write map keyed by run reference,
        // so a cloned image run missed the lookup and the w:drawing was silently dropped (image 1 -> 0).
        var png = MinimalPng();
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        table.Rows[0].Cells[0] = new TableCell("Head");
        var imageCell = new TableCell();
        var imagePara = new Paragraph();
        imagePara.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)));
        imageCell.Paragraphs.Add(imagePara);
        table.Rows[0].Cells[1] = imageCell;
        table.Rows[1].Cells[0] = new TableCell("a");
        table.Rows[1].Cells[1] = new TableCell("b");
        table.Formatting = TableFormatting.Default with { HeaderRow = true };
        doc.Blocks.Add(table);

        var readTable = RoundTrip(doc).Blocks.OfType<Table>().Single();
        var imageRun = readTable.Rows[0].Cells[1].Paragraphs
            .SelectMany(p => p.Runs)
            .Single(r => r.Image is not null);

        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(120, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(90, 0.01);
    }

    [Fact]
    public void Table_PlainTable_StyleTogglesAllFalse()
    {
        var doc = new TextDocument();
        var table = Table.Create(2, 2);
        doc.Blocks.Add(table);

        var readTable = RoundTrip(doc).Blocks.OfType<Table>().Single();
        readTable.Formatting.HeaderRow.Should().BeFalse();
        readTable.Formatting.BandedRows.Should().BeFalse();
        readTable.Formatting.RepeatHeaderRow.Should().BeFalse();
    }

    [Fact]
    public void InlineImage_RoundTrips()
    {
        var png = MinimalPng();
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("before "));
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)));
        paragraph.Runs.Add(new Run(" after"));
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        // Text runs survive on either side of the image run.
        runs.Select(r => r.Text).Should().Equal("before ", string.Empty, " after");

        var imageRun = runs.Single(r => r.Image is not null);
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(120, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(90, 0.01);

        // An image without alt text round-trips with AltText still null (no wp:docPr/@descr emitted).
        imageRun.Image.AltText.Should().BeNull();
    }

    [Fact]
    public void InlineImage_AltText_RoundTrips()
    {
        var png = MinimalPng();
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)
        {
            AltText = "A red square logo",
        }));
        doc.Blocks.Add(paragraph);

        var imageRun = RoundTrip(doc).Paragraphs.First().Runs.Single(r => r.Image is not null);

        // Bytes + size + alt text all survive the docx round-trip (alt text via wp:docPr/@descr).
        imageRun.Image!.PngBytes.Should().Equal(png);
        imageRun.Image.WidthPt.Should().BeApproximately(120, 0.01);
        imageRun.Image.HeightPt.Should().BeApproximately(90, 0.01);
        imageRun.Image.AltText.Should().Be("A red square logo");
    }

    [Fact]
    public void InlineImage_AltText_EmittedAsDocPrDescr()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), 50, 50) { AltText = "Accessible caption" }));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("descr=\"Accessible caption\"");
    }

    [Fact]
    public void InlineImage_IsDecorative_RoundTrips()
    {
        var png = MinimalPng();
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)
        {
            IsDecorative = true,
        }));
        doc.Blocks.Add(paragraph);

        var imageRun = RoundTrip(doc).Paragraphs.First().Runs.Single(r => r.Image is not null);

        imageRun.Image!.IsDecorative.Should().BeTrue();
    }

    [Fact]
    public void InlineImage_NotDecorative_RoundTripsFalse()
    {
        // Sibling of InlineImage_IsDecorative_RoundTrips: an ordinary (non-decorative) image must not
        // pick up the flag from the read/write path.
        var png = MinimalPng();
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(png, widthPt: 120, heightPt: 90)));
        doc.Blocks.Add(paragraph);

        var imageRun = RoundTrip(doc).Paragraphs.First().Runs.Single(r => r.Image is not null);

        imageRun.Image!.IsDecorative.Should().BeFalse();
    }

    [Fact]
    public void InlineImage_IsDecorative_EmittedAsDecorativeExtension()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), 50, 50) { IsDecorative = true }));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = docReader.ReadToEnd();
        xml.Should().Contain("{C183D7F6-B498-43B3-948B-1728B52AA6E4}");
        xml.Should().Contain("decorative");
    }

    [Fact]
    public void InlineImage_AddsPngContentTypeAndMediaPart()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.FromImage(new InlineImage(MinimalPng(), 50, 50)));
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/media/image1.png").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        ctReader.ReadToEnd().Should().Contain("image/png");
    }

    // A 1x1 transparent PNG — valid bytes so decoders accept it, opaque to the writer (stored verbatim).
    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x62, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82,
    ];

    [Fact]
    public void CoreProperties_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Properties.Title = "My Title";
        doc.Properties.Author = "Ada Lovelace";
        doc.Properties.Subject = "Analytical Engine";
        doc.Properties.Keywords = "history; computing";
        doc.Properties.Comments = "First program";
        doc.Properties.LastModifiedBy = "Charles Babbage";
        doc.Properties.Created = new DateTimeOffset(1843, 10, 1, 9, 30, 0, TimeSpan.Zero);
        doc.Properties.Modified = new DateTimeOffset(1843, 10, 15, 14, 0, 0, TimeSpan.Zero);
        doc.Properties.Category = "Computing";
        doc.Properties.ContentStatus = "Draft";
        doc.Properties.Language = "en-GB";
        doc.Properties.Version = "1.0";

        var properties = RoundTrip(doc).Properties;

        properties.Title.Should().Be("My Title");
        properties.Author.Should().Be("Ada Lovelace");
        properties.Subject.Should().Be("Analytical Engine");
        properties.Keywords.Should().Be("history; computing");
        properties.Comments.Should().Be("First program");
        properties.LastModifiedBy.Should().Be("Charles Babbage");
        properties.Created.Should().Be(new DateTimeOffset(1843, 10, 1, 9, 30, 0, TimeSpan.Zero));
        properties.Modified.Should().Be(new DateTimeOffset(1843, 10, 15, 14, 0, 0, TimeSpan.Zero));
        properties.Category.Should().Be("Computing");
        properties.ContentStatus.Should().Be("Draft");
        properties.Language.Should().Be("en-GB");
        properties.Version.Should().Be("1.0");
    }

    [Fact]
    public void CoreProperties_PreserveUnmodeledWordPropertiesAcrossEditedAndSecondSave()
    {
        XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        var sourceDocument = new TextDocument();
        sourceDocument.Blocks.Add(new Paragraph("Body"));
        sourceDocument.Properties.Title = "Original title";

        using var sourceStream = new MemoryStream();
        DocxWriter.Write(sourceDocument, sourceStream);
        using (var archive = new ZipArchive(sourceStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("docProps/core.xml")!;
            XDocument core;
            using (var reader = entry.Open())
                core = XDocument.Load(reader);

            core.Root!.Add(
                new XElement(cp + "lastPrinted", "2026-07-31T21:14:15Z"),
                new XElement(cp + "revision", "42"),
                new XElement(dc + "identifier", "urn:freew:source:17"),
                new XElement(cp + "contentType", "application/vnd.example.review"));
            entry.Delete();
            var replacement = archive.CreateEntry("docProps/core.xml");
            using var writer = replacement.Open();
            core.Save(writer);
        }

        var sourceBytes = sourceStream.ToArray();
        var loaded = DocxReader.Read(new MemoryStream(sourceBytes));
        loaded.Properties.Title = "Edited title";

        static byte[] Save(TextDocument document)
        {
            using var stream = new MemoryStream();
            DocxWriter.Write(document, stream);
            return stream.ToArray();
        }

        static XDocument ReadCore(byte[] package)
        {
            using var stream = new MemoryStream(package);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            using var entry = archive.GetEntry("docProps/core.xml")!.Open();
            return XDocument.Load(entry);
        }

        var firstSaveBytes = Save(loaded);
        var firstSave = ReadCore(firstSaveBytes);
        firstSave.Root!.Element(dc + "title")!.Value.Should().Be("Edited title");

        var unmodeledNames = new[]
        {
            cp + "lastPrinted",
            cp + "revision",
            dc + "identifier",
            cp + "contentType",
        };
        var sourceCore = ReadCore(sourceBytes);
        foreach (var name in unmodeledNames)
        {
            var expected = sourceCore.Root!.Elements(name).Should().ContainSingle().Subject;
            var actual = firstSave.Root!.Elements(name).Should().ContainSingle().Subject;
            XNode.DeepEquals(actual, expected).Should().BeTrue($"{name} must retain its exact source value");
        }

        var reopened = DocxReader.Read(new MemoryStream(firstSaveBytes));
        var secondSave = ReadCore(Save(reopened));
        secondSave.Root!.Element(dc + "title")!.Value.Should().Be("Edited title");
        foreach (var name in unmodeledNames)
        {
            var first = firstSave.Root!.Elements(name).Should().ContainSingle().Subject;
            var second = secondSave.Root!.Elements(name).Should().ContainSingle().Subject;
            XNode.DeepEquals(second, first).Should().BeTrue($"{name} must remain stable on a second save");
        }
    }

    [Fact]
    public void CoreProperties_PackageHasCorePartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Properties.Title = "Has Core";

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("docProps/core.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/docProps/core.xml");
        contentTypes.Should().Contain("application/vnd.openxmlformats-package.core-properties+xml");

        using var relsReader = new StreamReader(zip.GetEntry("_rels/.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("docProps/core.xml");
        rels.Should().Contain("http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties");
    }

    [Fact]
    public void MissingCorePart_YieldsEmptyProperties()
    {
        // A package without docProps/core.xml (built by hand) must read back with empty properties.
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body><w:p><w:r><w:t>Hi</w:t></w:r></w:p></w:body></w:document>");
        }
        stream.Position = 0;

        var properties = DocxReader.Read(stream).Properties;

        properties.Title.Should().BeNull();
        properties.Author.Should().BeNull();
        properties.Created.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_RoundTrips_WithUrlIntact()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("see "));
        paragraph.Runs.Add(new Run("the docs") { HyperlinkUrl = "https://example.com/docs" });
        paragraph.Runs.Add(new Run(" now"));
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Select(r => r.Text).Should().Equal("see ", "the docs", " now");
        runs[0].HyperlinkUrl.Should().BeNull();
        runs[1].HyperlinkUrl.Should().Be("https://example.com/docs");
        runs[2].HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_PreservesRunFormatting()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("bold link", new RunFormatting { Bold = true })
        {
            HyperlinkUrl = "https://example.com"
        });
        doc.Blocks.Add(paragraph);

        var run = RoundTrip(doc).Paragraphs.First().Runs.Single();

        run.Text.Should().Be("bold link");
        run.HyperlinkUrl.Should().Be("https://example.com");
        run.Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void Hyperlink_WritesExternalRelationship()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link") { HyperlinkUrl = "https://example.com/page" });
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("https://example.com/page");
        rels.Should().Contain("TargetMode=\"External\"");
        rels.Should().Contain("/hyperlink");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("hyperlink");
    }

    [Fact]
    public void Hyperlinks_InStoryParts_UseOwningPartRelationships_AndRoundTrip()
    {
        const string headerUrl = "https://example.com/header";
        const string footnoteUrl = "https://example.com/footnote";
        const string endnoteUrl = "https://example.com/endnote";
        const string commentUrl = "https://example.com/comment";

        var document = new TextDocument();
        var body = new Paragraph("Body");
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(Run.EndnoteReference(1));
        document.Blocks.Add(body);

        var header = new HeaderFooter();
        header.Paragraphs.Add(new Paragraph());
        header.Paragraphs[0].Runs.Add(new Run("Header") { HyperlinkUrl = headerUrl });
        document.FinalSectionHeadersFooters.Header = header;

        var footnote = new Footnote(1);
        footnote.Content.Add(new Paragraph());
        footnote.Content[0].Runs.Add(new Run("Footnote") { HyperlinkUrl = footnoteUrl });
        document.Footnotes[1] = footnote;

        var endnote = new Endnote(1);
        endnote.Content.Add(new Paragraph());
        endnote.Content[0].Runs.Add(new Run("Endnote") { HyperlinkUrl = endnoteUrl });
        document.Endnotes[1] = endnote;

        var comment = new Comment(0, string.Empty, author: "A", initials: "A");
        comment.Content[0].Runs.Add(new Run("Comment") { HyperlinkUrl = commentUrl });
        document.Comments[0] = comment;

        byte[] bytes;
        using (var stream = new MemoryStream())
        {
            DocxWriter.Write(document, stream);
            bytes = stream.ToArray();
        }

        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            string ReadEntry(string path)
            {
                using var reader = new StreamReader(zip.GetEntry(path)!.Open());
                return reader.ReadToEnd();
            }

            ReadEntry("word/_rels/header1.xml.rels").Should().Contain(headerUrl).And.Contain("TargetMode=\"External\"");
            ReadEntry("word/_rels/footnotes.xml.rels").Should().Contain(footnoteUrl).And.Contain("TargetMode=\"External\"");
            ReadEntry("word/_rels/endnotes.xml.rels").Should().Contain(endnoteUrl).And.Contain("TargetMode=\"External\"");
            ReadEntry("word/_rels/comments.xml.rels").Should().Contain(commentUrl).And.Contain("TargetMode=\"External\"");

            var documentRelationships = ReadEntry("word/_rels/document.xml.rels");
            documentRelationships.Should().NotContain(headerUrl).And.NotContain(footnoteUrl)
                .And.NotContain(endnoteUrl).And.NotContain(commentUrl);
        }

        var roundTripped = DocxReader.Read(new MemoryStream(bytes));
        roundTripped.FinalSectionHeadersFooters.Header!.Paragraphs.Single().Runs.Single().HyperlinkUrl.Should().Be(headerUrl);
        roundTripped.Footnotes[1].Content.Single().Runs.Single(run => run.Text == "Footnote").HyperlinkUrl.Should().Be(footnoteUrl);
        roundTripped.Endnotes[1].Content.Single().Runs.Single(run => run.Text == "Endnote").HyperlinkUrl.Should().Be(endnoteUrl);
        roundTripped.Comments[0].Content.Single().Runs.Single(run => run.Text == "Comment").HyperlinkUrl.Should().Be(commentUrl);
    }

    [Fact]
    public void Hyperlink_SharedUrl_UsesSingleRelationship()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("a") { HyperlinkUrl = "https://example.com" });
        paragraph.Runs.Add(new Run("plain"));
        paragraph.Runs.Add(new Run("b") { HyperlinkUrl = "https://example.com" });
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Where(r => r.HyperlinkUrl is not null)
            .Select(r => r.HyperlinkUrl)
            .Should().AllBe("https://example.com");
        runs.Single(r => r.Text == "plain").HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void Bookmark_RoundTrips_WithNameIntact()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("intro"));
        doc.Blocks.Add(new Paragraph("the target") { BookmarkName = "Section1" });

        var paragraphs = RoundTrip(doc).Paragraphs.ToList();

        paragraphs.Select(p => p.PlainText).Should().Equal("intro", "the target");
        paragraphs[0].BookmarkName.Should().BeNull();
        paragraphs[1].BookmarkName.Should().Be("Section1");
    }

    [Fact]
    public void InternalLink_RoundTrips_WithAnchorIntact()
    {
        var doc = new TextDocument();
        var linking = new Paragraph();
        linking.Runs.Add(new Run("jump to "));
        linking.Runs.Add(new Run("Section 1") { HyperlinkAnchor = "Section1" });
        linking.Runs.Add(new Run(" please"));
        doc.Blocks.Add(linking);
        doc.Blocks.Add(new Paragraph("the target") { BookmarkName = "Section1" });

        var result = RoundTrip(doc);
        var runs = result.Paragraphs.First().Runs;

        runs.Select(r => r.Text).Should().Equal("jump to ", "Section 1", " please");
        runs[0].HyperlinkAnchor.Should().BeNull();
        runs[1].HyperlinkAnchor.Should().Be("Section1");
        runs[1].HyperlinkUrl.Should().BeNull();
        runs[2].HyperlinkAnchor.Should().BeNull();
        result.Paragraphs.Last().BookmarkName.Should().Be("Section1");
    }

    [Fact]
    public void InternalLink_WritesAnchorAndBookmarkElements()
    {
        var doc = new TextDocument();
        var linking = new Paragraph();
        linking.Runs.Add(new Run("go") { HyperlinkAnchor = "Top" });
        doc.Blocks.Add(linking);
        doc.Blocks.Add(new Paragraph("top") { BookmarkName = "Top" });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = docReader.ReadToEnd();
        xml.Should().Contain("anchor=\"Top\"");
        xml.Should().Contain("bookmarkStart");
        xml.Should().Contain("name=\"Top\"");
        xml.Should().Contain("bookmarkEnd");

        // An internal link must NOT create an external hyperlink relationship.
        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        relsReader.ReadToEnd().Should().NotContain("/hyperlink");
    }

    [Fact]
    public void InternalLink_PreservesRunFormatting()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("bold anchor", new RunFormatting { Bold = true })
        {
            HyperlinkAnchor = "Here"
        });
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(new Paragraph("dest") { BookmarkName = "Here" });

        var run = RoundTrip(doc).Paragraphs.First().Runs.Single();

        run.Text.Should().Be("bold anchor");
        run.HyperlinkAnchor.Should().Be("Here");
        run.Formatting.Bold.Should().BeTrue();
    }

    [Fact]
    public void ExternalAndInternalLinks_CoexistInSameDocument()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("external") { HyperlinkUrl = "https://example.com" });
        paragraph.Runs.Add(new Run(" and "));
        paragraph.Runs.Add(new Run("internal") { HyperlinkAnchor = "Mark" });
        doc.Blocks.Add(paragraph);
        doc.Blocks.Add(new Paragraph("dest") { BookmarkName = "Mark" });

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Single(r => r.Text == "external").HyperlinkUrl.Should().Be("https://example.com");
        runs.Single(r => r.Text == "external").HyperlinkAnchor.Should().BeNull();
        runs.Single(r => r.Text == "internal").HyperlinkAnchor.Should().Be("Mark");
        runs.Single(r => r.Text == "internal").HyperlinkUrl.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_RoundTrips_WithTooltip()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("see "));
        paragraph.Runs.Add(new Run("the docs")
        {
            HyperlinkUrl = "https://example.com/docs",
            HyperlinkTooltip = "Open the documentation"
        });
        paragraph.Runs.Add(new Run(" now"));
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        runs.Select(r => r.Text).Should().Equal("see ", "the docs", " now");
        var linked = runs.Single(r => r.Text == "the docs");
        linked.HyperlinkUrl.Should().Be("https://example.com/docs");
        linked.HyperlinkTooltip.Should().Be("Open the documentation");
        runs[0].HyperlinkTooltip.Should().BeNull();
        runs[2].HyperlinkTooltip.Should().BeNull();
    }

    [Fact]
    public void Hyperlink_WithTooltip_WritesTooltipAttribute()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link")
        {
            HyperlinkUrl = "https://example.com/page",
            HyperlinkTooltip = "Tip text"
        });
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = docReader.ReadToEnd();
        xml.Should().Contain("tooltip=\"Tip text\"");
    }

    [Fact]
    public void Hyperlink_WithoutTooltip_OmitsTooltipAttribute()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("link") { HyperlinkUrl = "https://example.com/page" });
        doc.Blocks.Add(paragraph);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().NotContain("tooltip");
    }

    [Fact]
    public void InternalLink_RoundTrips_WithTooltip()
    {
        var doc = new TextDocument();
        var linking = new Paragraph();
        linking.Runs.Add(new Run("jump to "));
        linking.Runs.Add(new Run("Section 1")
        {
            HyperlinkAnchor = "Section1",
            HyperlinkTooltip = "Go to section one"
        });
        doc.Blocks.Add(linking);
        doc.Blocks.Add(new Paragraph("the target") { BookmarkName = "Section1" });

        var runs = RoundTrip(doc).Paragraphs.First().Runs;

        var linked = runs.Single(r => r.Text == "Section 1");
        linked.HyperlinkAnchor.Should().Be("Section1");
        linked.HyperlinkUrl.Should().BeNull();
        linked.HyperlinkTooltip.Should().Be("Go to section one");
    }

    [Fact]
    public void BulletList_RoundTrips_ListKindAndLevel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bullet item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 1 }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ListKind.Should().Be(ListKind.Bullet);
        formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void NumberedList_RoundTrips_ListKindAndLevel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("numbered item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 2 }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ListKind.Should().Be(ListKind.Number);
        formatting.ListLevel.Should().Be(2);
    }

    [Fact]
    public void MultiLevelList_RoundTrips_ListKindAndLevel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("outline item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 2 }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.ListKind.Should().Be(ListKind.MultiLevel);
        formatting.ListLevel.Should().Be(2);
    }

    [Fact]
    public void MultiLevelList_DoesNotChangeBulletOrDecimalRoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("bullet")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet, ListLevel = 0 }
        });
        doc.Blocks.Add(new Paragraph("decimal")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number, ListLevel = 1 }
        });
        doc.Blocks.Add(new Paragraph("outline")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 1 }
        });

        var paragraphs = RoundTrip(doc).Paragraphs.ToList();

        paragraphs[0].Formatting.ListKind.Should().Be(ListKind.Bullet);
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Number);
        paragraphs[2].Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraphs[2].Formatting.ListLevel.Should().Be(1);
    }

    [Fact]
    public void MultiLevelList_WritesOutlineAbstractDefinition()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("outline item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel }
        });
        doc.Page.PageNumberChapterStyleLevel = 1;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var numReader = new StreamReader(zip.GetEntry("word/numbering.xml")!.Open());
        var numbering = numReader.ReadToEnd();

        // The outline abstract num is tagged multilevel and accumulates ancestor counters in its
        // level text: %1. / %1.%2. / %1.%2.%3. , and the multilevel list maps to numId 3.
        numbering.Should().Contain("multiLevelType");
        numbering.Should().Contain("multilevel");
        numbering.Should().Contain("%1.%2.");
        numbering.Should().Contain("%1.%2.%3.");
        numbering.Should().Contain("<w:pStyle w:val=\"Heading1\"");
        numbering.Should().Contain("<w:pStyle w:val=\"Heading3\"");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("w:val=\"3\"");
    }

    [Fact]
    public void MultiLevelList_WritesAndReadsPerLevelNumberFormats()
    {
        var doc = new TextDocument();
        doc.MultiLevelList.SetNumberFormats(MultiLevelListFormat.DecimalLowerLetterLowerRomanNumberFormats);
        doc.Blocks.Add(new Paragraph("outline item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel, ListLevel = 2 }
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var numReader = new StreamReader(zip.GetEntry("word/numbering.xml")!.Open()))
        {
            var numbering = numReader.ReadToEnd();
            numbering.Should().Contain("<w:numFmt w:val=\"decimal\"");
            numbering.Should().Contain("<w:numFmt w:val=\"lowerLetter\"");
            numbering.Should().Contain("<w:numFmt w:val=\"lowerRoman\"");
        }

        stream.Position = 0;
        var read = DocxReader.Read(stream);

        read.MultiLevelList.NumberFormats.Take(3).Should().Equal(
            ListNumberFormat.Decimal,
            ListNumberFormat.LowerLetter,
            ListNumberFormat.LowerRoman);
        read.Paragraphs.Single().Formatting.ListKind.Should().Be(ListKind.MultiLevel);
    }

    [Fact]
    public void NumberedList_StartOverride_RoundTripsAndEmitsLvlOverride()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("first")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number }
        });
        doc.Blocks.Add(new Paragraph("restart at five")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.Number,
                ListStartOverride = 5
            }
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(zip.GetEntry("word/numbering.xml")!.Open()))
        {
            var numbering = reader.ReadToEnd();
            numbering.Should().Contain("<w:lvlOverride w:ilvl=\"0\">");
            numbering.Should().Contain("<w:startOverride w:val=\"5\" />");
        }

        stream.Position = 0;
        var paragraphs = DocxReader.Read(stream).Paragraphs.ToList();

        paragraphs[0].Formatting.ListStartOverride.Should().BeNull();
        paragraphs[1].Formatting.ListKind.Should().Be(ListKind.Number);
        paragraphs[1].Formatting.ListStartOverride.Should().Be(5);
    }

    /// <summary>Reads the body paragraphs' w:numPr/w:numId values, in document order (non-list paragraphs
    /// contribute nothing), so a restarted run's numbering INSTANCE can be asserted.</summary>
    private static List<int> WrittenNumIds(TextDocument document) => NumIdsOf(WriteDocumentXml(document));

    /// <summary>The same, for a non-body story part (e.g. word/header1.xml).</summary>
    private static List<int> PartNumIds(TextDocument document, string partName)
    {
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry(partName)!.Open();
        return NumIdsOf(XDocument.Load(entry));
    }

    private static List<int> NumIdsOf(XDocument part)
    {
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        return part
            .Descendants(w + "numId")
            .Where(id => id.Parent?.Name == w + "numPr")
            .Select(id => int.Parse(id.Attribute(w + "val")!.Value))
            .ToList();
    }

    private static Paragraph ListParagraph(string text, ListKind kind, int? startOverride = null, int level = 0) =>
        new(text)
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = kind,
                ListLevel = level,
                ListStartOverride = startOverride
            }
        };

    [Theory]
    [InlineData(ListKind.Number)]
    [InlineData(ListKind.MultiLevel)]
    public void RestartedList_ContinuationParagraphs_StayOnTheRestartNumId(ListKind kind)
    {
        // Only the FIRST paragraph of a restarted run carries ListStartOverride; the rest continue it. Every
        // paragraph of that run must therefore land on the SAME dedicated override w:numId — falling back to
        // the shared base numId would re-join them to the earlier list and renumber them in Word.
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("one", kind));
        doc.Blocks.Add(ListParagraph("two", kind));
        doc.Blocks.Add(ListParagraph("restart-anchor", kind, startOverride: 5));
        doc.Blocks.Add(ListParagraph("continuation", kind));

        var numIds = WrittenNumIds(doc);

        numIds.Should().HaveCount(4);
        numIds[1].Should().Be(numIds[0]);
        numIds[2].Should().NotBe(numIds[0]);
        numIds[3].Should().Be(numIds[2]);
    }

    [Fact]
    public void RestartedList_ContinuationParagraph_RoundTripsAsContinuation()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("one", ListKind.Number));
        doc.Blocks.Add(ListParagraph("restart-anchor", ListKind.Number, startOverride: 5));
        doc.Blocks.Add(ListParagraph("continuation", ListKind.Number));

        var paragraphs = RoundTrip(doc).Paragraphs.ToList();

        paragraphs.Should().HaveCount(3);
        paragraphs.Select(p => p.Formatting.ListKind).Should().AllBeEquivalentTo(ListKind.Number);
        paragraphs[0].Formatting.ListStartOverride.Should().BeNull();
        // The restart stays on its anchor alone: the continuation reads back as "continue", not as a second
        // restart at 5 (which would render 5, 5 instead of 5, 6).
        paragraphs[1].Formatting.ListStartOverride.Should().Be(5);
        paragraphs[2].Formatting.ListStartOverride.Should().BeNull();
    }

    [Fact]
    public void RestartedList_ImportedThenReExported_KeepsTheSameNumIdGrouping()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("one", ListKind.Number));
        doc.Blocks.Add(ListParagraph("two", ListKind.Number));
        doc.Blocks.Add(ListParagraph("restart-anchor", ListKind.Number, startOverride: 5));
        doc.Blocks.Add(ListParagraph("continuation", ListKind.Number));

        // Re-exporting an imported document must reproduce the same grouping, not collapse the restarted run
        // back onto the base list (the round trip is idempotent).
        var reExported = WrittenNumIds(RoundTrip(doc));

        reExported.Should().Equal(WrittenNumIds(doc));
    }

    [Fact]
    public void RestartedList_InterruptedByBodyText_ContinuesOnTheRestartNumId()
    {
        // An intervening non-list paragraph does not restart numbering (the render layer continues across it),
        // so the list paragraph after it stays on the active restart instance.
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("restart-anchor", ListKind.Number, startOverride: 7));
        doc.Blocks.Add(new Paragraph("interrupting body text"));
        doc.Blocks.Add(ListParagraph("continuation", ListKind.Number));

        var numIds = WrittenNumIds(doc);

        numIds.Should().HaveCount(2);
        numIds[1].Should().Be(numIds[0]);
    }

    [Fact]
    public void SecondRestart_StartsANewNumId_AndItsContinuationFollowsIt()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("restart at five", ListKind.Number, startOverride: 5));
        doc.Blocks.Add(ListParagraph("continuation of five", ListKind.Number));
        doc.Blocks.Add(ListParagraph("restart at nine", ListKind.Number, startOverride: 9));
        doc.Blocks.Add(ListParagraph("continuation of nine", ListKind.Number));

        var numIds = WrittenNumIds(doc);

        numIds[1].Should().Be(numIds[0]);
        numIds[2].Should().NotBe(numIds[0]);
        numIds[3].Should().Be(numIds[2]);
    }

    [Fact]
    public void TwoRunsRestartingAtTheSameNumber_AreSeparateListInstances()
    {
        // Both runs restart at 5, so they must be DIFFERENT numbering instances: sharing one w:numId would
        // make the second run continue the first (5, 6, 7, 8) instead of restarting (5, 6, 5, 6).
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("first restart", ListKind.Number, startOverride: 5));
        doc.Blocks.Add(ListParagraph("first continuation", ListKind.Number));
        doc.Blocks.Add(ListParagraph("second restart", ListKind.Number, startOverride: 5));
        doc.Blocks.Add(ListParagraph("second continuation", ListKind.Number));

        var numIds = WrittenNumIds(doc);

        numIds[1].Should().Be(numIds[0]);
        numIds[2].Should().NotBe(numIds[0]);
        numIds[3].Should().Be(numIds[2]);

        // …and the model round-trips with both restarts intact.
        var paragraphs = RoundTrip(doc).Paragraphs.ToList();
        paragraphs.Select(p => p.Formatting.ListStartOverride).Should().Equal(5, null, 5, null);
    }

    [Fact]
    public void RestartedNumberList_DoesNotCaptureABulletRunOrABareBulletList()
    {
        // Bullets have no restart overrides: they must keep the shared bullet numId even while a Number
        // restart is active, and must not disturb the Number run that resumes after them.
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("restart-anchor", ListKind.Number, startOverride: 4));
        doc.Blocks.Add(ListParagraph("bullet", ListKind.Bullet));
        doc.Blocks.Add(ListParagraph("continuation", ListKind.Number));

        var numIds = WrittenNumIds(doc);

        numIds[1].Should().NotBe(numIds[0]);
        numIds[2].Should().Be(numIds[0]);
    }

    [Fact]
    public void RestartedList_DoesNotLeakIntoTheHeaderStory()
    {
        // Stories are numbered independently: the header's own list starts on the base numId, it does not
        // inherit the restart instance the body left active.
        var doc = new TextDocument();
        doc.Blocks.Add(ListParagraph("body restart", ListKind.Number, startOverride: 6));
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter();
        doc.FinalSectionHeadersFooters.Header.Paragraphs.Add(ListParagraph("header item", ListKind.Number));

        var bodyNumIds = WrittenNumIds(doc);
        var headerNumIds = PartNumIds(doc, "word/header1.xml");

        bodyNumIds.Should().ContainSingle();
        headerNumIds.Should().ContainSingle();
        headerNumIds[0].Should().NotBe(bodyNumIds[0]);
    }

    [Fact]
    public void TableCellList_StartOverride_RoundTrips()
    {
        var doc = new TextDocument();
        var table = new Table();
        table.Rows.Add(new TableRow());
        table.Rows[0].Cells.Add(new TableCell());
        table.Rows[0].Cells[0].Paragraphs.Add(new Paragraph("restart in table")
        {
            Formatting = ParagraphFormatting.Default with
            {
                ListKind = ListKind.MultiLevel,
                ListLevel = 1,
                ListStartOverride = 3
            }
        });
        doc.Blocks.Add(table);

        var paragraph = RoundTrip(doc).Blocks.OfType<Table>().Single()
            .Rows.Single().Cells.Single().Paragraphs.Single();

        paragraph.Formatting.ListKind.Should().Be(ListKind.MultiLevel);
        paragraph.Formatting.ListLevel.Should().Be(1);
        paragraph.Formatting.ListStartOverride.Should().Be(3);
    }

    [Fact]
    public void NonListParagraph_HasNoListKind()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        RoundTrip(doc).Paragraphs.First().Formatting.ListKind.Should().Be(ListKind.None);
    }

    [Fact]
    public void List_WritesNumberingPartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("item")
        {
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet }
        });

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/numbering.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/numbering.xml");
        contentTypes.Should().Contain("application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("numbering.xml");
        rels.Should().Contain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("numPr");
    }

    [Fact]
    public void NoLists_OmitsNumberingPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/numbering.xml").Should().BeNull();
    }

    [Fact]
    public void Header_And_Footer_Text_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("Confidential Report");
        doc.Footer = new HeaderFooter("Company Inc.");

        var result = RoundTrip(doc);

        result.Header.Should().NotBeNull();
        result.Header!.PlainText.Should().Be("Confidential Report");
        result.Footer.Should().NotBeNull();
        result.Footer!.PlainText.Should().Be("Company Inc.");
    }

    [Fact]
    public void Footer_PageNumberField_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        var footer = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Page "));
        paragraph.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(paragraph);
        doc.Footer = footer;

        var result = RoundTrip(doc);

        result.Footer.Should().NotBeNull();
        var runs = result.Footer!.Paragraphs.Single().Runs;
        runs[0].Text.Should().Be("Page ");
        runs[0].FieldKind.Should().Be(RunFieldKind.None);
        runs[1].FieldKind.Should().Be(RunFieldKind.PageNumber);
    }

    [Theory]
    [InlineData(RunFieldKind.Date, "6/17/2026")]
    [InlineData(RunFieldKind.Time, "9:41 AM")]
    [InlineData(RunFieldKind.FileName, "Report.docx")]
    [InlineData(RunFieldKind.Author, "Ada Lovelace")]
    [InlineData(RunFieldKind.NumPages, "12")]
    public void DocumentField_RoundTrips_KindAndCachedText(RunFieldKind kind, string cached)
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Value: "));
        paragraph.Runs.Add(new Run(cached) { FieldKind = kind });
        doc.Blocks.Add(paragraph);

        var result = RoundTrip(doc);

        var runs = result.Paragraphs.Single().Runs;
        runs[0].Text.Should().Be("Value: ");
        runs[0].FieldKind.Should().Be(RunFieldKind.None);
        runs[1].FieldKind.Should().Be(kind);
        runs[1].Text.Should().Be(cached);
    }

    [Fact]
    public void DocumentField_Factories_RoundTrip()
    {
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.DateField("6/17/2026"));
        paragraph.Runs.Add(Run.TimeField("9:41 AM"));
        paragraph.Runs.Add(Run.FileNameField("Report.docx"));
        paragraph.Runs.Add(Run.AuthorField("Ada Lovelace"));
        paragraph.Runs.Add(Run.NumPagesField("12"));
        paragraph.Runs.Add(Run.PageNumberField());
        doc.Blocks.Add(paragraph);

        var runs = RoundTrip(doc).Paragraphs.Single().Runs;

        runs.Select(r => r.FieldKind).Should().Equal(
            RunFieldKind.Date, RunFieldKind.Time, RunFieldKind.FileName,
            RunFieldKind.Author, RunFieldKind.NumPages, RunFieldKind.PageNumber);
        runs[0].Text.Should().Be("6/17/2026");
        runs[1].Text.Should().Be("9:41 AM");
        runs[2].Text.Should().Be("Report.docx");
        runs[3].Text.Should().Be("Ada Lovelace");
        runs[4].Text.Should().Be("12");
        // PAGE keeps its historic "1" fallback.
        runs[5].Text.Should().Be("1");
    }

    [Fact]
    public void DocumentField_DateWithFormatSwitch_MapsByLeadingKeyword()
    {
        // A DATE field with a Word formatting switch in its instruction must still map back to Date.
        using var stream = new MemoryStream();
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        // Rewrite the document part, injecting a fldSimple with a switch, to exercise the reader path.
        var rewritten = InjectFieldInstruction(stream, " DATE \\@ \"d MMMM yyyy\" ", "17 June 2026");
        rewritten.Position = 0;
        var result = DocxReader.Read(rewritten);

        var fieldRun = result.Paragraphs.SelectMany(p => p.Runs)
            .Single(r => r.FieldKind != RunFieldKind.None);
        fieldRun.FieldKind.Should().Be(RunFieldKind.Date);
        fieldRun.Text.Should().Be("17 June 2026");
    }

    // Helper: rebuilds the docx in-memory, appending a paragraph carrying a w:fldSimple with the given
    // instruction + cached text, so a reader-only path (instruction switches) can be exercised.
    private static MemoryStream InjectFieldInstruction(Stream source, string instruction, string cached)
    {
        var output = new MemoryStream();
        source.CopyTo(output);
        output.Position = 0;
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("word/document.xml")!;
            string xml;
            using (var reader = new StreamReader(entry.Open()))
                xml = reader.ReadToEnd();

            const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var field = $"<w:p xmlns:w=\"{w}\"><w:fldSimple w:instr=\"{System.Security.SecurityElement.Escape(instruction)}\">" +
                        $"<w:r><w:t>{System.Security.SecurityElement.Escape(cached)}</w:t></w:r></w:fldSimple></w:p>";
            xml = xml.Replace("</w:body>", field + "</w:body>");

            entry.Delete();
            var fresh = archive.CreateEntry("word/document.xml");
            using var writer = new StreamWriter(fresh.Open());
            writer.Write(xml);
        }
        output.Position = 0;
        return output;
    }

    [Fact]
    public void HeaderFooter_RunFormatting_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        var header = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("Bold header", new RunFormatting { Bold = true, FontSizePt = 14 }));
        header.Paragraphs.Add(paragraph);
        doc.Header = header;

        var formatting = RoundTrip(doc).Header!.Paragraphs.Single().Runs[0].Formatting;

        formatting.Bold.Should().BeTrue();
        formatting.FontSizePt.Should().Be(14);
    }

    [Fact]
    public void HeaderFooter_Package_HasPartsContentTypesAndRelationships()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter("Header text");
        var footer = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(paragraph);
        doc.Footer = footer;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/header1.xml").Should().NotBeNull();
        zip.GetEntry("word/footer1.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/header1.xml");
        contentTypes.Should().Contain("/word/footer1.xml");
        contentTypes.Should().Contain("wordprocessingml.header+xml");
        contentTypes.Should().Contain("wordprocessingml.footer+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("relationships/header");
        rels.Should().Contain("relationships/footer");
        rels.Should().Contain("header1.xml");
        rels.Should().Contain("footer1.xml");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().Contain("headerReference");
        documentXml.Should().Contain("footerReference");

        using var footerReader = new StreamReader(zip.GetEntry("word/footer1.xml")!.Open());
        var footerXml = footerReader.ReadToEnd();
        footerXml.Should().Contain("fldSimple");
        footerXml.Should().Contain(" PAGE ");
    }

    [Fact]
    public void EmptyHeaderFooter_DoesNotEmitParts()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.Header = new HeaderFooter();  // no paragraphs => empty
        doc.Footer = null;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/header1.xml").Should().BeNull();
        zip.GetEntry("word/footer1.xml").Should().BeNull();
    }

    [Fact]
    public void NoHeaderFooter_RoundTripsAsNull()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        var result = RoundTrip(doc);

        result.Header.Should().BeNull();
        result.Footer.Should().BeNull();
    }

    [Fact]
    public void Read_NonWordZip_Throws()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            zip.CreateEntry("not-a-document.txt");
        stream.Position = 0;

        var read = () => DocxReader.Read(stream);
        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Footnote_Reference_And_Content_RoundTrip()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("See note"));
        body.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(body);
        doc.Footnotes[1] = new Footnote(1, "The footnote text.");

        var result = RoundTrip(doc);

        // The body reference run keeps its id and renders as a superscript marker.
        var reference = result.Paragraphs.First().Runs.Single(r => r.FootnoteId is not null);
        reference.FootnoteId.Should().Be(1);
        reference.Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);

        // The footnote content is recovered intact.
        result.Footnotes.Should().ContainKey(1);
        result.Footnotes[1].PlainText.Should().Be("The footnote text.");
        result.Footnotes[1].HasAutomaticReferenceMark.Should().BeTrue();
    }

    [Fact]
    public void Footnotes_Package_HasPartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Body"));
        body.Runs.Add(Run.FootnoteReference(1));
        doc.Blocks.Add(body);
        doc.Footnotes[1] = new Footnote(1, "A footnote.");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/footnotes.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/footnotes.xml");
        contentTypes.Should().Contain("wordprocessingml.footnotes+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("relationships/footnotes");
        rels.Should().Contain("footnotes.xml");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().Contain("footnoteReference");

        using var footnotesReader = new StreamReader(zip.GetEntry("word/footnotes.xml")!.Open());
        var footnotesXml = footnotesReader.ReadToEnd();
        footnotesXml.Should().Contain("A footnote.");
        footnotesXml.Should().Contain("w:id=\"1\"");
        footnotesXml.Should().Contain("footnoteRef");
    }

    [Fact]
    public void NoFootnotes_DoesNotEmitPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/footnotes.xml").Should().BeNull();

        DocxReader.Read(new MemoryStream(stream.ToArray())).Footnotes.Should().BeEmpty();
    }

    [Fact]
    public void Endnote_Reference_And_Content_RoundTrip()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("See note"));
        body.Runs.Add(Run.EndnoteReference(1));
        doc.Blocks.Add(body);
        doc.Endnotes[1] = new Endnote(1, "The endnote text.");

        var result = RoundTrip(doc);

        // The body reference run keeps its id and renders as a superscript marker.
        var reference = result.Paragraphs.First().Runs.Single(r => r.EndnoteId is not null);
        reference.EndnoteId.Should().Be(1);
        reference.Formatting.VerticalAlign.Should().Be(VerticalAlign.Superscript);

        // The endnote content is recovered intact.
        result.Endnotes.Should().ContainKey(1);
        result.Endnotes[1].PlainText.Should().Be("The endnote text.");
        result.Endnotes[1].HasAutomaticReferenceMark.Should().BeTrue();
    }

    [Fact]
    public void Endnotes_Package_HasPartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Body"));
        body.Runs.Add(Run.EndnoteReference(1));
        doc.Blocks.Add(body);
        doc.Endnotes[1] = new Endnote(1, "An endnote.");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/endnotes.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/endnotes.xml");
        contentTypes.Should().Contain("wordprocessingml.endnotes+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("relationships/endnotes");
        rels.Should().Contain("endnotes.xml");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().Contain("endnoteReference");

        using var endnotesReader = new StreamReader(zip.GetEntry("word/endnotes.xml")!.Open());
        var endnotesXml = endnotesReader.ReadToEnd();
        endnotesXml.Should().Contain("An endnote.");
        endnotesXml.Should().Contain("w:id=\"1\"");
        endnotesXml.Should().Contain("endnoteRef");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoteBody_WithoutAutomaticReferenceMark_RoundTripsItsAbsence(bool endnote)
    {
        var document = new TextDocument();
        document.Blocks.Add(new Paragraph("Body"));
        if (endnote)
        {
            document.Endnotes[1] = new Endnote(1, "Authored endnote text.")
            {
                HasAutomaticReferenceMark = false
            };
        }
        else
        {
            document.Footnotes[1] = new Footnote(1, "Authored footnote text.")
            {
                HasAutomaticReferenceMark = false
            };
        }

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        var bytes = stream.ToArray();
        var partName = endnote ? "word/endnotes.xml" : "word/footnotes.xml";
        var noteElementName = endnote ? "endnote" : "footnote";
        var referenceElementName = endnote ? "endnoteRef" : "footnoteRef";
        var word = (XNamespace)"http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        using (var part = archive.GetEntry(partName)!.Open())
        {
            var xml = XDocument.Load(part);
            var note = xml.Root!.Elements(word + noteElementName)
                .Single(element => element.Attribute(word + "id")?.Value == "1");
            note.Descendants(word + referenceElementName).Should().BeEmpty();
        }

        var reopened = DocxReader.Read(new MemoryStream(bytes));
        var hasAutomaticReferenceMark = endnote
            ? reopened.Endnotes[1].HasAutomaticReferenceMark
            : reopened.Footnotes[1].HasAutomaticReferenceMark;
        hasAutomaticReferenceMark.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoteImages_UseOwningPartRelationshipsAndRoundTrip(bool endnote)
    {
        var imageBytes = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
        var document = new TextDocument();
        var body = new Paragraph("Body");
        body.Runs.Add(endnote ? Run.EndnoteReference(1) : Run.FootnoteReference(1));
        document.Blocks.Add(body);
        var content = new Paragraph();
        content.Runs.Add(new Run("Illustrated note "));
        content.Runs.Add(Run.FromImage(new InlineImage(imageBytes, 24, 18)));
        if (endnote)
        {
            var note = new Endnote(1);
            note.Content.Add(content);
            document.Endnotes[1] = note;
        }
        else
        {
            var note = new Footnote(1);
            note.Content.Add(content);
            document.Footnotes[1] = note;
        }

        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        var bytes = stream.ToArray();
        var partName = endnote ? "endnotes" : "footnotes";
        var mediaName = endnote ? "endnote_image1.png" : "footnote_image1.png";
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            zip.GetEntry("word/media/" + mediaName).Should().NotBeNull();
            using var rels = new StreamReader(zip.GetEntry("word/_rels/" + partName + ".xml.rels")!.Open());
            var relsXml = rels.ReadToEnd();
            relsXml.Should().Contain("relationships/image");
            relsXml.Should().Contain("media/" + mediaName);

            using var noteXml = new StreamReader(zip.GetEntry("word/" + partName + ".xml")!.Open());
            noteXml.ReadToEnd().Should().Contain("rIdImg1");
        }

        var read = DocxReader.Read(new MemoryStream(bytes));
        var image = (endnote ? read.Endnotes[1].Content : read.Footnotes[1].Content)
            .SelectMany(paragraph => paragraph.Runs)
            .Single(run => run.Image is not null)
            .Image!;
        image.Bytes.Should().Equal(imageBytes);
        image.WidthPt.Should().Be(24);
        image.HeightPt.Should().Be(18);
    }

    [Fact]
    public void NoEndnotes_DoesNotEmitPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/endnotes.xml").Should().BeNull();

        DocxReader.Read(new MemoryStream(stream.ToArray())).Endnotes.Should().BeEmpty();
    }

    [Fact]
    public void Footnotes_And_Endnotes_CoexistAndRoundTrip()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Text"));
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(Run.EndnoteReference(1));
        doc.Blocks.Add(body);
        doc.Footnotes[1] = new Footnote(1, "A footnote.");
        doc.Endnotes[1] = new Endnote(1, "An endnote.");

        var result = RoundTrip(doc);

        result.Footnotes.Should().ContainKey(1);
        result.Footnotes[1].PlainText.Should().Be("A footnote.");
        result.Endnotes.Should().ContainKey(1);
        result.Endnotes[1].PlainText.Should().Be("An endnote.");

        var runs = result.Paragraphs.First().Runs;
        runs.Should().ContainSingle(r => r.FootnoteId == 1);
        runs.Should().ContainSingle(r => r.EndnoteId == 1);
    }

    [Fact]
    public void Comment_Range_And_Content_RoundTrip()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Before "));
        body.Runs.Add(new Run("commented text") { CommentId = 0 });
        body.Runs.Add(Run.CommentReference(0));
        body.Runs.Add(new Run(" after"));
        doc.Blocks.Add(body);
        doc.Comments[0] = new Comment(0, "A reviewer note.", author: "Alice Adams", initials: "AA")
        {
            DateXml = "2026-06-17T10:30:00Z"
        };

        var result = RoundTrip(doc);

        // The covered text run keeps its comment id; the reference anchor is recovered as a textless run.
        var paragraph = result.Paragraphs.First();
        var covered = paragraph.Runs.Single(r => r.CommentId is not null && !r.IsCommentReference);
        covered.Text.Should().Be("commented text");
        covered.CommentId.Should().Be(0);
        var reference = paragraph.Runs.Single(r => r.IsCommentReference);
        reference.CommentId.Should().Be(0);

        // The surrounding text is untouched and the comment content/metadata is recovered intact.
        paragraph.PlainText.Should().Be("Before commented text after");
        result.Comments.Should().ContainKey(0);
        var comment = result.Comments[0];
        comment.PlainText.Should().Be("A reviewer note.");
        comment.Author.Should().Be("Alice Adams");
        comment.Initials.Should().Be("AA");
        comment.DateXml.Should().Be("2026-06-17T10:30:00Z");
    }

    [Fact]
    public void Comments_Package_HasPartContentTypeAndRelationship()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Reviewed") { CommentId = 0 });
        body.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(body);
        doc.Comments[0] = new Comment(0, "Needs work.", author: "Bob", initials: "B");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/comments.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/comments.xml");
        contentTypes.Should().Contain("wordprocessingml.comments+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("relationships/comments");
        rels.Should().Contain("comments.xml");

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().Contain("commentRangeStart");
        documentXml.Should().Contain("commentRangeEnd");
        documentXml.Should().Contain("commentReference");

        using var commentsReader = new StreamReader(zip.GetEntry("word/comments.xml")!.Open());
        var commentsXml = commentsReader.ReadToEnd();
        commentsXml.Should().Contain("Needs work.");
        commentsXml.Should().Contain("w:id=\"0\"");
        commentsXml.Should().Contain("w:author=\"Bob\"");
    }

    [Fact]
    public void NoComments_DoesNotEmitPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/comments.xml").Should().BeNull();

        DocxReader.Read(new MemoryStream(stream.ToArray())).Comments.Should().BeEmpty();
    }

    [Fact]
    public void CommentDate_Unset_IsNotEmitted()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Reviewed") { CommentId = 0 });
        body.Runs.Add(Run.CommentReference(0));
        doc.Blocks.Add(body);
        doc.Comments[0] = new Comment(0, "No date.", author: "C", initials: "C");

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var commentsReader = new StreamReader(zip.GetEntry("word/comments.xml")!.Open());
        var commentsXml = commentsReader.ReadToEnd();
        commentsXml.Should().NotContain("w:date");

        // A comment with no date round-trips with DateXml null.
        DocxReader.Read(new MemoryStream(stream.ToArray())).Comments[0].DateXml.Should().BeNull();
    }

    [Fact]
    public void DefaultDocument_StaysSingleColumn()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Single column body."));

        var result = RoundTrip(doc);

        result.Page.ColumnCount.Should().Be(1);
        // The default column spacing (36 pt) survives the dxa round-trip exactly.
        result.Page.ColumnSpacingPt.Should().BeApproximately(36, 0.001);
    }

    [Theory]
    [InlineData(2, 24)]
    [InlineData(3, 18)]
    public void MultiColumnPage_RoundTripsCountAndSpacing(int columns, double spacingPt)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Multi-column body text that flows across the page."));
        doc.Page.ColumnCount = columns;
        doc.Page.ColumnSpacingPt = spacingPt;

        var result = RoundTrip(doc);

        result.Page.ColumnCount.Should().Be(columns);
        result.Page.ColumnSpacingPt.Should().BeApproximately(spacingPt, 0.001);
    }

    [Fact]
    public void MultiColumnPage_EmitsColsElementInSectPr()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Two columns."));
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnSpacingPt = 36;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();

        // w:cols carries the column count and the spacing as dxa (36 pt -> 720 twentieths of a point).
        documentXml.Should().Contain("w:num=\"2\"");
        documentXml.Should().Contain("w:space=\"720\"");
    }

    [Fact]
    public void ColumnsLineBetween_RoundTripsAndEmitsSep()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Two columns with a divider line."));
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnsLineBetween = true;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
            reader.ReadToEnd().Should().Contain("w:sep=\"1\"");

        var result = RoundTrip(doc);
        result.Page.ColumnsLineBetween.Should().BeTrue();
    }

    [Fact]
    public void UnequalColumns_RoundTripWidthsAndEqualWidthOff()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("A narrow column beside a wide one (Word's Left preset)."));
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnSpacingPt = 36;
        doc.Page.ColumnWidthsPt = [108.0, 360.0];

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
        using (var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()))
        {
            var xml = reader.ReadToEnd();
            xml.Should().Contain("w:equalWidth=\"0\"");
            // 108 pt -> 2160 dxa, 360 pt -> 7200 dxa.
            xml.Should().Contain("w:w=\"2160\"");
            xml.Should().Contain("w:w=\"7200\"");
        }

        var result = RoundTrip(doc);
        result.Page.ColumnCount.Should().Be(2);
        result.Page.ColumnWidthsPt.Should().NotBeNull();
        result.Page.ColumnWidthsPt![0].Should().BeApproximately(108, 0.001);
        result.Page.ColumnWidthsPt![1].Should().BeApproximately(360, 0.001);
    }

    // S7 regression — a <w:cols w:equalWidth="0"><w:col w:w="..."/></w:cols> with a SINGLE explicit-width
    // column was silently treated as equal-width (the old guard required colElements.Count > 1). The width
    // was dropped and ColumnWidthsPt stayed null. The fix relaxes the guard to >= 1.
    [Fact]
    public void SingleExplicitWidthColumn_EqualWidthOff_ReadsWidth()
    {
        // Build a minimal package whose sectPr has one explicit w:col with w:equalWidth="0".
        // 4320 dxa = 216 pt (1.5 inches in Word's default 1440-dxa-per-inch scale).
        var stream = BuildSingleExplicitColPackage(colWidthDxa: 4320);
        stream.Position = 0;
        var doc = DocxReader.Read(stream);

        doc.Page.ColumnWidthsPt.Should().NotBeNull(
            because: "a single w:col with w:equalWidth=0 must be captured as an explicit-width column");
        doc.Page.ColumnWidthsPt!.Should().HaveCount(1);
        doc.Page.ColumnWidthsPt![0].Should().BeApproximately(216.0, 0.001,
            because: "4320 dxa = 216 pt (4320 / 20)");
    }

    private static MemoryStream BuildSingleExplicitColPackage(int colWidthDxa)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path);
                using var w = new StreamWriter(entry.Open());
                w.Write(xml);
            }

            Add("[Content_Types].xml",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                </Types>
                """);

            Add("_rels/.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """);

            Add("word/_rels/document.xml.rels",
                """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"/>
                """);

            Add("word/document.xml",
                $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                  <w:body>
                    <w:p><w:r><w:t>Body</w:t></w:r></w:p>
                    <w:sectPr>
                      <w:cols w:equalWidth="0">
                        <w:col w:w="{colWidthDxa}"/>
                      </w:cols>
                    </w:sectPr>
                  </w:body>
                </w:document>
                """);
        }
        return stream;
    }

    [Fact]
    public void InsertedRun_RoundTrips_KindAuthorAndDate()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Before "));
        body.Runs.Add(new Run("added text")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice Adams",
            RevisionDateXml = "2026-06-17T10:30:00Z"
        });
        body.Runs.Add(new Run(" after"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var paragraph = result.Paragraphs.First();
        paragraph.PlainText.Should().Be("Before added text after");

        var inserted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Inserted);
        inserted.Text.Should().Be("added text");
        inserted.RevisionAuthor.Should().Be("Alice Adams");
        inserted.RevisionDateXml.Should().Be("2026-06-17T10:30:00Z");

        // The surrounding text keeps no revision mark.
        paragraph.Runs.Where(r => r.Text is "Before " or " after").Should().OnlyContain(r => r.Revision == RevisionKind.None);
    }

    [Fact]
    public void DeletedRun_RoundTrips_AsDelTextWithKindAndAuthor()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Keep "));
        body.Runs.Add(new Run("removed text")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob Brown",
            RevisionDateXml = "2026-06-17T11:00:00Z"
        });
        doc.Blocks.Add(body);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        // The deleted text serialises inside a w:del wrapper using w:delText (not w:t).
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var documentXml = docReader.ReadToEnd();
            documentXml.Should().Contain("<w:del");
            documentXml.Should().Contain("w:delText");
            documentXml.Should().Contain("w:author=\"Bob Brown\"");
        }

        stream.Position = 0;
        var result = DocxReader.Read(stream);

        var paragraph = result.Paragraphs.First();
        // The deleted text is kept in the model (struck through, not dropped).
        paragraph.PlainText.Should().Be("Keep removed text");
        var deleted = paragraph.Runs.Single(r => r.Revision == RevisionKind.Deleted);
        deleted.Text.Should().Be("removed text");
        deleted.RevisionAuthor.Should().Be("Bob Brown");
        deleted.RevisionDateXml.Should().Be("2026-06-17T11:00:00Z");
    }

    [Fact]
    public void NoRevisions_DoesNotEmitInsOrDel()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().NotContain("<w:ins");
        documentXml.Should().NotContain("<w:del");
    }

    [Fact]
    public void PlainTextContentControl_RoundTrips_KindTagAndText()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Before "));
        body.Runs.Add(Run.PlainTextControl("editable content", tag: "FullName", alias: "Full name"));
        body.Runs.Add(new Run(" after"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var paragraph = result.Paragraphs.First();
        paragraph.PlainText.Should().Be("Before editable content after");

        var control = paragraph.Runs.Single(r => r.Control is not null);
        control.Text.Should().Be("editable content");
        control.Control!.Kind.Should().Be(ContentControlKind.PlainText);
        control.Control.Tag.Should().Be("FullName");
        control.Control.Alias.Should().Be("Full name");
        control.Control.Checked.Should().BeFalse();

        // The surrounding text carries no control mark.
        paragraph.Runs.Where(r => r.Text is "Before " or " after").Should().OnlyContain(r => r.Control == null);
    }

    [Theory]
    [InlineData("unlocked", ContentControlLockMode.Unlocked)]
    [InlineData("contentLocked", ContentControlLockMode.ContentLocked)]
    [InlineData("sdtLocked", ContentControlLockMode.ControlLocked)]
    [InlineData("sdtContentLocked", ContentControlLockMode.ControlAndContentLocked)]
    public void InlineContentControlLock_ReadsWritesAndSurvivesReopen(
        string token,
        ContentControlLockMode expected)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var source = ReadHandAuthoredDocx(
            $$"""
            <w:p>
              <w:sdt>
                <w:sdtPr><w:lock w:val="{{token}}"/><w:text/></w:sdtPr>
                <w:sdtContent><w:r><w:t>locked text</w:t></w:r></w:sdtContent>
              </w:sdt>
            </w:p>
            """);

        source.Paragraphs.Single().Runs.Single().Control!.LockMode.Should().Be(expected);
        var xml = WriteDocumentXml(source);
        xml.Descendants(w + "sdtPr").Single().Element(w + "lock")!
            .Attribute(w + "val")!.Value.Should().Be(token);
        RoundTrip(source).Paragraphs.Single().Runs.Single().Control!.LockMode.Should().Be(expected);
    }

    [Fact]
    public void InlineContentControlWithoutLock_OmitsLockToken()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var doc = new TextDocument();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(Run.PlainTextControl("editable"));
        doc.Blocks.Add(paragraph);

        WriteDocumentXml(doc).Descendants(w + "lock").Should().BeEmpty();
        RoundTrip(doc).Paragraphs.Single().Runs.Single().Control!.LockMode
            .Should().Be(ContentControlLockMode.NotSpecified);
    }

    [Fact]
    public void DataBoundInlineContentControl_PreservesWordMetadataAcrossTextEditAndSecondSave()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace w15 = "http://schemas.microsoft.com/office/word/2012/wordml";
        const string storeItemId = "{11111111-2222-3333-4444-555555555555}";
        var document = ReadHandAuthoredDocx(
            $$"""
            <w:p>
              <w:sdt xmlns:w15="http://schemas.microsoft.com/office/word/2012/wordml">
                <w:sdtPr>
                  <w:alias w:val="Bound name"/>
                  <w:placeholder><w:docPart w:val="DefaultPlaceholder_1"/></w:placeholder>
                  <w:showingPlcHdr/>
                  <w:dataBinding w:prefixMappings="xmlns:ns0='urn:freew:test'" w:xpath="/ns0:root/ns0:name" w:storeItemID="{{storeItemId}}"/>
                  <w:temporary/>
                  <w:id w:val="-123456"/>
                  <w:tag w:val="BoundName"/>
                  <w15:color w15:val="2E74B5"/>
                  <w15:appearance w15:val="boundingBox"/>
                  <w:text/>
                </w:sdtPr>
                <w:sdtContent><w:r><w:t>placeholder</w:t></w:r></w:sdtContent>
              </w:sdt>
            </w:p>
            """);

        var run = document.Paragraphs.Single().Runs.Single();
        var metadata = run.Control!.WordMetadata!;
        metadata.Id.Should().Be("-123456");
        metadata.DataBinding.Should().Be(new ContentControlDataBinding(
            storeItemId,
            "/ns0:root/ns0:name",
            "xmlns:ns0='urn:freew:test'"));
        metadata.PlaceholderDocPart.Should().Be("DefaultPlaceholder_1");
        metadata.ShowingPlaceholder.Should().BeTrue();
        metadata.Temporary.Should().BeTrue();
        metadata.Color.Should().Be("2E74B5");
        metadata.Appearance.Should().Be("boundingBox");

        run.Text = "Edited in FreeW";
        var firstXml = WriteDocumentXml(document);
        var sdtPr = firstXml.Descendants(w + "sdtPr").Single();
        sdtPr.Elements(w + "dataBinding").Should().ContainSingle();
        sdtPr.Element(w + "dataBinding")!.Attribute(w + "storeItemID")!.Value.Should().Be(storeItemId);
        sdtPr.Element(w + "id")!.Attribute(w + "val")!.Value.Should().Be("-123456");
        sdtPr.Element(w15 + "appearance")!.Attribute(w15 + "val")!.Value.Should().Be("boundingBox");

        var reopened = RoundTrip(document);
        reopened.Paragraphs.Single().Runs.Single().Text.Should().Be("Edited in FreeW");
        reopened.Paragraphs.Single().Runs.Single().Control!.WordMetadata.Should().Be(metadata);
        var secondSdtPr = WriteDocumentXml(reopened).Descendants(w + "sdtPr").Single();
        secondSdtPr.ToString(SaveOptions.DisableFormatting)
            .Should().Be(sdtPr.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void DataBoundBlockContentControl_PreservesBindingAndIdentity()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var document = ReadHandAuthoredDocx(
            """
            <w:sdt>
              <w:sdtPr>
                <w:dataBinding w:xpath="/root/value" w:storeItemID="{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}"/>
                <w:id w:val="42"/>
                <w:tag w:val="BoundBlock"/>
                <w:richText/>
              </w:sdtPr>
              <w:sdtContent><w:p><w:r><w:t>block value</w:t></w:r></w:p></w:sdtContent>
            </w:sdt>
            """);

        var control = document.Blocks.Single().BlockContentControl!;
        control.WordMetadata!.Id.Should().Be("42");
        control.WordMetadata.DataBinding.Should().Be(new ContentControlDataBinding(
            "{AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE}",
            "/root/value",
            null));

        document.Paragraphs.Single().Runs.Single().Text = "edited block value";
        var reopened = RoundTrip(document);
        reopened.Paragraphs.Single().PlainText.Should().Be("edited block value");
        reopened.Blocks.Single().BlockContentControl!.WordMetadata.Should().Be(control.WordMetadata);
        WriteDocumentXml(reopened).Descendants(w + "dataBinding").Should().ContainSingle();
    }

    [Theory]
    [InlineData(RevisionKind.Inserted)]
    [InlineData(RevisionKind.Deleted)]
    public void RevisedContentControl_RoundTrips_ControlAndRevision(RevisionKind revisionKind)
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        var control = Run.PlainTextControl("tracked control", tag: "Tracked", alias: "Tracked control");
        control.Revision = revisionKind;
        control.RevisionAuthor = "Alex Editor";
        control.RevisionDateXml = "2026-06-19T08:00:00Z";
        body.Runs.Add(control);
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var run = result.Paragraphs.First().Runs.Single();
        run.Text.Should().Be("tracked control");
        run.Control.Should().NotBeNull();
        run.Control!.Kind.Should().Be(ContentControlKind.PlainText);
        run.Control.Tag.Should().Be("Tracked");
        run.Revision.Should().Be(revisionKind);
        run.RevisionAuthor.Should().Be("Alex Editor");
        run.RevisionDateXml.Should().Be("2026-06-19T08:00:00Z");
    }

    [Fact]
    public void BlockLevelContentControl_ReadsContainedParagraph()
    {
        var result = ReadHandAuthoredDocx(
            """
            <w:sdt>
              <w:sdtPr><w:tag w:val="BlockControl"/><w:richText/></w:sdtPr>
              <w:sdtContent>
                <w:p><w:r><w:t>block control text</w:t></w:r></w:p>
              </w:sdtContent>
            </w:sdt>
            """);

        var run = result.Paragraphs.Should().ContainSingle().Subject.Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be("block control text");
        run.Control.Should().BeNull();
        var paragraphControl = result.Blocks.Should().ContainSingle().Subject.BlockContentControl;
        paragraphControl.Should().NotBeNull();
        paragraphControl!.Kind.Should().Be(BlockContentControlKind.RichText);
        paragraphControl.Tag.Should().Be("BlockControl");
    }

    [Fact]
    public void BlockLevelContentControlLock_RoundTrips()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var control = new BlockContentControl(
            BlockContentControlKind.RichText,
            Tag: "LockedBlock",
            LockMode: ContentControlLockMode.ControlAndContentLocked);
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("locked block") { BlockContentControl = control });

        var xml = WriteDocumentXml(doc);
        xml.Descendants(w + "sdtPr").Single().Element(w + "lock")!
            .Attribute(w + "val")!.Value.Should().Be("sdtContentLocked");
        RoundTrip(doc).Blocks.Single().BlockContentControl!.LockMode
            .Should().Be(ContentControlLockMode.ControlAndContentLocked);
    }

    [Fact]
    public void TableCellContentControl_ReadsContainedParagraph()
    {
        var result = ReadHandAuthoredDocx(
            """
            <w:tbl>
              <w:tr>
                <w:tc>
                  <w:sdt>
                    <w:sdtPr><w:tag w:val="CellControl"/></w:sdtPr>
                    <w:sdtContent>
                      <w:p><w:r><w:t>cell control text</w:t></w:r></w:p>
                    </w:sdtContent>
                  </w:sdt>
                </w:tc>
              </w:tr>
            </w:tbl>
            """);

        var table = result.Blocks.OfType<Table>().Should().ContainSingle().Subject;
        var run = table.Rows[0].Cells[0].Paragraphs.Should().ContainSingle().Subject.Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be("cell control text");
        run.Control.Should().NotBeNull();
        run.Control!.Kind.Should().Be(ContentControlKind.PlainText);
        run.Control.Tag.Should().Be("CellControl");
    }

    [Fact]
    public void ContentControlWrappedHyperlinkRevision_ReadsTextControlLinkAndRevision()
    {
        var rels =
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdLink" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink" Target="https://example.com" TargetMode="External"/>
            </Relationships>
            """;
        var result = ReadHandAuthoredDocx(
            """
            <w:p>
              <w:sdt>
                <w:sdtPr><w:tag w:val="TrackedLink"/></w:sdtPr>
                <w:sdtContent>
                  <w:hyperlink r:id="rIdLink">
                    <w:ins w:author="Alex Editor" w:date="2026-06-19T08:00:00Z">
                      <w:r><w:t>linked tracked control</w:t></w:r>
                    </w:ins>
                  </w:hyperlink>
                </w:sdtContent>
              </w:sdt>
            </w:p>
            """,
            rels);

        var run = result.Paragraphs.Should().ContainSingle().Subject.Runs.Should().ContainSingle().Subject;
        run.Text.Should().Be("linked tracked control");
        run.Control.Should().NotBeNull();
        run.Control!.Tag.Should().Be("TrackedLink");
        run.HyperlinkUrl.Should().Be("https://example.com");
        run.Revision.Should().Be(RevisionKind.Inserted);
        run.RevisionAuthor.Should().Be("Alex Editor");
        run.RevisionDateXml.Should().Be("2026-06-19T08:00:00Z");
    }

    [Theory]
    [InlineData(true, "☒")]
    [InlineData(false, "☐")]
    public void CheckBoxContentControl_RoundTrips_CheckedState(bool isChecked, string glyph)
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Agree: "));
        body.Runs.Add(Run.CheckBoxControl(isChecked, tag: "Agree", alias: "I agree"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var control = result.Paragraphs.First().Runs.Single(r => r.Control is { Kind: ContentControlKind.CheckBox });
        control.Control!.Checked.Should().Be(isChecked);
        control.Text.Should().Be(glyph);
        control.Control.Tag.Should().Be("Agree");
        control.Control.Alias.Should().Be("I agree");
    }

    [Fact]
    public void RichTextContentControl_RoundTrips_KindTagAndText()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(new Run("Before "));
        body.Runs.Add(Run.RichTextControl("rich content", tag: "Bio", alias: "Biography"));
        body.Runs.Add(new Run(" after"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var paragraph = result.Paragraphs.First();
        paragraph.PlainText.Should().Be("Before rich content after");

        var control = paragraph.Runs.Single(r => r.Control is not null);
        control.Text.Should().Be("rich content");
        control.Control!.Kind.Should().Be(ContentControlKind.RichText);
        control.Control.Tag.Should().Be("Bio");
        control.Control.Alias.Should().Be("Biography");
    }

    [Fact]
    public void DatePickerContentControl_RoundTrips_DateFormatAndText()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(Run.DatePickerControl("2026-06-19", tag: "Signed", alias: "Signed on", dateFormat: "yyyy-MM-dd"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var control = result.Paragraphs.First().Runs.Single(r => r.Control is { Kind: ContentControlKind.DatePicker });
        control.Text.Should().Be("2026-06-19");
        control.Control!.Tag.Should().Be("Signed");
        control.Control.Alias.Should().Be("Signed on");
        control.Control.DateFormat.Should().Be("yyyy-MM-dd");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ListContentControl_RoundTrips_ItemsAndSelection(bool combo)
    {
        var items = new[]
        {
            new ContentControlListItem("Red", "R"),
            new ContentControlListItem("Green", "G"),
            new ContentControlListItem("Blue", "B")
        };

        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(combo
            ? Run.ComboBoxControl(items, selectedText: "Green", tag: "Color", alias: "Favourite colour")
            : Run.DropDownListControl(items, selectedText: "Green", tag: "Color", alias: "Favourite colour"));
        doc.Blocks.Add(body);

        var result = RoundTrip(doc);

        var control = result.Paragraphs.First().Runs.Single(r => r.Control is not null);
        control.Control!.Kind.Should().Be(combo ? ContentControlKind.ComboBox : ContentControlKind.DropDownList);
        control.Text.Should().Be("Green");
        control.Control.Tag.Should().Be("Color");
        control.Control.Alias.Should().Be("Favourite colour");
        control.Control.Items.Should().HaveCount(3);
        control.Control.Items.Select(i => i.DisplayText).Should().ContainInOrder("Red", "Green", "Blue");
        control.Control.Items.Select(i => i.Value).Should().ContainInOrder("R", "G", "B");
    }

    [Fact]
    public void BlockLevelBibliographyContentControl_ReadsWordSdtWithoutFlatteningRuns()
    {
        var doc = ReadHandAuthoredDocx(
            """
            <w:sdt>
              <w:sdtPr>
                <w:alias w:val="Bibliography"/>
                <w:tag w:val="Bibliography"/>
                <w:docPartObj>
                  <w:docPartGallery w:val="Bibliographies"/>
                  <w:docPartUnique/>
                </w:docPartObj>
              </w:sdtPr>
              <w:sdtContent>
                <w:p><w:r><w:t>References</w:t></w:r></w:p>
                <w:tbl>
                  <w:tr>
                    <w:tc><w:p><w:r><w:t>Structured entry</w:t></w:r></w:p></w:tc>
                  </w:tr>
                </w:tbl>
              </w:sdtContent>
            </w:sdt>
            """);

        doc.Blocks.Should().HaveCount(2);
        var heading = doc.Blocks[0].Should().BeOfType<Paragraph>().Subject;
        heading.PlainText.Should().Be("References");
        heading.Runs.Should().OnlyContain(run => run.Control == null);

        heading.BlockContentControl.Should().NotBeNull();
        var control = heading.BlockContentControl!;
        control.Kind.Should().Be(BlockContentControlKind.Bibliography);
        control.Tag.Should().Be("Bibliography");
        control.Alias.Should().Be("Bibliography");
        control.DocPartGallery.Should().Be(BlockContentControl.BibliographyGallery);
        control.DocPartUnique.Should().BeTrue();

        var table = doc.Blocks[1].Should().BeOfType<Table>().Subject;
        table.Rows[0].Cells[0].PlainText.Should().Be("Structured entry");
        ReferenceEquals(table.BlockContentControl, control).Should().BeTrue();
    }

    [Fact]
    public void BlockLevelBibliographyContentControl_RoundTripsAsOuterSdt()
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var control = BlockContentControl.BibliographyRegion();
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Before"));
        doc.Blocks.Add(new Paragraph("References")
        {
            StyleId = Citations.HeadingStyleId,
            BlockContentControl = control,
        });
        doc.Blocks.Add(new Paragraph("Doe. (2024). Structured Work.")
        {
            StyleId = Citations.EntryStyleId,
            BlockContentControl = control,
        });
        doc.Blocks.Add(new Paragraph("After"));

        var documentXml = WriteDocumentXml(doc);
        var sdt = documentXml.Root!.Element(w + "body")!.Elements(w + "sdt").Should().ContainSingle().Subject;
        var sdtPr = sdt.Element(w + "sdtPr")!;
        sdtPr.Element(w + "docPartObj")!
            .Element(w + "docPartGallery")!
            .Attribute(w + "val")!.Value.Should().Be(BlockContentControl.BibliographyGallery);
        sdtPr.Element(w + "docPartObj")!.Element(w + "docPartUnique").Should().NotBeNull();
        sdt.Element(w + "sdtContent")!.Elements(w + "p").Should().HaveCount(2);
        sdt.Descendants(w + "sdt").Should().BeEmpty("the bibliography is a block-level wrapper, not run-level controls");

        var result = RoundTrip(doc);
        result.Blocks.Select(BlockPlainText).Should().Equal(
            "Before",
            "References",
            "Doe. (2024). Structured Work.",
            "After");

        result.Blocks[1].BlockContentControl.Should().NotBeNull();
        var roundTrippedControl = result.Blocks[1].BlockContentControl!;
        roundTrippedControl.Kind.Should().Be(BlockContentControlKind.Bibliography);
        ReferenceEquals(result.Blocks[2].BlockContentControl, roundTrippedControl).Should().BeTrue();
        result.Blocks[0].BlockContentControl.Should().BeNull();
        result.Blocks[3].BlockContentControl.Should().BeNull();
        result.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs)
            .Should().OnlyContain(run => run.Control == null);

        static string BlockPlainText(Block block) => block switch
        {
            Paragraph paragraph => paragraph.PlainText,
            Table table => string.Join("\n", table.Rows.Select(row =>
                string.Join("\t", row.Cells.Select(cell => cell.PlainText)))),
            _ => string.Empty,
        };
    }

    [Fact]
    public void ContentControls_EmitSdtInDocumentXml()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(Run.PlainTextControl("text control", tag: "T1"));
        body.Runs.Add(Run.CheckBoxControl(@checked: true, tag: "C1"));
        doc.Blocks.Add(body);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();

        // Each control wraps its run(s) in a w:sdt (w:sdtPr + w:sdtContent).
        documentXml.Should().Contain("<w:sdt>");
        documentXml.Should().Contain("<w:sdtPr>");
        documentXml.Should().Contain("<w:sdtContent>");
        // Plain-text control marker + tag, checkbox control marker + checked state.
        documentXml.Should().Contain("<w:text");
        documentXml.Should().Contain("w:val=\"T1\"");
        documentXml.Should().Contain("w14:checkbox");
        documentXml.Should().Contain("w14:val=\"1\"");
    }

    [Fact]
    public void NewContentControls_EmitTheirSdtPrElements()
    {
        var doc = new TextDocument();
        var body = new Paragraph();
        body.Runs.Add(Run.RichTextControl("rich", tag: "R1"));
        body.Runs.Add(Run.DatePickerControl("6/19/2026", tag: "D1", dateFormat: "M/d/yyyy"));
        body.Runs.Add(Run.DropDownListControl(
            new[] { new ContentControlListItem("One", "1"), new ContentControlListItem("Two", "2") }, tag: "DD1"));
        body.Runs.Add(Run.ComboBoxControl(
            new[] { new ContentControlListItem("A", "a") }, tag: "CB1"));
        doc.Blocks.Add(body);

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();

        documentXml.Should().Contain("<w:richText");
        documentXml.Should().Contain("<w:date");
        documentXml.Should().Contain("<w:dateFormat");
        documentXml.Should().Contain("<w:dropDownList");
        documentXml.Should().Contain("<w:comboBox");
        documentXml.Should().Contain("<w:listItem");
        documentXml.Should().Contain("w:displayText=\"One\"");
        documentXml.Should().Contain("w:value=\"2\"");
    }

    [Fact]
    public void NoContentControls_DoesNotEmitSdt()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Plain body with no controls"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = docReader.ReadToEnd();
        documentXml.Should().NotContain("<w:sdt");
    }

    [Theory]
    [InlineData(ProtectionMode.ReadOnly, "readOnly")]
    [InlineData(ProtectionMode.CommentsOnly, "comments")]
    [InlineData(ProtectionMode.TrackChangesOnly, "trackedChanges")]
    [InlineData(ProtectionMode.FillingForms, "forms")]
    public void DocumentProtection_RoundTrips_EachMode(ProtectionMode mode, string expectedEdit)
    {
        var doc = new TextDocument { Protection = new ProtectionSettings(mode) };
        doc.Blocks.Add(new Paragraph("Protected body"));

        // The written settings part carries the expected w:edit token and enforcement.
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var settingsReader = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
            var settingsXml = settingsReader.ReadToEnd();
            settingsXml.Should().Contain("documentProtection");
            settingsXml.Should().Contain($"w:edit=\"{expectedEdit}\"");
            settingsXml.Should().Contain("w:enforcement=\"1\"");
        }

        // And it reads back to the same protection mode.
        stream.Position = 0;
        var result = DocxReader.Read(stream);
        result.Protection.Mode.Should().Be(mode);
        result.Protection.IsProtected.Should().BeTrue();
    }

    [Fact]
    public void DocumentProtection_Package_HasSettingsPart_ContentType_AndRelationship()
    {
        var doc = new TextDocument { Protection = new ProtectionSettings(ProtectionMode.ReadOnly) };
        doc.Blocks.Add(new Paragraph("Locked"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/settings.xml").Should().NotBeNull();

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        var contentTypes = ctReader.ReadToEnd();
        contentTypes.Should().Contain("/word/settings.xml");
        contentTypes.Should().Contain("application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        var rels = relsReader.ReadToEnd();
        rels.Should().Contain("settings.xml");
        rels.Should().Contain("http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings");
    }

    [Fact]
    public void NoProtection_EmitsNoSettingsPart_AndReadsBackNone()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Unprotected body"));
        doc.Protection.Mode.Should().Be(ProtectionMode.None); // default

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            // No settings part, content-type override, or relationship is emitted for an unprotected doc.
            zip.GetEntry("word/settings.xml").Should().BeNull();

            using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
            ctReader.ReadToEnd().Should().NotContain("settings+xml");

            using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
            relsReader.ReadToEnd().Should().NotContain("/relationships/settings");
        }

        stream.Position = 0;
        var result = DocxReader.Read(stream);
        result.Protection.Mode.Should().Be(ProtectionMode.None);
        result.Protection.IsProtected.Should().BeFalse();
    }

    [Fact]
    public void MarkAsFinal_RoundTrips_AsCustomProperty()
    {
        var doc = new TextDocument { MarkedAsFinal = true };
        doc.Blocks.Add(new Paragraph("Final body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            // The flag rides in docProps/custom.xml as the Word-convention _MarkAsFinal boolean property,
            // with the part declared and related in the package.
            using var customReader = new StreamReader(zip.GetEntry("docProps/custom.xml")!.Open());
            var custom = customReader.ReadToEnd();
            custom.Should().Contain("_MarkAsFinal");
            custom.Should().Contain("<vt:bool>true</vt:bool>");

            using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
            ctReader.ReadToEnd().Should().Contain("custom-properties+xml");

            using var relsReader = new StreamReader(zip.GetEntry("_rels/.rels")!.Open());
            relsReader.ReadToEnd().Should().Contain("docProps/custom.xml");
        }

        stream.Position = 0;
        DocxReader.Read(stream).MarkedAsFinal.Should().BeTrue();
    }

    [Fact]
    public void MarkAsFinal_AndWatermark_BothRoundTrip_InOneCustomPart()
    {
        var doc = new TextDocument { MarkedAsFinal = true };
        doc.Page.Watermark = "DRAFT";
        doc.Blocks.Add(new Paragraph("Body"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        var result = DocxReader.Read(stream);
        result.MarkedAsFinal.Should().BeTrue();
        result.Page.Watermark.Should().Be("DRAFT");
    }

    [Fact]
    public void NotMarkedAsFinal_AndNoWatermark_EmitsNoCustomPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("Body"));
        doc.MarkedAsFinal.Should().BeFalse(); // default

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("docProps/custom.xml").Should().BeNull();
    }

    // --- Page setup polish: hyphenation (settings.xml), vertical alignment + titlePg (sectPr) ---

    [Fact]
    public void AutoHyphenation_RoundTrips()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hyphenate me"));
        doc.Page.AutoHyphenation = true;

        var page = RoundTrip(doc).Page;

        page.AutoHyphenation.Should().BeTrue();
    }

    [Fact]
    public void HyphenationOptions_RoundTrip_ZoneLimitAndCaps()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hyphenate me"));
        doc.Page.AutoHyphenation = true;
        doc.Page.HyphenationZonePt = 18; // 360 twips
        doc.Page.ConsecutiveHyphenLimit = 3;
        doc.Page.DoNotHyphenateCaps = true;

        var page = RoundTrip(doc).Page;

        page.AutoHyphenation.Should().BeTrue();
        page.HyphenationZonePt.Should().BeApproximately(18, 0.01);
        page.ConsecutiveHyphenLimit.Should().Be(3);
        page.DoNotHyphenateCaps.Should().BeTrue();
    }

    [Fact]
    public void HyphenationOptions_EmitSettingsElements()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hyphenate me"));
        doc.Page.AutoHyphenation = true;
        doc.Page.HyphenationZonePt = 18;
        doc.Page.ConsecutiveHyphenLimit = 2;
        doc.Page.DoNotHyphenateCaps = true;

        using var optionsStream = new MemoryStream();
        DocxWriter.Write(doc, optionsStream);
        optionsStream.Position = 0;

        using var optionsZip = new ZipArchive(optionsStream, ZipArchiveMode.Read);
        using var optionsReader = new StreamReader(optionsZip.GetEntry("word/settings.xml")!.Open());
        var settings = optionsReader.ReadToEnd();
        settings.Should().Contain("autoHyphenation");
        settings.Should().Contain("hyphenationZone");
        settings.Should().Contain("consecutiveHyphenLimit");
        settings.Should().Contain("doNotHyphenateCaps");
    }

    [Fact]
    public void HyphenationSubOptions_NotEmitted_WhenAutoHyphenationOff()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));
        // Sub-options set but automatic hyphenation off => they must not be emitted (and no settings part is
        // forced into existence by them alone).
        doc.Page.ConsecutiveHyphenLimit = 4;
        doc.Page.DoNotHyphenateCaps = true;

        using var offStream = new MemoryStream();
        DocxWriter.Write(doc, offStream);
        offStream.Position = 0;

        using var offZip = new ZipArchive(offStream, ZipArchiveMode.Read);
        offZip.GetEntry("word/settings.xml").Should().BeNull();
    }

    [Fact]
    public void PreservedHyphenationSubOptions_RoundTrip_WhenAutoHyphenationOff()
    {
        var read = ReadHandAuthoredDocx(
            """<w:p><w:r><w:t>plain</w:t></w:r></w:p>""",
            settingsXml:
            """
            <w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:consecutiveHyphenLimit w:val="4"/>
              <w:hyphenationZone w:val="360"/>
              <w:doNotHyphenateCaps/>
            </w:settings>
            """);

        read.Page.AutoHyphenation.Should().BeFalse();
        read.Page.ConsecutiveHyphenLimit.Should().Be(4);
        read.Page.HyphenationZonePt.Should().BeApproximately(18, 0.01);
        read.Page.DoNotHyphenateCaps.Should().BeTrue();

        using var stream = new MemoryStream();
        DocxWriter.Write(read, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
        var settings = reader.ReadToEnd();
        settings.Should().Contain("consecutiveHyphenLimit");
        settings.Should().Contain("hyphenationZone");
        settings.Should().Contain("doNotHyphenateCaps");
        settings.Should().NotContain("autoHyphenation");
    }

    [Fact]
    public void SuppressAutoHyphens_RoundTripsExplicitOnAndOffPerParagraph()
    {
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("inherit"));
        doc.Blocks.Add(new Paragraph("suppress")
        {
            Formatting = ParagraphFormatting.Default with
            {
                SuppressAutoHyphens = true,
                SuppressAutoHyphensIsSet = true
            }
        });
        doc.Blocks.Add(new Paragraph("explicit off")
        {
            Formatting = ParagraphFormatting.Default with { SuppressAutoHyphensIsSet = true }
        });

        var xml = WriteDocumentXml(doc);
        var suppressTokens = xml.Descendants(w + "suppressAutoHyphens").ToList();
        suppressTokens.Should().HaveCount(2);
        suppressTokens[0].Attribute(w + "val").Should().BeNull();
        suppressTokens[1].Attribute(w + "val")?.Value.Should().Be("0");

        var result = RoundTrip(doc).Paragraphs.ToList();
        result[0].Formatting.SuppressAutoHyphens.Should().BeFalse();
        result[0].Formatting.SuppressAutoHyphensIsSet.Should().BeFalse();
        result[1].Formatting.SuppressAutoHyphens.Should().BeTrue();
        result[1].Formatting.SuppressAutoHyphensIsSet.Should().BeTrue();
        result[2].Formatting.SuppressAutoHyphens.Should().BeFalse();
        result[2].Formatting.SuppressAutoHyphensIsSet.Should().BeTrue();
    }

    [Fact]
    public void SuppressLineNumbers_RoundTripsExplicitOnAndOffPerParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("inherit"));
        doc.Blocks.Add(new Paragraph("suppress")
        {
            Formatting = ParagraphFormatting.Default with
            {
                SuppressLineNumbers = true,
                SuppressLineNumbersIsSet = true
            }
        });
        doc.Blocks.Add(new Paragraph("explicit off")
        {
            Formatting = ParagraphFormatting.Default with { SuppressLineNumbersIsSet = true }
        });

        var result = RoundTrip(doc).Paragraphs.ToList();

        result[0].Formatting.SuppressLineNumbers.Should().BeFalse();
        result[0].Formatting.SuppressLineNumbersIsSet.Should().BeFalse();
        result[1].Formatting.SuppressLineNumbers.Should().BeTrue();
        result[1].Formatting.SuppressLineNumbersIsSet.Should().BeTrue();
        result[2].Formatting.SuppressLineNumbers.Should().BeFalse();
        result[2].Formatting.SuppressLineNumbersIsSet.Should().BeTrue();
    }

    [Fact]
    public void AutoHyphenation_EmitsSettingsPart_WithAutoHyphenationToggle()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hyphenate me"));
        doc.Page.AutoHyphenation = true;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/settings.xml").Should().NotBeNull();

        using var settingsReader = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
        settingsReader.ReadToEnd().Should().Contain("autoHyphenation");

        using var ctReader = new StreamReader(zip.GetEntry("[Content_Types].xml")!.Open());
        ctReader.ReadToEnd().Should().Contain("wordprocessingml.settings+xml");

        using var relsReader = new StreamReader(zip.GetEntry("word/_rels/document.xml.rels")!.Open());
        relsReader.ReadToEnd().Should().Contain("relationships/settings");
    }

    [Fact]
    public void DefaultPage_HasNoHyphenation_AndNoSettingsPart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            // Unprotected + no hyphenation => no settings part at all (existing behaviour preserved).
            zip.GetEntry("word/settings.xml").Should().BeNull();
        }

        stream.Position = 0;
        DocxReader.Read(stream).Page.AutoHyphenation.Should().BeFalse();
    }

    [Theory]
    [InlineData(PageVerticalAlignment.Top)]
    [InlineData(PageVerticalAlignment.Center)]
    [InlineData(PageVerticalAlignment.Justified)]
    [InlineData(PageVerticalAlignment.Bottom)]
    public void PageVerticalAlignment_RoundTrips(PageVerticalAlignment alignment)
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("vertically aligned"));
        doc.Page.VerticalAlignment = alignment;

        RoundTrip(doc).Page.VerticalAlignment.Should().Be(alignment);
    }

    [Fact]
    public void PageVerticalAlignment_Justified_EmitsVAlignBoth()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("spread me"));
        doc.Page.VerticalAlignment = PageVerticalAlignment.Justified;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();

        documentXml.Should().Contain("w:vAlign");
        documentXml.Should().Contain("w:val=\"both\"");
    }

    [Fact]
    public void DefaultPage_TopAlignment_EmitsNoVAlign()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));
        doc.Page.VerticalAlignment.Should().Be(PageVerticalAlignment.Top); // default

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        reader.ReadToEnd().Should().NotContain("vAlign");
    }

    [Fact]
    public void DifferentFirstPage_RoundTrips_AndEmitsTitlePg()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("first page differs"));
        doc.Page.DifferentFirstPage = true;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            reader.ReadToEnd().Should().Contain("w:titlePg");
        }

        stream.Position = 0;
        DocxReader.Read(stream).Page.DifferentFirstPage.Should().BeTrue();
    }

    [Fact]
    public void DefaultPage_HasNoTitlePg()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;

        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            reader.ReadToEnd().Should().NotContain("titlePg");
        }

        stream.Position = 0;
        DocxReader.Read(stream).Page.DifferentFirstPage.Should().BeFalse();
    }

    // --- Page Setup dialog fields: gutter (pgMar), mirror margins (settings), header/footer distance
    //     (pgMar), vertical alignment (sectPr) ---

    [Fact]
    public void PageSetup_GutterHeaderFooterDistance_RoundTrip_AndEmitInPgMar()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("page setup"));
        doc.Page.GutterPt = 18;          // 0.25"
        doc.Page.HeaderDistancePt = 30;  // ~0.42"
        doc.Page.FooterDistancePt = 45;  // 0.625"

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
            var documentXml = reader.ReadToEnd();
            documentXml.Should().Contain("w:gutter=\"360\"");  // 18 pt * 20 = 360 twips
            documentXml.Should().Contain("w:header=\"600\"");  // 30 pt * 20 = 600 twips
            documentXml.Should().Contain("w:footer=\"900\"");  // 45 pt * 20 = 900 twips
        }

        stream.Position = 0;
        var read = DocxReader.Read(stream).Page;
        read.GutterPt.Should().BeApproximately(18, 0.01);
        read.HeaderDistancePt.Should().BeApproximately(30, 0.01);
        read.FooterDistancePt.Should().BeApproximately(45, 0.01);
    }

    [Fact]
    public void DefaultPage_PgMar_HasNoGutterHeaderOrFooter()
    {
        // Regression guard: a document that never touched these keeps the legacy pgMar (no gutter/header/footer).
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var documentXml = reader.ReadToEnd();
        documentXml.Should().NotContain("w:gutter");
        documentXml.Should().NotContain("w:header=");
        documentXml.Should().NotContain("w:footer=");
    }

    [Fact]
    public void MirrorMargins_RoundTrips_AndEmitsSettingsToggle()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("double sided"));
        doc.Page.MirrorMargins = true;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        {
            using var reader = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
            reader.ReadToEnd().Should().Contain("w:mirrorMargins");
        }

        stream.Position = 0;
        DocxReader.Read(stream).Page.MirrorMargins.Should().BeTrue();
    }

    [Fact]
    public void DefaultPage_NoMirrorMargins_EmitsNoSettingsPart()
    {
        // A document needing none of FreeW's settings-triggering features still emits no settings part.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain page"));
        doc.Page.MirrorMargins.Should().BeFalse(); // default

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.GetEntry("word/settings.xml").Should().BeNull();
    }

    [Fact]
    public void PageSetup_AllDialogFields_SurviveFullRoundTrip()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("everything"));
        doc.Page.MarginTopPt = 54;
        doc.Page.MarginBottomPt = 60;
        doc.Page.MarginLeftPt = 66;
        doc.Page.MarginRightPt = 70;
        doc.Page.GutterPt = 12;
        doc.Page.HeaderDistancePt = 24;
        doc.Page.FooterDistancePt = 24;
        doc.Page.MirrorMargins = true;
        doc.Page.DifferentFirstPage = true;
        doc.Page.VerticalAlignment = PageVerticalAlignment.Center;
        doc.Page.WidthPt = 595.3; // A4 portrait width
        doc.Page.HeightPt = 841.9;

        var read = RoundTrip(doc).Page;
        read.MarginTopPt.Should().BeApproximately(54, 0.05);
        read.MarginBottomPt.Should().BeApproximately(60, 0.05);
        read.MarginLeftPt.Should().BeApproximately(66, 0.05);
        read.MarginRightPt.Should().BeApproximately(70, 0.05);
        read.GutterPt.Should().BeApproximately(12, 0.05);
        read.HeaderDistancePt.Should().BeApproximately(24, 0.05);
        read.FooterDistancePt.Should().BeApproximately(24, 0.05);
        read.MirrorMargins.Should().BeTrue();
        read.DifferentFirstPage.Should().BeTrue();
        read.VerticalAlignment.Should().Be(PageVerticalAlignment.Center);
        read.WidthPt.Should().BeApproximately(595.3, 0.1);
        read.HeightPt.Should().BeApproximately(841.9, 0.1);
    }

    [Fact]
    public void WidePageDimensions_EmitLandscapeOrientationForWord()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("wide page"));
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 396;
        doc.Page.Landscape = false;

        var xml = WriteDocumentXml(doc);
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var pgSz = xml.Root!.Element(w + "body")!.Element(w + "sectPr")!.Element(w + "pgSz")!;
        pgSz.Attribute(w + "orient")!.Value.Should().Be("landscape");

        var read = RoundTrip(doc).Page;
        read.Landscape.Should().BeTrue();
        read.WidthPt.Should().BeApproximately(612, 0.01);
        read.HeightPt.Should().BeApproximately(396, 0.01);
    }

    [Fact]
    public void SingleSection_RoundTripsIdentically_WithNoParagraphLevelSectPr()
    {
        // Regression guard: a document with no section breaks must behave exactly as before — one
        // body-level w:sectPr, no per-paragraph w:sectPr, and a single reconstructed section.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("only section"));
        doc.Page.Landscape = true; // exercise the (previously unread) pgSz orientation round-trip too.

        var xml = WriteDocumentXml(doc);
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var body = xml.Root!.Element(w + "body")!;
        // Exactly one body-level sectPr, and no paragraph carries a pPr/sectPr.
        body.Elements(w + "sectPr").Should().HaveCount(1);
        body.Elements(w + "p").SelectMany(p => p.Elements(w + "pPr").Elements(w + "sectPr"))
            .Should().BeEmpty();

        var result = RoundTrip(doc);
        result.Sections.Should().HaveCount(1);
        result.Sections[0].Page.Landscape.Should().BeTrue();
        result.Blocks.OfType<Paragraph>().Should().OnlyContain(p => p.SectionBreak == null);
    }

    [Fact]
    public void TwoSections_RoundTrip_PerSectionPageSetupAndBreakKind()
    {
        // Section 1: portrait, NextPage break, ending on its last paragraph. Section 2 (final): landscape.
        var doc = new TextDocument();
        var section1End = new Paragraph("section one")
        {
            SectionBreak = new Section(
                new PageSettings { Landscape = false, MarginLeftPt = 90 },
                SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(section1End);
        doc.Blocks.Add(new Paragraph("section two"));
        doc.Page.Landscape = true; // the final (body-level) section is landscape.
        doc.Page.MarginLeftPt = 54;

        // Structural check: section 1's sectPr lives in its paragraph's pPr (with a w:type), and there is
        // exactly one body-level sectPr for the final section.
        var xml = WriteDocumentXml(doc);
        var w = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var body = xml.Root!.Element(w + "body")!;
        body.Elements(w + "sectPr").Should().HaveCount(1);
        var paragraphSectPrs = body.Elements(w + "p")
            .SelectMany(p => p.Elements(w + "pPr").Elements(w + "sectPr")).ToList();
        paragraphSectPrs.Should().HaveCount(1);
        paragraphSectPrs[0].Element(w + "type")!.Attribute(w + "val")!.Value.Should().Be("nextPage");

        var result = RoundTrip(doc);

        result.Sections.Should().HaveCount(2);
        // Section 1 (the non-final section, recovered from the paragraph-level sectPr).
        result.Sections[0].BreakKind.Should().Be(SectionBreakKind.NextPage);
        result.Sections[0].Page.Landscape.Should().BeFalse();
        result.Sections[0].Page.MarginLeftPt.Should().BeApproximately(90, 0.01);
        // Section 2 (the final/body-level section).
        result.Sections[1].Page.Landscape.Should().BeTrue();
        result.Sections[1].Page.MarginLeftPt.Should().BeApproximately(54, 0.01);

        // The marker survives on the first paragraph; the second paragraph ends no section.
        var paragraphs = result.Blocks.OfType<Paragraph>().ToList();
        paragraphs[0].SectionBreak.Should().NotBeNull();
        paragraphs[1].SectionBreak.Should().BeNull();
    }

    [Fact]
    public void ContinuousSectionBreak_RoundTripsBreakKind()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("continuous-ended")
        {
            SectionBreak = new Section(new PageSettings { ColumnCount = 2 }, SectionBreakKind.Continuous)
        });
        doc.Blocks.Add(new Paragraph("rest"));

        var result = RoundTrip(doc);

        result.Sections.Should().HaveCount(2);
        result.Sections[0].BreakKind.Should().Be(SectionBreakKind.Continuous);
        result.Sections[0].Page.ColumnCount.Should().Be(2);
    }

    // ── Footnote/Endnote numbering options (w:footnotePr / w:endnotePr in settings.xml) ─────────

    [Fact]
    public void FootnoteNumbering_Default_Does_Not_Emit_Settings_Part()
    {
        // A freshly authored document with default footnote options must NOT emit a settings part
        // (nor footnotePr) — keeps the file minimal and byte-equivalent to before.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("text"));
        // FootnoteNumbering is IsDefault — no settings part should appear.

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        zip.GetEntry("word/settings.xml").Should().BeNull("default footnote options must not force a settings part");
    }

    [Fact]
    public void FootnoteNumbering_RoundTrips_LowerRoman_PerSection()
    {
        // Set lower-roman format + start-at=1 + restart-per-section on footnotes;
        // leave endnotes at default. The settings must survive a save → reload.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var result = RoundTrip(doc);

        result.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerRoman);
        result.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);
        result.FootnoteNumbering.StartAt.Should().Be(1);  // default preserved
        // Endnotes untouched.
        result.EndnoteNumbering.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void FootnoteNumbering_RoundTrips_StartAt_3_And_LowerLetter()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerLetter;
        doc.FootnoteNumbering.StartAt = 3;
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.Continuous;

        var result = RoundTrip(doc);

        result.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerLetter);
        result.FootnoteNumbering.StartAt.Should().Be(3);
        result.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.Continuous);
    }

    [Fact]
    public void FootnoteNumbering_RoundTrips_EachPage_Restart()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;

        var result = RoundTrip(doc);

        result.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachPage);
        result.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.Decimal);  // default
    }

    [Fact]
    public void EndnoteNumbering_RoundTrips_UpperRoman_PerSection()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.EndnoteNumbering.NumberFormat = NoteNumberFormat.UpperRoman;
        doc.EndnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;
        doc.EndnoteNumbering.StartAt = 2;

        var result = RoundTrip(doc);

        result.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperRoman);
        result.EndnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);
        result.EndnoteNumbering.StartAt.Should().Be(2);
        // Footnotes untouched.
        result.FootnoteNumbering.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void FootnoteAndEndnoteNumbering_RoundTrip_Independently()
    {
        // Both changed simultaneously — must survive independently.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.Chicago;
        doc.FootnoteNumbering.StartAt = 1;
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachPage;
        doc.EndnoteNumbering.NumberFormat = NoteNumberFormat.UpperLetter;
        doc.EndnoteNumbering.StartAt = 5;
        doc.EndnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        var result = RoundTrip(doc);

        result.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.Chicago);
        result.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachPage);
        result.EndnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperLetter);
        result.EndnoteNumbering.StartAt.Should().Be(5);
        result.EndnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachSection);
    }

    [Fact]
    public void FootnoteNumbering_Emits_Correct_Ooxml_Attributes()
    {
        // Verify the raw XML written to word/settings.xml contains the correct w:footnotePr children.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("body"));
        doc.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerRoman;
        doc.FootnoteNumbering.StartAt = 2;
        doc.FootnoteNumbering.NumberRestart = NoteNumberRestart.EachSection;

        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var settingsEntry = zip.GetEntry("word/settings.xml");
        settingsEntry.Should().NotBeNull();

        using var reader = new System.IO.StreamReader(settingsEntry!.Open());
        var xml = reader.ReadToEnd();

        xml.Should().Contain("footnotePr");
        xml.Should().Contain("lowerRoman");
        xml.Should().Contain("numStart");
        xml.Should().Contain("eachSect");
        // Must NOT contain endnotePr (endnote still default).
        xml.Should().NotContain("endnotePr");
    }

    [Fact]
    public void FootnoteNumbering_PreservedSettings_Overlay_Works()
    {
        // When a preserved settings part exists (from a read document), overlaying new footnote options
        // must replace any existing footnotePr and survive the round-trip.

        // Build a minimal docx with a settings part containing footnotePr and read it back.
        const string settingsXml =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
            "<w:footnotePr>" +
            "<w:numFmt w:val=\"upperRoman\"/>" +
            "<w:numStart w:val=\"3\"/>" +
            "<w:numRestart w:val=\"eachPage\"/>" +
            "</w:footnotePr>" +
            "</w:settings>";

        // Create a minimal docx containing this settings part and read it back.
        using var srcStream = BuildMinimalDocxWithSettings(settingsXml);
        var loaded = DocxReader.Read(srcStream);

        // Reader must have populated the model from the settings XML.
        loaded.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.UpperRoman);
        loaded.FootnoteNumbering.StartAt.Should().Be(3);
        loaded.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachPage);

        // Now change one property and round-trip: the new value must survive.
        loaded.FootnoteNumbering.NumberFormat = NoteNumberFormat.LowerLetter;
        var result = RoundTrip(loaded);
        result.FootnoteNumbering.NumberFormat.Should().Be(NoteNumberFormat.LowerLetter);
        result.FootnoteNumbering.StartAt.Should().Be(3);
        result.FootnoteNumbering.NumberRestart.Should().Be(NoteNumberRestart.EachPage);
    }

    // Builds a MemoryStream containing a minimal valid docx with the given settings XML content.
    private static MemoryStream BuildMinimalDocxWithSettings(string settingsXml)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipEntry(zip, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                "</Types>");
            WriteZipEntry(zip, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "</Relationships>");
            WriteZipEntry(zip, "word/document.xml",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:body><w:p><w:r><w:t>body</w:t></w:r></w:p></w:body></w:document>");
            // Document rels: reference the settings part (must be written before settings.xml is added).
            WriteZipEntry(zip, "word/_rels/document.xml.rels",
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>" +
                "</Relationships>");
            WriteZipEntry(zip, "word/settings.xml", settingsXml);
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteZipEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path);
        using var writer = new System.IO.StreamWriter(entry.Open());
        writer.Write(content);
    }

    // ── H4 tblGrid gridCol count reconciliation test ─────────────────────────────────────────────

    [Fact]
    public void Table_WithColumnWidthsMismatch_TblGridReconcilesWithActualGridColumns()
    {
        // A table where ColumnWidthsPt has 2 entries but the rows have 3 grid columns (due to a
        // GridSpan=2 cell leaving widths under-counted after a prior edit). The saved tblGrid must
        // emit exactly 3 gridCol elements, not 2 (which would cause Word to repair the file).
        var doc = new TextDocument();
        var table = new Table();
        // Row 0: cell spanning 2 grid cols + one single cell = 3 grid cols total.
        var row0 = new TableRow();
        row0.Cells.Add(new TableCell("wide") { GridSpan = 2 });
        row0.Cells.Add(new TableCell("narrow"));
        table.Rows.Add(row0);
        // ColumnWidthsPt only has 2 entries — simulates the drift described in H4.
        table.ColumnWidthsPt.AddRange([120.0, 60.0]);
        doc.Blocks.Add(table);

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var gridCols = WriteDocumentXml(doc).Descendants(ns + "tblGrid").Single()
            .Elements(ns + "gridCol").Count();

        // Must be 3 (actual grid columns), not 2 (drifted ColumnWidthsPt.Count).
        gridCols.Should().Be(3, "tblGrid must be reconciled to the actual grid-column total");
    }

    // ── H3 Hanging-indent regression tests ──────────────────────────────────────────────────────

    [Fact]
    public void HangingIndent_RoundTrips_WithNegativeFirstLineIndentPt()
    {
        // A paragraph with a hanging indent (modelled as negative FirstLineIndentPt) must survive
        // a full write→read cycle and come back with the same negative value.
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hanging")
        {
            Formatting = ParagraphFormatting.Default with
            {
                IndentLeftPt = 36,        // 0.5 in left indent
                FirstLineIndentPt = -18   // 0.25 in hanging (first-line pulls left of body indent)
            }
        });

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.IndentLeftPt.Should().BeApproximately(36, 0.5);   // dxa round-trip tolerance
        formatting.FirstLineIndentPt.Should().BeApproximately(-18, 0.5);
    }

    [Fact]
    public void HangingIndent_WrittenAs_WHanging_NotNegativeFirstLine()
    {
        // The XML saved to disk must use w:hanging (positive), never a negative w:firstLine value
        // (which OOXML forbids — w:firstLine is an unsigned twips measure).
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("hanging")
        {
            Formatting = ParagraphFormatting.Default with
            {
                IndentLeftPt = 36,
                FirstLineIndentPt = -36
            }
        });

        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var ind = WriteDocumentXml(doc).Descendants(ns + "ind").First();

        ind.Attribute(ns + "hanging").Should().NotBeNull("w:hanging must be present for a hanging indent");
        ind.Attribute(ns + "firstLine").Should().BeNull("w:firstLine must NOT be emitted for a hanging indent");
        // The hanging value must be positive (unsigned dxa).
        int.Parse(ind.Attribute(ns + "hanging")!.Value).Should().BeGreaterThan(0);
    }

    [Fact]
    public void HangingIndent_ReadFromWordAuthored_WHanging()
    {
        // A Word-authored paragraph with w:hanging must be read as a negative FirstLineIndentPt.
        var doc = ReadHandAuthoredDocx(
            """
            <w:p>
              <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
              <w:r><w:t>hanging text</w:t></w:r>
            </w:p>
            """);

        var f = doc.Paragraphs.First().Formatting;
        f.IndentLeftPt.Should().BeApproximately(36, 0.5);   // 720 dxa = 36 pt
        f.FirstLineIndentPt.Should().BeApproximately(-18, 0.5); // 360 dxa → -18 pt
    }

    // ── H5 Hyperlink-in-field result regression test ─────────────────────────────────────────────

    [Fact]
    public void ComplexField_WithHyperlinkWrappedResult_StaysInsideField()
    {
        // A complex field whose cached result runs are inside a w:hyperlink element (as TOC/INDEX/
        // HYPERLINK fields emit) must round-trip with the result text INSIDE the ComplexField run,
        // not leaked out as bare text paragraph runs.
        var doc = ReadHandAuthoredDocx(
            """
            <w:p>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> HYPERLINK "https://example.com" </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:hyperlink r:id="rId1" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <w:r><w:t>Example Link</w:t></w:r>
              </w:hyperlink>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            """);

        var runs = doc.Paragraphs.First().Runs;
        // Exactly one ComplexField run — no spurious plain-text run containing "Example Link"
        var fieldRun = runs.Should().ContainSingle(r => r.ComplexField != null).Subject;
        fieldRun.ComplexField!.Instruction.Should().Contain("HYPERLINK");
        // Run.Text holds the cached result text for a ComplexField run.
        fieldRun.Text.Should().Contain("Example Link");
        fieldRun.HyperlinkUrl.Should().Be("https://example.com");
        runs.Where(r => r.ComplexField is null).Should().NotContain(r => r.Text.Contains("Example Link"),
            "result text must not leak outside the ComplexField run");
    }

    [Fact]
    public void NativeHyperlinkFields_ProjectTargetsAndRetainTheirPackageForm()
    {
        var document = ReadHandAuthoredDocx(
            """
            <w:p>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> HYPERLINK "https://example.com/manual" \o "Open manual" </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:r><w:t>Manual</w:t></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            <w:p>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> HYPERLINK \l "Details" \o "Jump to details" </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:r><w:t>Details</w:t></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            <w:p>
              <w:fldSimple w:instr=" HYPERLINK &quot;https://example.com/guide&quot; \l &quot;Install&quot; \t &quot;_blank&quot; ">
                <w:r><w:t>Install guide</w:t></w:r>
              </w:fldSimple>
            </w:p>
            """);

        AssertProjectedTargets(document);

        var word = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var xml = WriteDocumentXml(document);
        xml.Descendants(word + "instrText").Select(element => element.Value).Should().Contain([
            " HYPERLINK \"https://example.com/manual\" \\o \"Open manual\" ",
            " HYPERLINK \\l \"Details\" \\o \"Jump to details\" "
        ]);
        xml.Descendants(word + "fldSimple").Single().Attribute(word + "instr")!.Value.Should().Be(
            " HYPERLINK \"https://example.com/guide\" \\l \"Install\" \\t \"_blank\" ");
        xml.Descendants(word + "hyperlink").Should().BeEmpty(
            "the native field instruction, not a generated hyperlink relationship, remains authoritative");

        AssertProjectedTargets(RoundTrip(document));

        static void AssertProjectedTargets(TextDocument candidate)
        {
            var runs = candidate.Paragraphs.Select(paragraph => paragraph.Runs.Single()).ToArray();
            runs[0].HyperlinkUrl.Should().Be("https://example.com/manual");
            runs[0].HyperlinkAnchor.Should().BeNull();
            runs[0].HyperlinkTooltip.Should().Be("Open manual");
            runs[1].HyperlinkUrl.Should().BeNull();
            runs[1].HyperlinkAnchor.Should().Be("Details");
            runs[1].HyperlinkTooltip.Should().Be("Jump to details");
            runs[2].HyperlinkUrl.Should().Be("https://example.com/guide#Install");
            runs[2].HyperlinkAnchor.Should().BeNull();
            runs.All(run => run.ComplexField?.Keyword == "HYPERLINK").Should().BeTrue();
        }
    }

    // ── H7 Style-type (table/numbering) regression test ─────────────────────────────────────────

    [Fact]
    public void TableStyle_RoundTrips_WithCorrectWType()
    {
        // A w:style w:type="table" read from a hand-authored docx must be stored as StyleType.Table
        // and written back as w:type="table", not w:type="paragraph".
        var doc = ReadHandAuthoredDocxWithStyles(
            bodyXml: """<w:p><w:r><w:t>body</w:t></w:r></w:p>""",
            stylesXml:
            """
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="table" w:styleId="TableGrid">
                <w:name w:val="Table Grid"/>
              </w:style>
            </w:styles>
            """);

        // Model layer: the style is read as StyleType.Table, not Paragraph.
        doc.Styles.Should().ContainKey("TableGrid");
        doc.Styles["TableGrid"].Type.Should().Be(StyleType.Table);

        // Writer layer: writing and re-reading preserves the table type.
        var roundTripped = RoundTrip(doc);
        roundTripped.Styles.Should().ContainKey("TableGrid");
        roundTripped.Styles["TableGrid"].Type.Should().Be(StyleType.Table);

        // XML layer: the emitted XML must say w:type="table".
        using var stream = new MemoryStream();
        DocxWriter.Write(doc, stream);
        stream.Position = 0;
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var stylesEntry = zip.GetEntry("word/styles.xml")!.Open();
        var stylesDoc = XDocument.Load(stylesEntry);
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        var tableStyleEl = stylesDoc.Descendants(ns + "style")
            .FirstOrDefault(s => s.Attribute(ns + "styleId")?.Value == "TableGrid");
        tableStyleEl.Should().NotBeNull();
        tableStyleEl!.Attribute(ns + "type")!.Value.Should().Be("table");
    }

    /// <summary>
    /// Reads a hand-authored docx that has both a custom body and a styles.xml part.
    /// </summary>
    private static TextDocument ReadHandAuthoredDocxWithStyles(string bodyXml, string stylesXml)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                var entry = zip.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(xml);
            }

            Add("word/document.xml",
                $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <w:body>{bodyXml}</w:body>
                </w:document>
                """);
            Add("word/styles.xml", stylesXml);
        }
        stream.Position = 0;
        return DocxReader.Read(stream);
    }

    // ── Em-dash / non-ASCII punctuation round-trip (bug fix: encoding mis-decode) ─────────────────

    /// <summary>
    /// Em dashes, en dashes, curly quotes and ellipsis must survive a write→read round-trip
    /// with their Unicode code points intact. The bug category: UTF-8 bytes interpreted as
    /// Windows-1252 would turn U+2014 (—) into the three-character sequence â€" and similar.
    /// Root cause established: the DocxWriter writes via XDocument.Save (UTF-8 + declaration)
    /// and the DocxReader reads via XmlReader (respects the declaration) — both are correct.
    /// This test acts as a regression guard so any future change to encoding paths stays honest.
    /// </summary>
    [Theory]
    [InlineData("em dash", "—")]
    [InlineData("en dash", "–")]
    [InlineData("left double quote", "“")]
    [InlineData("right double quote", "”")]
    [InlineData("left single quote", "‘")]
    [InlineData("right single quote", "’")]
    [InlineData("ellipsis", "…")]
    [InlineData("mixed non-ASCII", "Hello—World – “quoted” and …")]
    public void NonAsciiPunctuation_RoundTrips_WithCorrectCodePoints(string _, string text)
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(text));
        doc.Blocks.Add(para);

        var read = RoundTrip(doc);

        var recovered = read.Blocks.OfType<Paragraph>().First().Runs.First().Text;
        recovered.Should().Be(text, $"Unicode character(s) must survive write→read without encoding corruption");
    }

    /// <summary>
    /// Confirms the em-dash is stored as the Unicode codepoint U+2014 in the emitted XML
    /// (not as UTF-8 byte sequence interpreted as Windows-1252 mojibake â€").
    /// </summary>
    [Fact]
    public void EmDash_StoredAsUnicodeCodePointInXml()
    {
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run("word—word"));
        doc.Blocks.Add(para);

        var xml = WriteDocumentXml(doc);
        var tText = string.Concat(xml.Descendants(
            System.Xml.Linq.XName.Get("t", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))
            .Select(t => t.Value));

        // The text must contain the actual em-dash character, not the Latin-1 mojibake sequence.
        tText.Should().Contain("—");
        tText.Should().NotContain("â", "that would indicate UTF-8 bytes mis-decoded as Latin-1 (â)");
    }

    /// <summary>
    /// Non-ASCII punctuation typed via AutoCorrect (em-dash from `--`, smart quotes, ellipsis)
    /// must also survive a save→open cycle. This verifies the full write→read path, not just
    /// in-memory state.
    /// </summary>
    [Fact]
    public void AutoCorrectPunctuation_SurvivesSaveOpen()
    {
        // Simulate text that would be produced by AutoCorrect: em-dash, en-dash, smart quotes,
        // ellipsis all in one paragraph.
        const string text = "“Hello” — world’s … en–dash";
        var doc = new TextDocument();
        var para = new Paragraph();
        para.Runs.Add(new Run(text));
        doc.Blocks.Add(para);

        var read = RoundTrip(doc);
        read.Blocks.OfType<Paragraph>().First().Runs.First().Text.Should().Be(text);
    }
}
