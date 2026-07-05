using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FreeW.Core.Model;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.DocumentView;

public static class FreeWVisualEvidenceDocumentFactory
{
    public static TextDocument BuildFootnotePlacementDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Footnotes Test", "Heading1"));
        doc.Blocks.Add(new Paragraph("This tests whether footnote content appears at the foot of each page."));

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("This sentence has a footnote reference"));
        p1.Runs.Add(Run.FootnoteReference(1));
        p1.Runs.Add(new Run(". The footnote content should appear at the bottom of this page."));
        doc.Blocks.Add(p1);
        doc.Footnotes[1] = new Footnote(
            1,
            "Footnote 1: This is first footnote content. Should appear at bottom of page 1 with a separator rule.");

        for (var i = 1; i <= 22; i++)
            doc.Blocks.Add(new Paragraph($"Filler paragraph {i}: Lorem ipsum dolor sit amet consectetur adipiscing."));

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("This sentence on page 2 has a second footnote reference"));
        p2.Runs.Add(Run.FootnoteReference(2));
        p2.Runs.Add(new Run(". The second footnote should be at the bottom of page 2."));
        doc.Blocks.Add(p2);
        doc.Footnotes[2] = new Footnote(
            2,
            "Footnote 2: Second footnote content. Should appear at the bottom of page 2.");

        for (var i = 1; i <= 20; i++)
            doc.Blocks.Add(new Paragraph($"More filler {i}: Additional content to ensure footnote reference is on page 2."));

        return doc;
    }

    public static TextDocument BuildEndnotePlacementDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Endnotes Test", "Heading1"));
        doc.Blocks.Add(new Paragraph("This tests whether endnote content appears at the end of the document."));

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("First sentence with an endnote reference"));
        p1.Runs.Add(Run.EndnoteReference(1));
        p1.Runs.Add(new Run(". Endnotes should collect at the document end."));
        doc.Blocks.Add(p1);
        doc.Endnotes[1] = new Endnote(
            1,
            "Endnote 1: This content should appear at the very end of the document, after all body text.");

        for (var i = 1; i <= 20; i++)
            doc.Blocks.Add(new Paragraph($"Body paragraph {i}: Endnote references collect at document end."));

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Second sentence with another endnote reference"));
        p2.Runs.Add(Run.EndnoteReference(2));
        p2.Runs.Add(new Run(". Both endnotes should appear together at the end."));
        doc.Blocks.Add(p2);
        doc.Endnotes[2] = new Endnote(
            2,
            "Endnote 2: This is the second endnote. Both endnotes should be listed together at the document end.");

        for (var i = 1; i <= 20; i++)
            doc.Blocks.Add(new Paragraph($"More body content {i}: Additional text before the endnotes section."));

        return doc;
    }

    public static TextDocument BuildFieldPageNumberVariantsDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Field Page Number Evidence";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Subject = "Visual parity field scenario";
        doc.Properties.Keywords = "PAGE, NUMPAGES, DOCPROPERTY";
        doc.Properties.Comments = "Exercises shared field rendering evidence.";
        doc.Page.DifferentFirstPage = true;
        doc.Page.DifferentOddEvenPages = true;

        doc.FinalSectionHeadersFooters.FirstHeader = FieldHeaderFooter(
            new Run("First header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("3"));
        doc.FinalSectionHeadersFooters.FirstFooter = FieldHeaderFooter(
            new Run("First footer complex page "),
            Run.ComplexFieldRun(" PAGE ", "1"),
            new Run(" / "),
            Run.ComplexFieldRun(" NUMPAGES ", "3"));
        doc.FinalSectionHeadersFooters.EvenHeader = FieldHeaderFooter(
            new Run("Even header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("3"));
        doc.FinalSectionHeadersFooters.EvenFooter = FieldHeaderFooter(
            new Run("Even footer title: "),
            Run.TitleField("Field Page Number Evidence"));
        doc.FinalSectionHeadersFooters.Header = FieldHeaderFooter(
            new Run("Default header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("3"));
        doc.FinalSectionHeadersFooters.Footer = FieldHeaderFooter(
            new Run("Default footer author: "),
            Run.AuthorField("FreeW Visual Evidence"),
            new Run(" | page "),
            Run.PageNumberField());

        doc.Blocks.Add(StyledParagraph("Field/Page Number Variants", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises PAGE and NUMPAGES fields across first, even, and default " +
            "header/footer slots, plus document-property fields in the body."));

        var propertyParagraph = new Paragraph();
        propertyParagraph.Runs.Add(new Run("Properties: title="));
        propertyParagraph.Runs.Add(Run.TitleField("Field Page Number Evidence"));
        propertyParagraph.Runs.Add(new Run("; author="));
        propertyParagraph.Runs.Add(Run.AuthorField("FreeW Visual Evidence"));
        propertyParagraph.Runs.Add(new Run("; subject="));
        propertyParagraph.Runs.Add(Run.SubjectField("Visual parity field scenario"));
        propertyParagraph.Runs.Add(new Run("; keywords="));
        propertyParagraph.Runs.Add(Run.KeywordsField("PAGE, NUMPAGES, DOCPROPERTY"));
        propertyParagraph.Runs.Add(new Run("; comments="));
        propertyParagraph.Runs.Add(Run.DocCommentsField("Exercises shared field rendering evidence."));
        doc.Blocks.Add(propertyParagraph);

        var complexParagraph = new Paragraph();
        complexParagraph.Runs.Add(new Run("Complex result fields: "));
        complexParagraph.Runs.Add(Run.ComplexFieldRun(" TITLE \\* MERGEFORMAT ", "Field Page Number Evidence"));
        complexParagraph.Runs.Add(new Run(" by "));
        complexParagraph.Runs.Add(Run.ComplexFieldRun(" AUTHOR ", "FreeW Visual Evidence"));
        doc.Blocks.Add(complexParagraph);

        for (var i = 1; i <= 58; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Field evidence body paragraph {i}: enough text to force multiple pages while " +
                "keeping the same PAGE and NUMPAGES header/footer variants visible."));
        }

        return doc;
    }

    public static TextDocument BuildReferencesHeavyFieldDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "References Heavy Evidence";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Subject = "Citation, bibliography, and table of authorities visual parity";
        doc.Properties.Keywords = "CITATION, BIBLIOGRAPHY, TOA";
        doc.Properties.Comments = "Exercises visible references fields plus explicit-break generated TOA page references; Word PNG comparison remains separate.";
        doc.BibliographyStyle = CitationStyle.Ieee;

        var book = new Source
        {
            Tag = "Knuth1997",
            Type = SourceType.Book,
            Author = "Knuth, Donald",
            Title = "The Art of Computer Programming",
            Publisher = "Addison-Wesley",
            Year = "1997"
        };
        var article = new Source
        {
            Tag = "Doe2024",
            Type = SourceType.JournalArticle,
            Author = "Jane Q. Doe; Alex Smith",
            Title = "Evidence-first document rendering",
            Journal = "Journal of Document Systems",
            Volume = "42",
            Issue = "2",
            Pages = "12-20",
            Year = "2024"
        };
        var web = new Source
        {
            Tag = "W3C2025",
            Type = SourceType.WebSite,
            Author = "World Wide Web Consortium",
            Title = "Digital publishing accessibility notes",
            Url = "https://www.w3.org/",
            Accessed = "2026-07-04",
            Year = "2025"
        };
        doc.Sources.AddRange([book, article, web]);

        doc.Blocks.Add(StyledParagraph("References Heavy Evidence", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style references output across visible CITATION fields, " +
            "a bibliography field, structured source types, hidden legal-authority marks, a cached TOA field, " +
            "and shared generated TOA page references."));

        doc.Blocks.Add(CitationParagraph(
            doc,
            "Numeric citations should preserve source-order markers: ",
            [book, article, web]));
        doc.Blocks.Add(CitationParagraph(
            doc,
            "Repeated citations should reuse the same marker: ",
            [article, book]));

        var caseCitation = new Citation("Example v. FreeW, 123 F.4th 456 (2026)", CitationCategory.Cases, "Example");
        var statuteCitation = new Citation("Free Software Evidence Act, 42 U.S.C. 2026", CitationCategory.Statutes, "FSEA");
        doc.Blocks.Add(AuthorityParagraph(
            "Marked authorities: Example v. FreeW and Free Software Evidence Act should be collected into the generated TOA region.",
            caseCitation,
            statuteCitation));
        doc.Blocks.Add(DocumentOps.CreatePageBreak());
        doc.Blocks.Add(AuthorityParagraph(
            "Second-page authority mark: Example v. FreeW should produce a shared generated TOA page-reference range.",
            caseCitation));

        for (var i = 1; i <= 16; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"References body paragraph {i}: filler text keeps the bibliography and table of authorities " +
                "near a later page while preserving citation-field metadata in the visual evidence manifest."));
        }

        doc.Blocks.Add(FieldParagraph(
            "Bibliography field cache: ",
            Run.ComplexFieldRun(" BIBLIOGRAPHY \\l 1033 ", "References")));
        doc.Blocks.AddRange(BibliographyRegionPlanner
            .BuildInsertPlan(doc, doc.Blocks.Count, CitationStyle.Ieee)
            .Paragraphs);

        doc.Blocks.Add(FieldParagraph(
            "TOA field cache with page-reference sentinel: ",
            Run.ComplexFieldRun(" TOA \\c 1 \\p ", "Cases\t1, 2")));
        doc.Blocks.AddRange(TableOfAuthoritiesRegionPlanner
            .BuildInsertPlan(doc, doc.Blocks.Count, new ToaOptions { TabLeader = ToaTabLeader.Dots })
            .Paragraphs);

        for (var i = 1; i <= 10; i++)
            doc.Blocks.Add(new Paragraph($"Closing references paragraph {i}: confirms late-page evidence remains nonblank."));

        return doc;
    }

    public static TextDocument BuildSectionGeometryDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Section 1: Portrait (8.5 x 11 in)", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This section is portrait. The page is taller than wide. A next-page section break below " +
            "this paragraph should switch to landscape."));

        for (var i = 1; i <= 4; i++)
            doc.Blocks.Add(new Paragraph($"Portrait section paragraph {i}: Standard letter-size portrait page."));

        var sectionMarker = new Paragraph("[ End of Portrait Section ]")
        {
            SectionBreak = new Section(
                new PageSettings
                {
                    WidthPt = 612,
                    HeightPt = 792,
                    Landscape = false,
                    MarginLeftPt = 72,
                    MarginRightPt = 72,
                    MarginTopPt = 72,
                    MarginBottomPt = 72
                },
                SectionBreakKind.NextPage)
        };
        doc.Blocks.Add(sectionMarker);

        doc.Page.WidthPt = 792;
        doc.Page.HeightPt = 612;
        doc.Page.Landscape = true;
        doc.Page.MarginLeftPt = 72;
        doc.Page.MarginRightPt = 72;
        doc.Page.MarginTopPt = 72;
        doc.Page.MarginBottomPt = 72;

        doc.Blocks.Add(StyledParagraph("Section 2: Landscape (11 x 8.5 in)", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This section should be landscape. If the section break rendered correctly the page is now " +
            "wider than tall, and this text spans a wider line length."));
        for (var i = 1; i <= 4; i++)
            doc.Blocks.Add(new Paragraph($"Landscape section paragraph {i}: Page is now wider than tall."));

        return doc;
    }

    public static TextDocument BuildTrackedChangesReviewDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Tracked Changes Test", "Heading1"));
        doc.Blocks.Add(new Paragraph("Insertions should be underlined; deletions should be struck-through."));

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("Normal text before. "));
        p1.Runs.Add(new Run("INSERTED text by Alice.")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-06-26T09:00:00Z"
        });
        p1.Runs.Add(new Run(" Normal text between. "));
        p1.Runs.Add(new Run("DELETED text by Bob.")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = "2026-06-26T09:30:00Z"
        });
        p1.Runs.Add(new Run(" Normal text after."));
        doc.Blocks.Add(p1);

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("This entire paragraph is a tracked insertion by Carol.")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Carol",
            RevisionDateXml = "2026-06-26T10:00:00Z"
        });
        doc.Blocks.Add(p2);

        var p3 = new Paragraph();
        p3.Runs.Add(new Run("Alice: "));
        p3.Runs.Add(new Run("inserted-by-alice ")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Alice",
            RevisionDateXml = "2026-06-26T09:00:00Z"
        });
        p3.Runs.Add(new Run("Bob: "));
        p3.Runs.Add(new Run("deleted-by-bob ")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Bob",
            RevisionDateXml = "2026-06-26T09:30:00Z"
        });
        p3.Runs.Add(new Run("Carol: "));
        p3.Runs.Add(new Run("inserted-by-carol")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Carol",
            RevisionDateXml = "2026-06-26T10:00:00Z"
        });
        doc.Blocks.Add(p3);

        for (var i = 1; i <= 40; i++)
            doc.Blocks.Add(new Paragraph($"Normal paragraph {i}: No tracked changes here."));

        return doc;
    }

    public static TextDocument BuildCommentsReviewDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Comments Test", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "Comment anchors should be highlighted. Comment content should appear as balloons or in a reviewing pane."));

        var p1 = new Paragraph();
        p1.Runs.Add(new Run("Text before the first comment anchor. "));
        p1.Runs.Add(Run.CommentReference(1));
        p1.Runs.Add(new Run("The first commented span.") { CommentId = 1 });
        p1.Runs.Add(new Run(" Text after the first comment anchor."));
        doc.Blocks.Add(p1);
        doc.Comments[1] = new Comment(
            1,
            "Comment 1 by Alice: This is the comment text. Should appear as a balloon in the right margin.")
        {
            Author = "Alice",
            Initials = "A",
            DateXml = "2026-06-26T09:00:00Z"
        };

        var p2 = new Paragraph();
        p2.Runs.Add(new Run("Second paragraph before comment. "));
        p2.Runs.Add(Run.CommentReference(2));
        p2.Runs.Add(new Run("Second commented phrase.") { CommentId = 2 });
        p2.Runs.Add(new Run(" End of second paragraph."));
        doc.Blocks.Add(p2);
        doc.Comments[2] = new Comment(
            2,
            "Comment 2 by Bob: Different author, distinct comment. Both should be visible.")
        {
            Author = "Bob",
            Initials = "B",
            DateXml = "2026-06-26T09:30:00Z"
        };

        for (var i = 1; i <= 35; i++)
            doc.Blocks.Add(new Paragraph($"Normal paragraph {i}: No comments here."));

        return doc;
    }

    public static TextDocument BuildComplexTableLayoutDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Complex Table Layout Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style table layout contracts: named style, preferred widths, " +
            "merged cells, vertical merges, repeated header row, banding, cell shading, custom borders, " +
            "cell margins, spacing, vertical text, and vertical alignment."));

        doc.Blocks.Add(BuildComplexTable());
        doc.Blocks.Add(new Paragraph(
            "The same model is rendered by WPF FidelityRender and Avalonia PageLayoutShot, and both emit " +
            "the shared table expectation into the visual evidence manifest."));

        return doc;
    }

    public static TextDocument BuildTablePaginationRepeatHeaderDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 396;
        doc.Page.MarginLeftPt = 36;
        doc.Page.MarginRightPt = 36;
        doc.Page.MarginTopPt = 36;
        doc.Page.MarginBottomPt = 36;
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(
            "The table below is intentionally tall enough to paginate. Row 4 is marked keep-together, " +
            "and the header row repeats on the second page."));

        var table = new Table
        {
            Formatting = new TableFormatting
            {
                Borders = true,
                HeaderRow = true,
                RepeatHeaderRow = true,
                BandedRows = true
            },
            TableStyleId = "GridTable1Light",
            PreferredWidthPt = 520,
            Alignment = TableAlignment.Center,
            AutoFit = AutoFitMode.Fixed,
            DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 6, BottomPt: 3, RightPt: 6)
        };
        table.ColumnWidthsPt.AddRange([150, 170, 200]);
        table.Rows.Add(new TableRow
        {
            HeightPt = 30,
            HeightRule = TableRowHeightRule.Exact,
            AllowBreakAcrossPages = false,
            Cells =
            {
                HeaderCell("Step"),
                HeaderCell("Owner"),
                HeaderCell("Pagination evidence")
            }
        });

        for (var row = 1; row <= 8; row++)
        {
            table.Rows.Add(new TableRow
            {
                HeightPt = 54,
                HeightRule = TableRowHeightRule.Exact,
                AllowBreakAcrossPages = row != 4 && row != 7,
                Cells =
                {
                    Cell($"Row {row}"),
                    Cell(row == 4 ? "Keep with row box" : "Flow row"),
                    Cell($"Body row {row} should retain banding and page assignment in the shared pagination plan.")
                }
            });
        }

        doc.Blocks.Add(table);
        return doc;
    }

    public static TextDocument BuildDrawingObjectsCompositionDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Drawing Object Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style drawing object composition: floating shapes, charts, " +
            "SmartArt, WordArt, grouping, wrap modes, behind-text layering, in-front layering, and z-order."));

        var anchor = new Paragraph();
        anchor.Runs.Add(new Run(
            "The drawing objects in this paragraph should retain their shared placement metadata while " +
            "WPF and Avalonia renderers emit a common visual-evidence manifest. "));
        anchor.Runs.Add(Run.FromShape(BuildFloatingShape()));
        anchor.Runs.Add(Run.FromImage(BuildFloatingEffectImage()));
        anchor.Runs.Add(Run.FromChart(BuildFloatingChart()));
        anchor.Runs.Add(Run.FromSmartArt(BuildFloatingSmartArt()));
        anchor.Runs.Add(Run.FromWordArt(BuildFloatingWordArt()));
        anchor.Runs.Add(Run.FromDrawingGroup(BuildFloatingGroup()));
        doc.Blocks.Add(anchor);

        for (var i = 1; i <= 10; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Drawing object body paragraph {i}: surrounding text gives square and top-and-bottom " +
                "wrap modes real layout context for comparison."));
        }

        return doc;
    }

    public static TextDocument BuildObjectFormatPositionSizeStyleDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Object Format Position Size Style") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture records selected drawing-object formatting: explicit position, size, " +
            "style effects, alt text, text wrapping, and z-order across WPF and Avalonia evidence."));

        var anchor = new Paragraph();
        anchor.Runs.Add(new Run(
            "The three selected objects should retain their object-format metadata while surrounding " +
            "text gives square and top-and-bottom wrapping real page context. "));
        anchor.Runs.Add(Run.FromShape(BuildObjectFormatBehindTextShape()));
        anchor.Runs.Add(Run.FromImage(BuildObjectFormatSquareImage()));
        anchor.Runs.Add(Run.FromWordArt(BuildObjectFormatWordArt()));
        doc.Blocks.Add(anchor);

        for (var i = 1; i <= 8; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Object format body paragraph {i}: this text surrounds selected drawing objects so wrap, " +
                "behind-text placement, in-front layering, and z-order remain visible in the capture."));
        }

        return doc;
    }

    public static TextDocument BuildChartSmartArtCompositionDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("Chart and SmartArt Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises Word-style chart and SmartArt visual planning: named chart " +
            "palettes, quick layouts, scatter markers, data labels, axis titles, plot fills, SmartArt " +
            "layouts, color schemes, styles, and node fill sequences."));

        var chartParagraph = new Paragraph();
        chartParagraph.Runs.Add(new Run("Column chart with quick-layout annotations: "));
        chartParagraph.Runs.Add(Run.FromChart(BuildQuickLayoutColumnChart()));
        doc.Blocks.Add(chartParagraph);

        var scatterParagraph = new Paragraph();
        scatterParagraph.Runs.Add(new Run("Scatter chart must render marker-only geometry: "));
        scatterParagraph.Runs.Add(Run.FromChart(BuildMarkerOnlyScatterChart()));
        doc.Blocks.Add(scatterParagraph);

        var smartArtParagraph = new Paragraph();
        smartArtParagraph.Runs.Add(new Run("SmartArt process colors and style: "));
        smartArtParagraph.Runs.Add(Run.FromSmartArt(BuildStyledSmartArt()));
        doc.Blocks.Add(smartArtParagraph);

        doc.Blocks.Add(new Paragraph(
            "The same model is rendered by WPF FidelityRender and Avalonia PageLayoutShot, and both " +
            "emit the shared chart/SmartArt expectation into the visual evidence manifest."));

        return doc;
    }

    public static TextDocument BuildWordArtWatermarkStressDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder = new PageBorder("#1F4E79", 2.25);
        doc.Page.WatermarkOptions = new WatermarkOptions("CONFIDENTIAL")
        {
            FontColorHex = "#A6A6A6",
            Opacity = 0.32,
            Layout = WatermarkLayout.Diagonal
        };

        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("WordArt And Watermark Stress") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture overlays multiple floating WordArt objects on a diagonal text watermark " +
            "and a page border so WPF and Avalonia must report the same visual-evidence contract."));

        var anchor = new Paragraph();
        anchor.Runs.Add(new Run(
            "WordArt objects should remain visible above the watermark, keep their z-order metadata, " +
            "and preserve square/in-front wrapping in both renderers. "));
        anchor.Runs.Add(Run.FromShape(BuildWatermarkStressBackingShape()));
        anchor.Runs.Add(Run.FromWordArt(BuildPrimaryStressWordArt()));
        anchor.Runs.Add(Run.FromWordArt(BuildSecondaryStressWordArt()));
        doc.Blocks.Add(anchor);

        for (var i = 1; i <= 12; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Watermark stress paragraph {i}: body text gives the watermark and floating WordArt " +
                "real page-composition context for visual comparison."));
        }

        return doc;
    }

    public static TextDocument BuildWordArtPictureWatermarkLayoutDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.PageBorder = new PageBorder("#1F4E79", 2.25)
        {
            LineStyle = BorderLineStyle.Double
        };
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnSpacingPt = 30;
        doc.Page.ColumnsLineBetween = true;
        doc.Page.WatermarkOptions = new WatermarkOptions(string.Empty)
        {
            ImageBytes = BuildGeneratedWatermarkPngBytes(),
            ScalePct = 48,
            Layout = WatermarkLayout.Horizontal,
            Opacity = 0.38
        };

        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph("WordArt and Picture Watermark Fidelity") { StyleId = "Heading1" });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture stresses page layout, a centered picture watermark, page border, " +
            "columns, body text, and floating WordArt in one Word-comparable capture."));

        var anchor = new Paragraph();
        anchor.Runs.Add(new Run(
            "The decorative WordArt should sit above the text layer while the picture watermark remains " +
            "behind the body content. "));
        anchor.Runs.Add(Run.FromWordArt(new WordArt("WATERMARK", WordArtStyle.GradFillMulti, 34)
        {
            AltText = "WordArt watermark stress label",
            Warp = WordArtWarp.ArchUp,
            Placement = Placement(ImageWrapping.InFront, xPt: 205, yPt: 38, zOrder: 9)
        }));
        doc.Blocks.Add(anchor);

        for (var i = 1; i <= 18; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Watermark layout paragraph {i}: body text should remain readable across both columns " +
                "with the generated picture watermark centered behind the text and inside the border."));
        }

        return doc;
    }

    public static TextDocument BuildBackstagePrintExportDocument(string title, string description)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.MarginTopPt = 60;
        doc.Page.MarginBottomPt = 60;
        doc.Page.MarginLeftPt = 54;
        doc.Page.MarginRightPt = 54;
        doc.Page.ColumnCount = 2;
        doc.Page.ColumnSpacingPt = 36;
        doc.Page.ColumnsLineBetween = true;
        doc.Page.PageBorder = new PageBorder("#24536B", 1.5);
        doc.Page.WatermarkOptions = new WatermarkOptions("PRINT COPY")
        {
            FontColorHex = "#74828A",
            Opacity = 0.18,
            Layout = WatermarkLayout.Diagonal,
        };
        doc.FinalSectionHeadersFooters.Header = new HeaderFooter(title);
        doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("FreeW backstage print/export evidence");

        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph(title, "Heading1"));
        doc.Blocks.Add(new Paragraph(description));
        doc.Blocks.Add(new Paragraph(
            "The first two rendered pages are retained as real PNG evidence and normalized through the shared visual evidence manifest."));
        doc.Blocks.Add(new Paragraph(
            "This fixture intentionally includes headers, footers, columns, page border, watermark, margins, and body pagination so Backstage Print Preview and PDF export evidence is stronger than a plain text capture."));

        for (var i = 1; i <= 72; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Backstage fixed-layout paragraph {i}: body text, pagination, page chrome, column flow, and header/footer composition must survive the renderer capture path."));
        }

        return doc;
    }

    private static Paragraph StyledParagraph(string text, string styleId) =>
        new(text) { StyleId = styleId };

    private static HeaderFooter FieldHeaderFooter(params Run[] runs)
    {
        var headerFooter = new HeaderFooter();
        var paragraph = new Paragraph();
        paragraph.Runs.AddRange(runs);
        headerFooter.Paragraphs.Add(paragraph);
        return headerFooter;
    }

    private static Table BuildComplexTable()
    {
        var table = new Table
        {
            Formatting = new TableFormatting
            {
                Borders = true,
                HeaderRow = true,
                RepeatHeaderRow = true,
                BandedRows = true,
                FirstColumn = true
            },
            TableStyleId = "GridTable4",
            PreferredWidthPt = 468,
            Alignment = TableAlignment.Center,
            DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 8, BottomPt: 3, RightPt: 8),
            CellSpacingPt = 2.4,
            AutoFit = AutoFitMode.Fixed
        };
        table.ColumnWidthsPt.AddRange([108, 96, 96, 168]);

        table.Rows.Add(new TableRow
        {
            HeightPt = 30,
            HeightRule = TableRowHeightRule.AtLeast,
            AllowBreakAcrossPages = false,
            Cells =
            {
                HeaderCell("Region", gridSpan: 2),
                HeaderCell("FY2026 outlook", gridSpan: 2)
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 36,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("North account group", shading: "#EAF2F8", verticalMerge: VerticalMergeState.Restart),
                Cell("Q1\n$1.20M", verticalAlignment: TableCellVerticalAlignment.Center),
                Cell("Q2\n$1.42M", verticalAlignment: TableCellVerticalAlignment.Center),
                Cell("Key account", textDirection: CellTextDirection.Rotate90, shading: "#FFF2CC")
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 36,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell(string.Empty, verticalMerge: VerticalMergeState.Continue),
                Cell("Q3\n$1.36M"),
                Cell("Q4\n$1.51M"),
                Cell("Renewal review", verticalAlignment: TableCellVerticalAlignment.Bottom)
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 34,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("South", shading: "#FCE4D6"),
                Cell("Launch", gridSpan: 2, shading: "#E2F0D9"),
                Cell("Merged forecast cell")
            }
        });

        table.Rows.Add(new TableRow
        {
            HeightPt = 32,
            HeightRule = TableRowHeightRule.AtLeast,
            Cells =
            {
                Cell("Total", gridSpan: 2, shading: "#D9EAD3", customBorder: true),
                Cell("$5.49M", gridSpan: 2, shading: "#D9EAD3", customBorder: true)
            }
        });

        return table;
    }

    private static Shape BuildFloatingShape()
    {
        var shape = Shape.TextBoxWith("Behind text box\nwith shadow", widthPt: 150, heightPt: 60, fillColorHex: "#D9EAD3");
        shape.OutlineColorHex = "#38761D";
        shape.OutlineWidthPt = 1.5;
        shape.Placement = Placement(ImageWrapping.Behind, xPt: 18, yPt: 12, zOrder: 1);
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowAlpha = 35000 };
        return shape;
    }

    private static Shape BuildObjectFormatBehindTextShape()
    {
        var shape = Shape.TextBoxWith("Behind text\n150 x 64 pt", widthPt: 150, heightPt: 64, fillColorHex: "#FCE4D6");
        shape.OutlineColorHex = "#C55A11";
        shape.OutlineWidthPt = 1.75;
        shape.OutlineDash = "dash";
        shape.AltText = "Behind text callout with shadow and bevel";
        shape.Placement = Placement(ImageWrapping.Behind, xPt: 24, yPt: 42, zOrder: 1);
        shape.Effects = new ShapeEffectLst
        {
            HasShadow = true,
            ShadowAlpha = 32000,
            HasBevel = true
        };
        return shape;
    }

    private static InlineImage BuildFloatingEffectImage() =>
        new(BuildGeneratedWatermarkPngBytes(), widthPt: 126, heightPt: 72)
        {
            AltText = "Floating image with shadow glow reflection and artistic effect",
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 150,
            VerticalOffsetPt = 34,
            ZOrderIndex = 5,
            ShadowPreset = 2,
            GlowSizePt = 5,
            GlowColorHex = "5B9BD5",
            ReflectionPreset = 1,
            ArtisticEffect = ImageArtisticEffect.GlowDiffused
        };

    private static InlineImage BuildObjectFormatSquareImage() =>
        new(BuildGeneratedWatermarkPngBytes(), widthPt: 132, heightPt: 84)
        {
            AltText = "Square wrapped sample picture with glow reflection soft edge and artistic effect",
            Wrapping = ImageWrapping.Square,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            HorizontalOffsetPt = 174,
            VerticalOffsetPt = 60,
            ZOrderIndex = 5,
            ShadowPreset = 3,
            GlowSizePt = 6,
            GlowColorHex = "70AD47",
            ReflectionPreset = 2,
            SoftEdgePt = 2,
            BevelPreset = 1,
            ArtisticEffect = ImageArtisticEffect.GlowDiffused
        };

    private static Chart BuildFloatingChart()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [1.2, 1.7, 1.4, 2.1],
            seriesName: "Revenue",
            title: "Quarterly revenue");
        chart.WidthPt = 210;
        chart.HeightPt = 126;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        chart.Placement = Placement(ImageWrapping.TopAndBottom, xPt: 210, yPt: 120, zOrder: 4);

        return chart;
    }

    private static Chart BuildQuickLayoutColumnChart()
    {
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [1.4, 1.8, 1.6, 2.2],
            seriesName: "Revenue",
            title: "Revenue by quarter");
        chart.WidthPt = 300;
        chart.HeightPt = 168;
        chart.ColorSchemeId = "mono-blue";
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        return chart;
    }

    private static Chart BuildMarkerOnlyScatterChart()
    {
        var chart = Chart.Create(
            ChartKind.Scatter,
            ["155", "160", "165", "170"],
            [52, 58, 62, 66],
            seriesName: "Sample",
            title: "Height and weight");
        chart.WidthPt = 270;
        chart.HeightPt = 150;
        chart.ColorSchemeId = "colorful1";
        chart.StyleId = 4;
        chart.ShowLegend = false;
        chart.CategoryAxisTitle = "Height";
        chart.ValueAxisTitle = "Weight";
        return chart;
    }

    private static SmartArt BuildFloatingSmartArt()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.WidthPt = 216;
        smartArt.HeightPt = 90;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "colorful1";
        smartArt.StyleId = "subtle1";
        smartArt.Placement = Placement(ImageWrapping.Square, xPt: 36, yPt: 210, zOrder: 6);

        return smartArt;
    }

    private static SmartArt BuildStyledSmartArt()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.WidthPt = 300;
        smartArt.HeightPt = 110;
        smartArt.LayoutId = "stepup1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "intense1";
        return smartArt;
    }

    private static WordArt BuildFloatingWordArt() =>
        new("FreeW", WordArtStyle.GlowBlue, fontSizePt: 30)
        {
            AltText = "Floating WordArt",
            Warp = WordArtWarp.Wave1,
            Placement = Placement(ImageWrapping.InFront, xPt: 300, yPt: 30, zOrder: 8)
        };

    private static WordArt BuildObjectFormatWordArt() =>
        new("FORMAT", WordArtStyle.GlowGold, fontSizePt: 28)
        {
            AltText = "Top and bottom wrapped WordArt format label",
            Warp = WordArtWarp.ArchUp,
            Placement = Placement(ImageWrapping.TopAndBottom, xPt: 292, yPt: 146, zOrder: 9)
        };

    private static Shape BuildWatermarkStressBackingShape()
    {
        var shape = Shape.TextBoxWith("watermark backing layer", widthPt: 170, heightPt: 58, fillColorHex: "#E2F0D9");
        shape.OutlineColorHex = "#70AD47";
        shape.OutlineWidthPt = 1.25;
        shape.Placement = Placement(ImageWrapping.Square, xPt: 60, yPt: 72, zOrder: 2);
        shape.Effects = new ShapeEffectLst { HasShadow = true, ShadowAlpha = 28000 };
        return shape;
    }

    private static WordArt BuildPrimaryStressWordArt() =>
        new("FreeW CONFIDENTIAL", WordArtStyle.GlowBlue, fontSizePt: 32)
        {
            AltText = "Primary WordArt watermark stress",
            Warp = WordArtWarp.Wave1,
            Placement = Placement(ImageWrapping.InFront, xPt: 170, yPt: 38, zOrder: 7)
        };

    private static WordArt BuildSecondaryStressWordArt() =>
        new("Review Copy", WordArtStyle.FillGold, fontSizePt: 26)
        {
            AltText = "Secondary WordArt watermark stress",
            Warp = WordArtWarp.ArchUp,
            Placement = Placement(ImageWrapping.Square, xPt: 260, yPt: 142, zOrder: 9)
        };

    private static DrawingGroup BuildFloatingGroup()
    {
        var group = new DrawingGroup
        {
            WidthPt = 180,
            HeightPt = 82,
            Placement = Placement(ImageWrapping.Square, xPt: 280, yPt: 260, zOrder: 10)
        };
        group.Children.Add(new Shape(ShapeKind.Ellipse, 82, 50)
        {
            FillColorHex = "#CFE2F3",
            OutlineColorHex = "#1155CC",
            Effects = new ShapeEffectLst { HasGlow = true, GlowColorHex = "4472C4", GlowRad = 63500 }
        });
        group.ChildOffsets.Add((0, 16));
        group.Children.Add(new WordArt("Group", WordArtStyle.FillGold, 22));
        group.ChildOffsets.Add((70, 8));
        return group;
    }

    private static FloatingPlacement Placement(
        ImageWrapping wrapping,
        double xPt,
        double yPt,
        int zOrder) =>
        new()
        {
            Wrapping = wrapping,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Paragraph,
            HorizontalOffsetPt = xPt,
            VerticalOffsetPt = yPt,
            ZOrderIndex = zOrder
        };

    private static Paragraph CitationParagraph(TextDocument document, string prefix, IReadOnlyList<Source> sources)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(prefix));
        for (var i = 0; i < sources.Count; i++)
        {
            if (i > 0)
                paragraph.Runs.Add(new Run(" "));

            if (Citations.TryCreateCitationFieldRun(document, sources[i], document.BibliographyStyle, out var fieldRun))
                paragraph.Runs.Add(fieldRun);
            else
                paragraph.Runs.Add(new Run(Citations.FormatInText(document, sources[i], document.BibliographyStyle)));
        }

        return paragraph;
    }

    private static Paragraph AuthorityParagraph(string visibleText, params Citation[] citations)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(visibleText));
        foreach (var citation in citations)
            paragraph.Runs.Add(Run.CitationMark(citation));
        return paragraph;
    }

    private static Paragraph FieldParagraph(string prefix, Run fieldRun)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(prefix));
        paragraph.Runs.Add(fieldRun);
        return paragraph;
    }

    private static byte[] BuildGeneratedWatermarkPngBytes()
    {
        const int width = 120;
        const int height = 72;
        var rows = new byte[(1 + width * 4) * height];
        var offset = 0;
        for (var y = 0; y < height; y++)
        {
            rows[offset++] = 0;
            for (var x = 0; x < width; x++)
            {
                var inFrame = x < 5 || x >= width - 5 || y < 5 || y >= height - 5;
                var inBand = Math.Abs(y - (height - 1 - x * height / width)) <= 4;
                var inMark = x is > 22 and < 98 && y is > 24 and < 48 && ((x / 8) + (y / 8)) % 2 == 0;

                byte r = 0;
                byte g = 0;
                byte b = 0;
                byte a = 0;
                if (inFrame)
                {
                    r = 0x1F;
                    g = 0x4E;
                    b = 0x79;
                    a = 0xFF;
                }
                else if (inBand)
                {
                    r = 0xED;
                    g = 0x7D;
                    b = 0x31;
                    a = 0xE8;
                }
                else if (inMark)
                {
                    r = 0x70;
                    g = 0xAD;
                    b = 0x47;
                    a = 0xD8;
                }

                rows[offset++] = r;
                rows[offset++] = g;
                rows[offset++] = b;
                rows[offset++] = a;
            }
        }

        using var png = new MemoryStream();
        png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(png, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(rows);
        WritePngChunk(png, "IDAT", compressed.ToArray());
        WritePngChunk(png, "IEND", ReadOnlySpan<byte>.Empty);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream stream, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        var crc = UpdateCrc(0xFFFFFFFF, typeBytes);
        crc = UpdateCrc(crc, data);
        crc ^= 0xFFFFFFFF;

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) == 1 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }

    private static TableCell HeaderCell(string text, int gridSpan = 1) =>
        Cell(text, gridSpan: gridSpan, shading: "#D9E2F3", customBorder: true);

    private static TableCell Cell(
        string text,
        int gridSpan = 1,
        string? shading = null,
        bool customBorder = false,
        VerticalMergeState verticalMerge = VerticalMergeState.None,
        CellTextDirection textDirection = CellTextDirection.Horizontal,
        TableCellVerticalAlignment verticalAlignment = TableCellVerticalAlignment.Top)
    {
        var cell = new TableCell(text)
        {
            GridSpan = Math.Max(1, gridSpan),
            ShadingColorHex = shading,
            VerticalMerge = verticalMerge,
            TextDirection = textDirection,
            VerticalAlignment = verticalAlignment,
            Margins = new TableCellMargins(TopPt: 2, LeftPt: 6, BottomPt: 2, RightPt: 6)
        };

        if (customBorder)
        {
            cell.Borders = new CellBorders
            {
                Top = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.25),
                Bottom = new CellBorderEdge(BorderLineStyle.Thick, "#1F4E79", 1.25),
                Left = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75),
                Right = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75)
            };
        }

        return cell;
    }
}
