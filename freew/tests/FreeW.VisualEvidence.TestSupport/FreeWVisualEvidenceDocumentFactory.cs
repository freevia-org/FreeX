using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using FreeW.Core.Model;
using FreeW.App.Presentation.Dialogs;
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
        doc.Page.PageNumberFormat = PageNumberFormat.Decimal;
        doc.Page.PageNumberStartAt = 1;
        doc.Page.PageNumberChapterStyleLevel = 1;
        doc.Page.PageNumberChapterSeparator = PageNumberChapterSeparator.Hyphen;

        doc.FinalSectionHeadersFooters.FirstHeader = FieldHeaderFooter(
            new Run("First header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("4"));
        doc.FinalSectionHeadersFooters.FirstFooter = FieldHeaderFooter(
            new Run("First footer complex page "),
            Run.ComplexFieldRun(" PAGE ", "1"),
            new Run(" / "),
            Run.ComplexFieldRun(" NUMPAGES ", "4"));
        doc.FinalSectionHeadersFooters.EvenHeader = FieldHeaderFooter(
            new Run("Even header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("4"));
        doc.FinalSectionHeadersFooters.EvenFooter = FieldHeaderFooter(
            new Run("Even footer title: "),
            Run.TitleField("Field Page Number Evidence"));
        doc.FinalSectionHeadersFooters.Header = FieldHeaderFooter(
            new Run("Default header page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("4"));
        doc.FinalSectionHeadersFooters.Footer = FieldHeaderFooter(
            new Run("Default footer author: "),
            Run.AuthorField("FreeW Visual Evidence"),
            new Run(" | page "),
            Run.PageNumberField());

        // Word only resolves pgNumType/chapStyle when the matching heading participates in a
        // numbered outline. Keep the fixture's chapter-prefixed PAGE contract valid in OOXML.
        doc.Blocks.Add(new Paragraph("Field/Page Number Variants")
        {
            StyleId = "Heading1",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.MultiLevel }
        });
        doc.Blocks.Add(new Paragraph(
            "This shared fixture exercises PAGE and NUMPAGES fields across first, even, and default " +
            "header/footer slots, chapter-prefixed page numbering, plus document-property fields in the body."));

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
            Run.ComplexFieldRun(" TOA \\h \\c \"1\" ", "Cases\t1, 2")));
        doc.Blocks.AddRange(TableOfAuthoritiesRegionPlanner
            .BuildInsertPlan(doc, doc.Blocks.Count, new ToaOptions { TabLeader = ToaTabLeader.Dots })
            .Paragraphs);

        for (var i = 1; i <= 10; i++)
            doc.Blocks.Add(new Paragraph($"Closing references paragraph {i}: confirms late-page evidence remains nonblank."));

        return doc;
    }

    public static TextDocument BuildLegalReferenceSectionPageNumbersDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Legal Reference Section Page Numbers";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Subject = "Section-formatted Table of Authorities page references";
        doc.Properties.Keywords = "TOA, section page numbers, roman, restart";
        doc.Properties.Comments = "Exercises displayed TOA page references where physical page 1 displays i and the main section restarts at 1.";

        var frontMatterPage = doc.Page.Clone();
        frontMatterPage.PageNumberFormat = PageNumberFormat.LowerRoman;
        frontMatterPage.PageNumberStartAt = 1;
        doc.Page.PageNumberFormat = PageNumberFormat.Decimal;
        doc.Page.PageNumberStartAt = 1;

        var sectionedCase = new Citation(
            "Matter of Sectioned Pages, 101 F. Supp. 3d 2026 (D. FreeW)",
            CitationCategory.Cases,
            "Sectioned Pages");
        var restartStatute = new Citation(
            "Restart Numbering Act, 7 FreeW Code 13",
            CitationCategory.Statutes,
            "RNA");

        doc.Blocks.Add(StyledParagraph("Table of Authorities - Front Matter", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The front matter uses lower-roman page numbering. The marked authority below should be " +
            "reported by its displayed page reference i even though it is physical page 1."));
        doc.Blocks.Add(AuthorityParagraph(
            "Front-matter authority mark: Matter of Sectioned Pages appears before the main section restart.",
            sectionedCase));

        doc.Blocks.Add(new Paragraph("End of front matter")
        {
            SectionBreak = new Section(frontMatterPage, SectionBreakKind.NextPage)
        });

        doc.Blocks.Add(StyledParagraph("Main Matter", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The main matter restarts page numbering at decimal 1. The same case and a statute are marked " +
            "here so generated TOA evidence must keep physical pages distinct from displayed page text."));
        doc.Blocks.Add(AuthorityParagraph(
            "Main authority marks: Matter of Sectioned Pages and the Restart Numbering Act appear after restart.",
            sectionedCase,
            restartStatute));

        for (var i = 1; i <= 18; i++)
        {
            doc.Blocks.Add(new Paragraph(
                $"Main section reference body paragraph {i}: filler keeps the generated table of authorities " +
                "on the restarted section capture while preserving section-formatted page-reference metadata."));
        }

        doc.Blocks.Add(FieldParagraph(
            "TOA field cache with section-formatted page-reference sentinel: ",
            Run.ComplexFieldRun(" TOA \\h \\c \"1\" ", "Cases\ti, 1")));
        doc.Blocks.AddRange(TableOfAuthoritiesRegionPlanner
            .BuildInsertPlan(
                doc,
                doc.Blocks.Count,
                new ToaOptions { TabLeader = ToaTabLeader.Dots },
                PageNumberFormatDialogPlanner.BuildCitationPageReferenceResolver(doc))
            .Paragraphs);

        doc.Blocks.Add(new Paragraph(
            "Closing paragraph: semantic evidence remains separate from authoritative MS Word PNG baselines."));

        return doc;
    }

    public static TextDocument BuildEquationStructuresDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Equation Structures Evidence";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Subject = "OfficeMath visual structure parity";
        doc.Properties.Keywords = "OfficeMath, equations, visual evidence";
        doc.Properties.Comments = "Exercises shared equation visual planning evidence for WPF and Avalonia.";

        doc.Page.MarginTopPt = 54;
        doc.Page.MarginBottomPt = 54;
        doc.Page.MarginLeftPt = 54;
        doc.Page.MarginRightPt = 54;

        doc.Blocks.Add(StyledParagraph("Equation Structures Evidence", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This generated fixture keeps every currently modeled OfficeMath visual structure on a bounded page " +
            "so WPF and Avalonia evidence captures use the same shared equation planner inputs."));

        doc.Blocks.Add(EquationParagraph(
            "Scripts: ",
            new Equation([
                MathRun.PlainText("E = m"),
                MathRun.Superscript("c", "2"),
                MathRun.PlainText("; "),
                MathRun.Subscript("x", "i"),
                MathRun.PlainText("; "),
                MathRun.SubSuperscript("T", "n", "2")
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Fraction and radical: ",
            new Equation([
                MathRun.Fraction("a + b", "c + d"),
                MathRun.PlainText(" = "),
                MathRun.Radical("x + 1", "3")
            ])));
        doc.Blocks.Add(EquationParagraph(
            "N-ary operator: ",
            new Equation([
                MathRun.NAry("\u2211", "i=1", "n", "i^2"),
                MathRun.PlainText(" + "),
                MathRun.NAry("\u222B", "0", "1", "f(x) dx")
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Matrix: ",
            new Equation([
                MathRun.MatrixOf(new MathMatrix([["1", "0"], ["0", "1"]]))
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Equation array: ",
            new Equation([
                MathRun.EquationArrayOf(new MathMatrix([["x = 1"], ["y = 2"]]))
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Accents and bars: ",
            new Equation([
                MathRun.AccentOf("x", "\u0302"),
                MathRun.PlainText(" "),
                MathRun.BarOf("y"),
                MathRun.PlainText(" "),
                MathRun.BarOf("z", top: false)
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Delimiter and group character: ",
            new Equation([
                MathRun.Delimiter("a + b", "[", "]"),
                MathRun.PlainText(" "),
                MathRun.GroupCharOf("u + v", "\u23DE", "top"),
                MathRun.PlainText(" "),
                MathRun.GroupCharOf("r + s", "\u23DF", "bot")
            ])));
        doc.Blocks.Add(EquationParagraph(
            "Function apply: ",
            new Equation([
                MathRun.FunctionApply("sin", "x + y"),
                MathRun.PlainText(" + "),
                MathRun.FunctionApply("log", "n")
            ])));

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

    /// <summary>
    /// Small review-markup fixture whose only comment anchors live outside the main body story.
    /// The body footnote reference keeps the note visible in Word's printed page while header,
    /// footer, and footnote ranges verify that DOCX round-tripping preserves non-body comment stories.
    /// The printable Word/Avalonia page is intentionally kept separate from FidelityRender's explicit
    /// review-markup balloon overlay; this fixture asserts shared semantic coverage, not balloon pixels
    /// across those two presentation modes.
    /// </summary>
    public static TextDocument BuildOutOfBodyCommentsReviewDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Properties.Title = "Out-of-body review comments";
        doc.Properties.Comments = "Header, footer, and footnote comment anchor visual evidence.";

        var header = new HeaderFooter();
        var headerParagraph = new Paragraph();
        headerParagraph.Runs.Add(new Run("Header review anchor: "));
        headerParagraph.Runs.Add(new Run("header note") { CommentId = 31 });
        headerParagraph.Runs.Add(Run.CommentReference(31));
        header.Paragraphs.Add(headerParagraph);
        doc.FinalSectionHeadersFooters.Header = header;

        var footer = new HeaderFooter();
        var footerParagraph = new Paragraph();
        footerParagraph.Runs.Add(new Run("Footer review anchor: "));
        footerParagraph.Runs.Add(new Run("footer note") { CommentId = 32 });
        footerParagraph.Runs.Add(Run.CommentReference(32));
        footer.Paragraphs.Add(footerParagraph);
        doc.FinalSectionHeadersFooters.Footer = footer;

        doc.Blocks.Add(StyledParagraph("Out-of-body review comments", "Heading1"));
        var body = new Paragraph();
        body.Runs.Add(new Run("This page has no body comment anchor; its review comments are attached to the header and footnote"));
        body.Runs.Add(Run.FootnoteReference(1));
        body.Runs.Add(new Run("."));
        doc.Blocks.Add(body);

        var footnote = new Footnote(1);
        var footnoteParagraph = new Paragraph();
        footnoteParagraph.Runs.Add(new Run("Footnote review anchor: "));
        footnoteParagraph.Runs.Add(new Run("footnote note") { CommentId = 33 });
        footnoteParagraph.Runs.Add(Run.CommentReference(33));
        footnote.Content.Add(footnoteParagraph);
        doc.Footnotes[1] = footnote;

        doc.Comments[31] = new Comment(31, "Header comment by Alice: retained outside the body story.")
        {
            Author = "Alice",
            Initials = "A",
            DateXml = "2026-08-21T09:00:00Z"
        };
        doc.Comments[32] = new Comment(32, "Footer comment by Bob: retained outside the body story.")
        {
            Author = "Bob",
            Initials = "B",
            DateXml = "2026-08-21T09:05:00Z"
        };
        doc.Comments[33] = new Comment(33, "Footnote comment by Carol: retained outside the body story.")
        {
            Author = "Carol",
            Initials = "C",
            DateXml = "2026-08-21T09:10:00Z"
        };

        return doc;
    }

    public static TextDocument BuildReviewProofingVisualDepthDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Review And Proofing Visual Depth", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "This shared fixture combines visible review marks, threaded comments, table anchors, and proofing language diagnostics on one bounded first page."));

        var revisions = new Paragraph();
        revisions.Runs.Add(new Run("Review sentence keeps normal text, "));
        revisions.Runs.Add(new Run("inserted wording")
        {
            Revision = RevisionKind.Inserted,
            RevisionAuthor = "Maya",
            RevisionDateXml = "2026-07-05T09:10:00Z"
        });
        revisions.Runs.Add(new Run(", "));
        revisions.Runs.Add(new Run("removed wording")
        {
            Revision = RevisionKind.Deleted,
            RevisionAuthor = "Noah",
            RevisionDateXml = "2026-07-05T09:20:00Z"
        });
        revisions.Runs.Add(new Run(", and "));
        revisions.Runs.Add(new Run("formatted emphasis")
        {
            Formatting = new RunFormatting
            {
                Bold = true,
                Underline = true,
                ColorHex = "#C00000",
                HighlightColorHex = "#FFF2CC"
            },
            FormatRevision = new FormatRevision(
                RunFormatting.Default,
                "Priya",
                "2026-07-05T09:35:00Z")
        });
        revisions.Runs.Add(new Run(" in the same line."));
        doc.Blocks.Add(revisions);

        var openComment = new Paragraph();
        openComment.Runs.Add(new Run("Body comment anchor before "));
        openComment.Runs.Add(new Run("open thread with reply") { CommentId = 10 });
        openComment.Runs.Add(Run.CommentReference(10));
        openComment.Runs.Add(new Run(" after the marked words."));
        doc.Blocks.Add(openComment);
        var comment10 = new Comment(10, "Open note: verify the body anchor highlight and balloon.", "Alice", "A")
        {
            DateXml = "2026-07-05T10:00:00Z"
        };
        comment10.AddReply(11, "Reply keeps the thread count visible.", "Ben", "B").DateXml = "2026-07-05T10:05:00Z";
        doc.Comments[10] = comment10;

        var resolvedComment = new Paragraph();
        resolvedComment.Runs.Add(new Run("Resolved comment anchor before "));
        resolvedComment.Runs.Add(new Run("completed reviewer thread") { CommentId = 12 });
        resolvedComment.Runs.Add(Run.CommentReference(12));
        resolvedComment.Runs.Add(new Run(" with replies retained."));
        doc.Blocks.Add(resolvedComment);
        var comment12 = new Comment(12, "Resolved note: wording was clarified.", "Casey", "C")
        {
            DateXml = "2026-07-05T10:15:00Z",
            Resolved = true
        };
        comment12.AddReply(13, "Clarification accepted.", "Maya", "M").DateXml = "2026-07-05T10:17:00Z";
        comment12.AddReply(14, "Leaving this resolved for visual evidence.", "Casey", "C").DateXml = "2026-07-05T10:19:00Z";
        doc.Comments[12] = comment12;

        var proofing = new Paragraph();
        proofing.Runs.Add(new Run("Proofing diagnostics: "));
        proofing.Runs.Add(new Run("teh ") { Formatting = new RunFormatting { LanguageTag = "en-US" } });
        proofing.Runs.Add(new Run("recieve ") { Formatting = new RunFormatting { LanguageTag = "en-GB" } });
        proofing.Runs.Add(new Run("acommodate ") { Formatting = new RunFormatting { LanguageTag = "fr-FR" } });
        proofing.Runs.Add(new Run("the ") { Formatting = new RunFormatting { LanguageTag = "en-US" } });
        proofing.Runs.Add(new Run("the ") { Formatting = new RunFormatting { LanguageTag = "en-US" } });
        proofing.Runs.Add(new Run("tokens carry explicit proofing languages."));
        doc.Blocks.Add(proofing);

        doc.Blocks.Add(BuildReviewProofingTable());
        doc.Blocks.Add(new Paragraph(
            "End of bounded review/proofing fixture: both renderers should emit the shared manifest row for this page."));

        var comment20 = new Comment(20, "Table note: anchor is inside table text.", "Devon", "D")
        {
            DateXml = "2026-07-05T10:30:00Z",
            Resolved = true
        };
        comment20.AddReply(21, "Table reply retained.", "Eli", "E").DateXml = "2026-07-05T10:35:00Z";
        doc.Comments[20] = comment20;

        return doc;
    }

    public static TextDocument BuildReviewProtectionProofingEvidenceDocument()
    {
        var doc = BuildReviewProofingVisualDepthDocument();
        doc.Properties.Title = "Review Protection Proofing Evidence";
        doc.Properties.Comments = "Bounded CommentsOnly protection state plus Mark as Final review/proofing visual evidence.";
        doc.Protection = new ProtectionSettings(ProtectionMode.CommentsOnly);
        doc.MarkedAsFinal = true;
        return doc;
    }

    public static TextDocument BuildReviewCompareVisualProofDocument()
    {
        var original = BuildReviewCompareOriginalDocument();
        var revised = BuildReviewCompareRevisedDocument();
        AddReviewRetainedModelSafety(revised, "compare");
        var doc = DocumentCompare.Compare(
            original,
            revised,
            "Riley",
            "2026-07-13T09:00:00Z");

        doc.Properties.Title = "Review Compare Visual Proof";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Comments =
            "Generated through DocumentCompare so Review compare visual evidence stays shared-first.";
        doc.Blocks.Insert(
            0,
            new Paragraph(
                "Compare proof: this generated blackline uses the shared DocumentCompare engine and should render insertions and deletions consistently in WPF and Avalonia.")
            {
                StyleId = "Heading1"
            });
        return doc;
    }

    public static TextDocument BuildReviewCombineVisualProofDocument()
    {
        var original = BuildReviewCombineOriginalDocument();
        var revisedA = BuildReviewCombineReviewerADocument();
        var revisedB = BuildReviewCombineReviewerBDocument();
        AddReviewRetainedModelSafety(revisedB, "combine");
        var doc = DocumentCombine.Combine(
            original,
            revisedA,
            "Alice",
            revisedB,
            "Bob",
            "2026-07-13T09:30:00Z");

        doc.Properties.Title = "Review Combine Visual Proof";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Comments =
            "Generated through DocumentCombine so multi-author combine visual evidence stays shared-first.";
        doc.Blocks.Insert(
            0,
            new Paragraph(
                "Combine proof: this generated blackline uses the shared DocumentCombine engine and should retain both reviewer authors in WPF and Avalonia evidence.")
            {
                StyleId = "Heading1"
            });
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

    public static TextDocument BuildTablePageCompositionStressDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        Captions.EnsureStyles(doc);

        doc.Properties.Title = "Table Page Composition Stress";
        doc.Properties.Author = "FreeW Visual Evidence";
        doc.Properties.Subject = "Combined table and page composition visual parity";
        doc.Page.WidthPt = 612;
        doc.Page.HeightPt = 396;
        doc.Page.MarginLeftPt = 42;
        doc.Page.MarginRightPt = 42;
        doc.Page.MarginTopPt = 42;
        doc.Page.MarginBottomPt = 42;
        doc.Page.HeaderDistancePt = 18;
        doc.Page.FooterDistancePt = 18;
        doc.Page.PageBorder = new PageBorder("#24536B", 1.5)
        {
            LineStyle = BorderLineStyle.Double
        };
        doc.Page.WatermarkOptions = new WatermarkOptions("TABLE REVIEW")
        {
            FontColorHex = "#7F7F7F",
            Opacity = 0.22,
            Layout = WatermarkLayout.Diagonal
        };

        doc.FinalSectionHeadersFooters.Header = FieldHeaderFooter(
            new Run("Table composition stress page "),
            Run.PageNumberField(),
            new Run(" of "),
            Run.NumPagesField("3"));
        doc.FinalSectionHeadersFooters.Footer = FieldHeaderFooter(
            new Run("FreeW visual evidence | "),
            Run.TitleField("Table Page Composition Stress"));

        doc.Blocks.Add(StyledParagraph("Table Page Composition Stress", "Heading1"));
        var intro = new Paragraph();
        intro.Runs.Add(new Run(
            "This bounded fixture combines repeated table headers, page border, watermark, " +
            "header/footer fields, explicit table layout metadata, a caption, and a footnote reference"));
        intro.Runs.Add(Run.FootnoteReference(1));
        intro.Runs.Add(new Run("."));
        doc.Blocks.Add(intro);
        doc.Footnotes[1] = new Footnote(
            1,
            "Footnote 1: Confirms the shared fixture carries note metadata alongside table pagination.");

        doc.Blocks.Add(BuildTablePageCompositionStressTable());
        doc.Blocks.Add(Captions.BuildCaption(
            CaptionLabel.Table,
            1,
            "Repeated-header table with page chrome, field headers, watermark, and explicit cell borders."));
        doc.Blocks.Add(new Paragraph(
            "Both renderers should emit three trusted rows for this shared scenario, and the normalizer " +
            "should reject missing or drifted table/page-composition metadata."));

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
            "hierarchy depth, connectors, Basic Pyramid polygon bands, color schemes, styles, and node fill sequences."));

        var chartParagraph = new Paragraph();
        chartParagraph.Runs.Add(new Run("Column chart with quick-layout annotations: "));
        chartParagraph.Runs.Add(Run.FromChart(BuildQuickLayoutColumnChart()));
        doc.Blocks.Add(chartParagraph);

        var scatterParagraph = new Paragraph();
        scatterParagraph.Runs.Add(new Run("Scatter chart must render marker-only geometry: "));
        scatterParagraph.Runs.Add(Run.FromChart(BuildMarkerOnlyScatterChart()));
        doc.Blocks.Add(scatterParagraph);

        var smartArtParagraph = new Paragraph();
        smartArtParagraph.Runs.Add(new Run("SmartArt hierarchy colors and style: "));
        smartArtParagraph.Runs.Add(Run.FromSmartArt(BuildStyledSmartArt()));
        doc.Blocks.Add(smartArtParagraph);

        var pyramidParagraph = new Paragraph();
        pyramidParagraph.Runs.Add(new Run("Basic Pyramid SmartArt polygon bands: "));
        pyramidParagraph.Runs.Add(Run.FromSmartArt(BuildPyramidSmartArt()));
        doc.Blocks.Add(pyramidParagraph);

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

    public static TextDocument BuildMultiSectionHeaderFooterImageDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Page.HeaderDistancePt = 24;
        doc.Page.FooterDistancePt = 24;
        doc.Blocks.Clear();

        doc.Blocks.Add(StyledParagraph("Section 1 Header Image", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The first page uses a left-aligned header image so the shared evidence planner can " +
            "record image bytes, size, slot, section, alignment, and alt text."));
        for (var i = 1; i <= 8; i++)
            doc.Blocks.Add(new Paragraph($"Section one body paragraph {i}: header image evidence remains stable."));

        var sectionBreak = new Section(
            new PageSettings
            {
                HeaderDistancePt = 24,
                FooterDistancePt = 24
            },
            SectionBreakKind.NextPage);
        sectionBreak.HeadersFooters.Header = ImageHeaderFooter(
            "Section One Letterhead",
            TextAlignment.Left,
            widthPt: 96,
            heightPt: 32);
        doc.Blocks.Add(new Paragraph("[ Next-page section break ]") { SectionBreak = sectionBreak });

        doc.FinalSectionHeadersFooters.Header = ImageHeaderFooter(
            "Section Two Letterhead",
            TextAlignment.Right,
            widthPt: 84,
            heightPt: 28);
        doc.Blocks.Add(StyledParagraph("Section 2 Header Image", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The second page uses a right-aligned header image to make cross-renderer visual evidence " +
            "catch stale section, alignment, or image metadata."));
        for (var i = 1; i <= 8; i++)
            doc.Blocks.Add(new Paragraph($"Section two body paragraph {i}: the final section header differs."));

        return doc;
    }

    public static TextDocument BuildFloatingWrapEvidenceDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(new Paragraph(
            "F2-01: Floating image wrap evidence. The square-wrapped image should sit at the left margin, " +
            "the tight-wrapped image should sit farther right, and body text should flow around both objects."));

        var square = new InlineImage(BuildGeneratedWatermarkPngBytes(), widthPt: 108, heightPt: 72)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 40,
            VerticalOffsetPt = 60,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Page,
            ZOrderIndex = 10,
            AltText = "F2 square wrapped visual evidence image"
        };
        var squareParagraph = new Paragraph();
        squareParagraph.Runs.Add(Run.FromImage(square));
        squareParagraph.Runs.Add(new Run(LoremWords(92)));
        doc.Blocks.Add(squareParagraph);

        var tight = new InlineImage(BuildGeneratedWatermarkPngBytes(), widthPt: 96, heightPt: 64)
        {
            Wrapping = ImageWrapping.Tight,
            HorizontalOffsetPt = 300,
            VerticalOffsetPt = 60,
            HorizontalAnchor = HorizontalAnchor.Margin,
            VerticalAnchor = VerticalAnchor.Page,
            ZOrderIndex = 11,
            AltText = "F2 tight wrapped visual evidence image"
        };
        var tightParagraph = new Paragraph();
        tightParagraph.Runs.Add(Run.FromImage(tight));
        tightParagraph.Runs.Add(new Run(LoremWords(92)));
        doc.Blocks.Add(tightParagraph);

        for (var i = 1; i <= 4; i++)
            doc.Blocks.Add(new Paragraph($"Floating wrap body paragraph {i}: {LoremWords(76)}"));

        return doc;
    }

    public static TextDocument BuildFloatingImageEvidenceDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();

        var inFront = new Paragraph();
        inFront.Runs.Add(new Run("Avalonia floating image evidence: in-front image."));
        inFront.Runs.Add(Run.FromImage(new InlineImage([1, 2, 3, 4], widthPt: 96, heightPt: 48)
        {
            Wrapping = ImageWrapping.InFront,
            HorizontalOffsetPt = 24,
            VerticalOffsetPt = 12,
            ZOrderIndex = 10
        }));
        document.Blocks.Add(inFront);

        var behind = new Paragraph();
        behind.Runs.Add(new Run("Avalonia floating image evidence: behind-text image."));
        behind.Runs.Add(Run.FromImage(new InlineImage([4, 3, 2, 1], widthPt: 120, heightPt: 54)
        {
            Wrapping = ImageWrapping.Behind,
            HorizontalOffsetPt = 36,
            VerticalOffsetPt = 8,
            ZOrderIndex = 1
        }));
        document.Blocks.Add(behind);

        var topBottom = new Paragraph();
        topBottom.Runs.Add(new Run("Avalonia floating image evidence: top-and-bottom page placement."));
        topBottom.Runs.Add(Run.FromImage(new InlineImage([9, 8, 7, 6], widthPt: 72, heightPt: 42)
        {
            Wrapping = ImageWrapping.TopAndBottom,
            HorizontalOffsetPt = 180,
            VerticalOffsetPt = 80,
            ZOrderIndex = 5
        }));
        document.Blocks.Add(topBottom);
        return document;
    }

    public static TextDocument BuildFloatingWrapDocument()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run("WPF floating wrap evidence: square image plus tight image."));
        paragraph.Runs.Add(Run.FromImage(new InlineImage([1, 2, 3, 4], widthPt: 96, heightPt: 48)
        {
            Wrapping = ImageWrapping.Square,
            HorizontalOffsetPt = 24,
            VerticalOffsetPt = 12,
            ZOrderIndex = 2
        }));
        paragraph.Runs.Add(Run.FromImage(new InlineImage([4, 3, 2, 1], widthPt: 84, heightPt: 42)
        {
            Wrapping = ImageWrapping.Tight,
            HorizontalOffsetPt = 160,
            VerticalOffsetPt = 20,
            ZOrderIndex = 3
        }));
        document.Blocks.Add(paragraph);
        return document;
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

    private static HeaderFooter ImageHeaderFooter(
        string altText,
        TextAlignment alignment,
        double widthPt,
        double heightPt)
    {
        var headerFooter = new HeaderFooter();
        var paragraph = new Paragraph
        {
            Formatting = ParagraphFormatting.Default with { Alignment = alignment }
        };
        paragraph.Runs.Add(Run.FromImage(new InlineImage(BuildGeneratedWatermarkPngBytes(), widthPt, heightPt)
        {
            AltText = altText,
            Wrapping = ImageWrapping.Inline
        }));
        headerFooter.Paragraphs.Add(paragraph);
        return headerFooter;
    }

    private static Table BuildReviewProofingTable()
    {
        var table = new Table
        {
            Formatting = new TableFormatting
            {
                Borders = true,
                HeaderRow = true,
                BandedRows = true
            },
            TableStyleId = "GridTable1Light",
            PreferredWidthPt = 468,
            Alignment = TableAlignment.Center,
            AutoFit = AutoFitMode.Fixed,
            DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 6, BottomPt: 3, RightPt: 6)
        };
        table.ColumnWidthsPt.AddRange([180, 288]);
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                HeaderCell("Location"),
                HeaderCell("Review proofing evidence")
            }
        });
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                Cell("Body"),
                Cell("Open and resolved comment threads include replies.")
            }
        });

        var tableParagraph = new Paragraph();
        tableParagraph.Runs.Add(new Run("Table anchor includes "));
        tableParagraph.Runs.Add(new Run("commented cell text") { CommentId = 20 });
        tableParagraph.Runs.Add(Run.CommentReference(20));
        tableParagraph.Runs.Add(new Run(" for shared renderers."));
        var tableCell = new TableCell();
        tableCell.Paragraphs.Add(tableParagraph);
        table.Rows.Add(new TableRow
        {
            Cells =
            {
                Cell("Table"),
                tableCell
            }
        });

        return table;
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

    private static Table BuildTablePageCompositionStressTable()
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
            TableStyleId = "GridTable1Light",
            PreferredWidthPt = 520,
            Alignment = TableAlignment.Center,
            AutoFit = AutoFitMode.Fixed,
            CellSpacingPt = 1.8,
            DefaultCellMargins = new TableCellMargins(TopPt: 3, LeftPt: 6, BottomPt: 3, RightPt: 6)
        };
        table.ColumnWidthsPt.AddRange([118, 126, 136, 140]);
        table.Rows.Add(new TableRow
        {
            HeightPt = 30,
            HeightRule = TableRowHeightRule.Exact,
            AllowBreakAcrossPages = false,
            Cells =
            {
                HeaderCell("Page area"),
                HeaderCell("Owner"),
                HeaderCell("Table evidence"),
                HeaderCell("Composition evidence")
            }
        });

        for (var row = 1; row <= 8; row++)
        {
            var shaded = row % 2 == 0 ? "#EAF2F8" : "#F8FBFD";
            table.Rows.Add(new TableRow
            {
                HeightPt = 58,
                HeightRule = TableRowHeightRule.Exact,
                AllowBreakAcrossPages = row is not 3 and not 6,
                Cells =
                {
                    Cell($"Segment {row}", shading: shaded, customBorder: true),
                    Cell(row is 3 or 6 ? "Keep row together" : "Flow row", customBorder: true),
                    Cell(
                        $"Repeated-header table row {row} keeps fixed widths, margins, spacing, and cell border metadata.",
                        shading: row == 5 ? "#FFF2CC" : null,
                        customBorder: true),
                    Cell(
                        row <= 2
                            ? "Page 1 should show header/footer fields, watermark, and border."
                            : row <= 6
                                ? "Page 2 should repeat the header row inside the same page chrome."
                                : "Page 3 should repeat the header row before the caption and closing text.",
                        customBorder: true,
                        verticalAlignment: row >= 7
                            ? TableCellVerticalAlignment.Center
                            : TableCellVerticalAlignment.Top)
                }
            });
        }

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
        var root = new SmartArtNode("Plan");
        var child = root.AddChild("Build");
        child.AddChild("Verify");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(root);
        smartArt.WidthPt = 320;
        smartArt.HeightPt = 140;
        smartArt.LayoutId = "orgchart1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "intense1";
        return smartArt;
    }

    private static SmartArt BuildPyramidSmartArt()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.WidthPt = 300;
        smartArt.HeightPt = 150;
        smartArt.LayoutId = "pyramid1";
        smartArt.ColorSchemeId = "accent2";
        smartArt.StyleId = "flat1";
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
            WidthPt = 260,
            HeightPt = 150,
            Placement = Placement(ImageWrapping.Square, xPt: 280, yPt: 260, zOrder: 10)
        };
        group.Children.Add(new InlineImage(BuildGeneratedWatermarkPngBytes(), widthPt: 42, heightPt: 30)
        {
            AltText = "Grouped image child"
        });
        group.ChildOffsets.Add((0, 0));
        group.Children.Add(new Shape(ShapeKind.Ellipse, 82, 50)
        {
            FillColorHex = "#CFE2F3",
            OutlineColorHex = "#1155CC",
            Effects = new ShapeEffectLst { HasGlow = true, GlowColorHex = "4472C4", GlowRad = 63500 }
        });
        group.ChildOffsets.Add((0, 40));
        var chart = Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2"],
            [2.0, 3.0],
            seriesName: "Grouped",
            title: "Grouped chart");
        chart.WidthPt = 108;
        chart.HeightPt = 72;
        chart.StyleId = 2;
        chart.ColorSchemeId = "colorful2";
        chart.QuickLayoutId = 5;
        chart.ShowLegend = true;
        group.Children.Add(chart);
        group.ChildOffsets.Add((94, 0));
        group.Children.Add(new WordArt("Group", WordArtStyle.GlowGold, 22));
        group.ChildOffsets.Add((94, 82));
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Check"]);
        smartArt.WidthPt = 128;
        smartArt.HeightPt = 46;
        smartArt.LayoutId = "process1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "moderate1";
        group.Children.Add(smartArt);
        group.ChildOffsets.Add((0, 98));
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

    private static TextDocument BuildReviewCompareOriginalDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Quarterly Policy Draft", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The policy keeps baseline controls, removes obsolete wording, and closes with the original approval sentence."));
        doc.Blocks.Add(new Paragraph(
            "A stable paragraph remains unchanged so the compare blackline has an anchor between edits."));
        doc.Blocks.Add(new Paragraph(
            "The rollout checklist references the old review channel."));
        return doc;
    }

    private static TextDocument BuildReviewCompareRevisedDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Quarterly Policy Draft", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The policy keeps baseline controls, adds reviewer wording, and closes with the updated approval sentence."));
        doc.Blocks.Add(new Paragraph(
            "A stable paragraph remains unchanged so the compare blackline has an anchor between edits."));
        doc.Blocks.Add(new Paragraph(
            "The rollout checklist references the current review channel plus a final compliance note."));
        return doc;
    }

    private static TextDocument BuildReviewCombineOriginalDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Combined Reviewer Draft", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The project brief keeps the launch scope stable and routes approvals through the baseline team."));
        doc.Blocks.Add(new Paragraph(
            "The implementation note remains unchanged for both reviewers."));
        doc.Blocks.Add(new Paragraph(
            "The closing paragraph asks operations to review the final package."));
        return doc;
    }

    private static TextDocument BuildReviewCombineReviewerADocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Combined Reviewer Draft", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The project brief keeps the launch scope expanded and routes approvals through Alice's review board."));
        doc.Blocks.Add(new Paragraph(
            "The implementation note remains unchanged for both reviewers."));
        doc.Blocks.Add(new Paragraph(
            "The closing paragraph asks operations to review the final package."));
        return doc;
    }

    private static TextDocument BuildReviewCombineReviewerBDocument()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        doc.Blocks.Add(StyledParagraph("Combined Reviewer Draft", "Heading1"));
        doc.Blocks.Add(new Paragraph(
            "The project brief keeps the launch scope stable and routes approvals through the baseline team."));
        doc.Blocks.Add(new Paragraph(
            "The implementation note remains unchanged for both reviewers and adds Bob's audit reminder."));
        doc.Blocks.Add(new Paragraph(
            "The closing paragraph asks operations to publish the final package with release evidence."));
        return doc;
    }

    private static void AddReviewRetainedModelSafety(TextDocument doc, string operation)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace cp = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vt = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        doc.Preserved.OriginalSettings = new XElement(
            w + "settings",
            new XAttribute(XNamespace.Xmlns + "w", w.NamespaceName),
            new XElement(
                w + "proofState",
                new XAttribute(w + "spelling", "clean"),
                new XAttribute(w + "grammar", "clean")),
            new XElement(
                w + "compat",
                new XElement(w + "compatSetting",
                    new XAttribute(w + "name", "freewReviewSafety"),
                    new XAttribute(w + "uri", "urn:freew:visual-evidence"),
                    new XAttribute(w + "val", operation))));

        doc.Preserved.OriginalCustomProperties = new XElement(
            cp + "Properties",
            new XAttribute(XNamespace.Xmlns + "cp", cp.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "vt", vt.NamespaceName),
            new XElement(
                cp + "property",
                new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                new XAttribute("pid", "2"),
                new XAttribute("name", "FreeWReviewSafety"),
                new XElement(vt + "lpwstr", operation + "-retained-model-safety")));

        doc.Preserved.Parts.Add(new PreservedPart(
            "/customXml/freew-review-safety.xml",
            Encoding.UTF8.GetBytes("<freew-review-safety operation=\"" + operation + "\" retained=\"true\" />"),
            "application/xml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml"));
    }

    private static Paragraph EquationParagraph(string prefix, Equation equation)
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(prefix));
        paragraph.Runs.Add(Run.FromEquation(equation));
        return paragraph;
    }

    private static string LoremWords(int words)
    {
        var source = new[]
        {
            "Lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit",
            "sed", "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et",
            "dolore", "magna", "aliqua", "Ut", "enim", "ad", "minim", "veniam",
            "quis", "nostrud", "exercitation", "ullamco", "laboris", "nisi", "ut",
            "aliquip", "ex", "ea", "commodo", "consequat"
        };
        return string.Join(" ", Enumerable.Range(0, Math.Max(1, words)).Select(i => source[i % source.Length]));
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
        Cell(text, gridSpan: gridSpan, customBorder: true);

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
