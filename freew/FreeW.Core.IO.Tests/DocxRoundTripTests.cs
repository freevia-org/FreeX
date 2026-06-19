using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

public class DocxRoundTripTests
{
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
        var canonical = new[] { "rFonts", "b", "i", "caps", "smallCaps", "strike", "color", "sz", "szCs", "u", "shd", "vertAlign" };
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
    public void PlainParagraph_HasNoFlowControl()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("plain"));

        var formatting = RoundTrip(doc).Paragraphs.First().Formatting;

        formatting.KeepWithNext.Should().BeFalse();
        formatting.KeepLinesTogether.Should().BeFalse();
        formatting.WidowControl.Should().BeFalse();
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

        var properties = RoundTrip(doc).Properties;

        properties.Title.Should().Be("My Title");
        properties.Author.Should().Be("Ada Lovelace");
        properties.Subject.Should().Be("Analytical Engine");
        properties.Keywords.Should().Be("history; computing");
        properties.Comments.Should().Be("First program");
        properties.LastModifiedBy.Should().Be("Charles Babbage");
        properties.Created.Should().Be(new DateTimeOffset(1843, 10, 1, 9, 30, 0, TimeSpan.Zero));
        properties.Modified.Should().Be(new DateTimeOffset(1843, 10, 15, 14, 0, 0, TimeSpan.Zero));
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

        using var docReader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        docReader.ReadToEnd().Should().Contain("w:val=\"3\"");
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
        run.Control.Should().NotBeNull();
        run.Control!.Kind.Should().Be(ContentControlKind.RichText);
        run.Control.Tag.Should().Be("BlockControl");
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
    public void SuppressAutoHyphens_RoundTripsPerParagraph()
    {
        var doc = new TextDocument();
        doc.Blocks.Add(new Paragraph("normal"));
        doc.Blocks.Add(new Paragraph("no hyphens") { Formatting = ParagraphFormatting.Default with { SuppressAutoHyphens = true } });

        var result = RoundTrip(doc);

        var paragraphs = result.Paragraphs.ToList();
        paragraphs[0].Formatting.SuppressAutoHyphens.Should().BeFalse();
        paragraphs[1].Formatting.SuppressAutoHyphens.Should().BeTrue();
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
}
