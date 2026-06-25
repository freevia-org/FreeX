// FreeW.CorpusReview — generates 12 .docx corpus files for the review/refs/headers fidelity triage.
// Usage: FreeW.CorpusReview <outputDir>
//   Produces corpus files covering: header+footer, first-page header, odd/even headers, footnotes,
//   endnotes, table-of-contents, citation+bibliography, cross-reference, tracked insertions+deletions,
//   anchored comment, tracked changes (multi-revision), multi-page repeating headers.

using FreeW.Core.IO;
using FreeW.Core.Model;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: FreeW.CorpusReview <outputDir>");
    return 2;
}
string outDir = args[0];
Directory.CreateDirectory(outDir);

// Helper: write a document
void Write(string name, TextDocument doc)
{
    string path = Path.Combine(outDir, name);
    DocxWriter.Write(doc, path);
    Console.WriteLine($"  wrote {name}");
}

// Helper: add filler paragraphs to force multi-page layout
static void AddFillerParagraphs(TextDocument doc, int count, string prefix = "Body text line")
{
    for (int i = 1; i <= count; i++)
        doc.Blocks.Add(new Paragraph($"{prefix} {i}. " +
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
            "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat."));
}

Console.WriteLine("[1/12] header-footer-basic.docx — header+footer with page number across 2+ pages");
{
    var doc = TextDocument.CreateEmpty();
    // Header: "My Report" + page number
    var hdrPara = new Paragraph();
    hdrPara.Runs.Add(new Run("My Report — Page "));
    hdrPara.Runs.Add(Run.PageNumberField());
    doc.Header = new HeaderFooter { Paragraphs = { hdrPara } };
    // Footer: centred "Confidential"
    doc.Footer = new HeaderFooter("Confidential — Do Not Distribute");
    doc.Blocks.Add(new Paragraph("Introduction") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 30);
    Write("header-footer-basic.docx", doc);
}

Console.WriteLine("[2/12] header-firstpage.docx — different first-page header");
{
    var doc = TextDocument.CreateEmpty();
    doc.Page.DifferentFirstPage = true;
    doc.FirstHeader = new HeaderFooter("COVER PAGE — Title Document (first-page header only)");
    doc.FirstFooter = new HeaderFooter("(no footer on cover page)");
    var hdrPara = new Paragraph();
    hdrPara.Runs.Add(new Run("Subsequent Pages — "));
    hdrPara.Runs.Add(Run.PageNumberField());
    doc.Header = new HeaderFooter { Paragraphs = { hdrPara } };
    doc.Footer = new HeaderFooter("Standard Footer");
    doc.Blocks.Add(new Paragraph("Cover Page") { StyleId = "Title" });
    AddFillerParagraphs(doc, 30, "Cover page filler line");
    Write("header-firstpage.docx", doc);
}

Console.WriteLine("[3/12] header-odd-even.docx — odd/even (mirror) headers");
{
    var doc = TextDocument.CreateEmpty();
    doc.Page.DifferentOddEvenPages = true;
    var oddPara = new Paragraph();
    oddPara.Runs.Add(new Run("ODD PAGE — "));
    oddPara.Runs.Add(Run.PageNumberField());
    doc.Header = new HeaderFooter { Paragraphs = { oddPara } };  // odd (default)
    doc.Footer = new HeaderFooter("Odd Footer");
    var evenPara = new Paragraph();
    evenPara.Runs.Add(new Run("EVEN PAGE — "));
    evenPara.Runs.Add(Run.PageNumberField());
    doc.EvenHeader = new HeaderFooter { Paragraphs = { evenPara } };
    doc.EvenFooter = new HeaderFooter("Even Footer");
    doc.Blocks.Add(new Paragraph("Odd/Even Headers Demo") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 35, "Mirror header filler");
    Write("header-odd-even.docx", doc);
}

Console.WriteLine("[4/12] footnotes.docx — multiple footnotes");
{
    var doc = TextDocument.CreateEmpty();
    doc.Header = new HeaderFooter("Footnote Demo — Page " + "X");
    doc.Blocks.Add(new Paragraph("Footnotes Test") { StyleId = "Heading1" });
    // Paragraph 1 with footnote ref
    var p1 = new Paragraph();
    p1.Runs.Add(new Run("This sentence has a footnote at the end"));
    p1.Runs.Add(Run.FootnoteReference(1));
    p1.Runs.Add(new Run("."));
    doc.Blocks.Add(p1);
    doc.Footnotes[1] = new Footnote(1, "First footnote: this appears at the bottom of the page where the reference mark appears.");

    var p2 = new Paragraph();
    p2.Runs.Add(new Run("A second sentence with another footnote reference"));
    p2.Runs.Add(Run.FootnoteReference(2));
    p2.Runs.Add(new Run(" mid-paragraph."));
    doc.Blocks.Add(p2);
    doc.Footnotes[2] = new Footnote(2, "Second footnote: rendered at page bottom separated by a short rule line from body text.");

    var p3 = new Paragraph();
    p3.Runs.Add(new Run("Third reference appears on the next page after filler text"));
    p3.Runs.Add(Run.FootnoteReference(3));
    p3.Runs.Add(new Run("."));
    doc.Blocks.Add(p3);
    doc.Footnotes[3] = new Footnote(3, "Third footnote: should appear at the bottom of page 2.");
    AddFillerParagraphs(doc, 25);
    Write("footnotes.docx", doc);
}

Console.WriteLine("[5/12] endnotes.docx — endnotes at document end");
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(new Paragraph("Endnotes Test") { StyleId = "Heading1" });
    var p1 = new Paragraph();
    p1.Runs.Add(new Run("This claim requires an endnote citation"));
    p1.Runs.Add(Run.EndnoteReference(1));
    p1.Runs.Add(new Run("."));
    doc.Blocks.Add(p1);
    doc.Endnotes[1] = new Endnote(1, "Endnote 1: Smith, J. (2020). Research Foundations. Academic Press, pp. 45-67.");

    var p2 = new Paragraph();
    p2.Runs.Add(new Run("Another assertion also needing an endnote"));
    p2.Runs.Add(Run.EndnoteReference(2));
    p2.Runs.Add(new Run(" for supporting evidence."));
    doc.Blocks.Add(p2);
    doc.Endnotes[2] = new Endnote(2, "Endnote 2: Jones, A. & Brown, B. (2022). Data-Driven Decisions. Tech Review, 15(3), 12-18.");
    AddFillerParagraphs(doc, 20);
    Write("endnotes.docx", doc);
}

Console.WriteLine("[6/12] table-of-contents.docx — TOC with heading entries");
{
    var doc = TextDocument.CreateEmpty();
    doc.Header = new HeaderFooter("Document with Table of Contents");
    TableOfContents.EnsureStyles(doc);

    doc.Blocks.Add(new Paragraph("Table of Contents Demo") { StyleId = "Title" });
    // Add TOC paragraphs (built from the headings that follow — pre-populate with cached text)
    doc.Blocks.Add(new Paragraph("Contents") { StyleId = TableOfContents.HeadingStyleId });
    doc.Blocks.Add(new Paragraph("Introduction\t1") { StyleId = TableOfContents.EntryStyleId(1) });
    doc.Blocks.Add(new Paragraph("Background\t2") { StyleId = TableOfContents.EntryStyleId(1) });
    doc.Blocks.Add(new Paragraph("Methodology\t3") { StyleId = TableOfContents.EntryStyleId(2) });
    doc.Blocks.Add(new Paragraph("Results\t4") { StyleId = TableOfContents.EntryStyleId(1) });
    doc.Blocks.Add(new Paragraph("Conclusion\t5") { StyleId = TableOfContents.EntryStyleId(1) });
    doc.Blocks.Add(new Paragraph(string.Empty));  // spacer

    doc.Blocks.Add(new Paragraph("Introduction") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 10, "Introduction body");
    doc.Blocks.Add(new Paragraph("Background") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 8);
    doc.Blocks.Add(new Paragraph("Methodology") { StyleId = "Heading2" });
    AddFillerParagraphs(doc, 8);
    doc.Blocks.Add(new Paragraph("Results") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 8);
    doc.Blocks.Add(new Paragraph("Conclusion") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 5);
    Write("table-of-contents.docx", doc);
}

Console.WriteLine("[7/12] citation-bibliography.docx — citation + bibliography");
{
    var source1 = new Source
    {
        Tag = "Smith2020",
        Type = SourceType.Book,
        Author = "Smith, John",
        Title = "Foundations of Research Methods",
        Year = "2020",
        Publisher = "Academic Press"
    };
    var source2 = new Source
    {
        Tag = "Jones2022",
        Type = SourceType.JournalArticle,
        Author = "Jones, Alice",
        Title = "Modern Data Analysis Techniques",
        Year = "2022",
        Journal = "Data Science Review",
        Volume = "15",
        Issue = "3",
        Pages = "12-28"
    };

    var doc = TextDocument.CreateEmpty();
    doc.Sources.Add(source1);
    doc.Sources.Add(source2);
    Citations.EnsureStyles(doc);

    doc.Blocks.Add(new Paragraph("Citation and Bibliography Demo") { StyleId = "Heading1" });
    var p1 = new Paragraph();
    p1.Runs.Add(new Run("Research methodology is well-established "));
    p1.Runs.Add(new Run(Citations.FormatInText(source1)) { Formatting = RunFormatting.Default });
    p1.Runs.Add(new Run(" as demonstrated in multiple studies."));
    doc.Blocks.Add(p1);

    var p2 = new Paragraph();
    p2.Runs.Add(new Run("More recent work has advanced these methods "));
    p2.Runs.Add(new Run(Citations.FormatInText(source2)) { Formatting = RunFormatting.Default });
    p2.Runs.Add(new Run("."));
    doc.Blocks.Add(p2);

    AddFillerParagraphs(doc, 5);

    // Bibliography section
    foreach (var bibPara in Citations.BuildBibliography(doc))
        doc.Blocks.Add(bibPara);

    Write("citation-bibliography.docx", doc);
}

Console.WriteLine("[8/12] cross-reference.docx — cross-reference field");
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(new Paragraph("Cross-Reference Demo") { StyleId = "Heading1" });

    // Target heading (bookmarked)
    var targetPara = new Paragraph("Results Section") { StyleId = "Heading2" };
    targetPara.BookmarkName = "_RefResults";
    doc.Blocks.Add(targetPara);
    AddFillerParagraphs(doc, 5, "Results content");

    // Paragraph with a cross-reference back to that heading
    var refField = new CrossReferenceField(CrossRefFieldKind.Ref, "_RefResults", CrossRefInsertAs.Text, Hyperlink: true);
    var xrefPara = new Paragraph();
    xrefPara.Runs.Add(new Run("As shown in "));
    xrefPara.Runs.Add(Run.CrossReferenceFieldRun(refField, "Results Section"));
    xrefPara.Runs.Add(new Run(" above, the data supports the hypothesis."));
    doc.Blocks.Add(xrefPara);

    // Page reference cross-reference
    var pageRefField = new CrossReferenceField(CrossRefFieldKind.PageRef, "_RefResults", CrossRefInsertAs.PageNumber, Hyperlink: false);
    var pageRefPara = new Paragraph();
    pageRefPara.Runs.Add(new Run("See page "));
    pageRefPara.Runs.Add(Run.CrossReferenceFieldRun(pageRefField, "1"));
    pageRefPara.Runs.Add(new Run(" for results."));
    doc.Blocks.Add(pageRefPara);

    AddFillerParagraphs(doc, 5);
    Write("cross-reference.docx", doc);
}

Console.WriteLine("[9/12] tracked-changes-inline.docx — tracked insertions + deletions shown inline");
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(new Paragraph("Tracked Changes — All Markup View") { StyleId = "Heading1" });

    // Paragraph with a tracked insertion
    var p1 = new Paragraph();
    p1.Runs.Add(new Run("The original sentence contains "));
    p1.Runs.Add(new Run("important ")
    {
        Revision = RevisionKind.Inserted,
        RevisionAuthor = "Alice Editor",
        RevisionDateXml = "2026-06-20T10:00:00Z"
    });
    p1.Runs.Add(new Run("key information that should be preserved."));
    doc.Blocks.Add(p1);

    // Paragraph with a tracked deletion
    var p2 = new Paragraph();
    p2.Runs.Add(new Run("This sentence is perfectly fine as written. "));
    p2.Runs.Add(new Run("This redundant clause is being removed by the reviewer. ")
    {
        Revision = RevisionKind.Deleted,
        RevisionAuthor = "Bob Reviewer",
        RevisionDateXml = "2026-06-21T14:30:00Z"
    });
    p2.Runs.Add(new Run("The remaining text continues normally."));
    doc.Blocks.Add(p2);

    // Paragraph with both an insertion and deletion
    var p3 = new Paragraph();
    p3.Runs.Add(new Run("The "));
    p3.Runs.Add(new Run("old ")
    {
        Revision = RevisionKind.Deleted,
        RevisionAuthor = "Alice Editor",
        RevisionDateXml = "2026-06-22T09:00:00Z"
    });
    p3.Runs.Add(new Run("new ")
    {
        Revision = RevisionKind.Inserted,
        RevisionAuthor = "Alice Editor",
        RevisionDateXml = "2026-06-22T09:00:00Z"
    });
    p3.Runs.Add(new Run("approach is better suited to the task."));
    doc.Blocks.Add(p3);

    AddFillerParagraphs(doc, 5);
    Write("tracked-changes-inline.docx", doc);
}

Console.WriteLine("[10/12] comment-anchored.docx — anchored comment");
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(new Paragraph("Comments Demo") { StyleId = "Heading1" });

    // Add a comment
    var comment1 = new Comment(1, "This paragraph needs clarification. The argument is not fully developed.", "Carol Reviewer", "CR");
    doc.Comments[1] = comment1;

    // Paragraph with comment reference
    var p1 = new Paragraph();
    p1.Runs.Add(new Run("The central argument of this paper") { CommentId = 1 });
    p1.Runs.Add(Run.CommentReference(1));
    p1.Runs.Add(new Run(" relies on several key assumptions."));
    doc.Blocks.Add(p1);

    // Second comment
    var comment2 = new Comment(2, "Consider adding a citation here for better academic credibility.", "Dan Editor", "DE");
    doc.Comments[2] = comment2;

    var p2 = new Paragraph();
    p2.Runs.Add(new Run("This claim is supported by extensive prior research") { CommentId = 2 });
    p2.Runs.Add(Run.CommentReference(2));
    p2.Runs.Add(new Run(" in the field."));
    doc.Blocks.Add(p2);

    AddFillerParagraphs(doc, 8);
    Write("comment-anchored.docx", doc);
}

Console.WriteLine("[11/12] tracked-changes-with-comments.docx — tracked changes + comment balloons");
{
    var doc = TextDocument.CreateEmpty();
    doc.Blocks.Add(new Paragraph("Review Markup — Tracked Changes and Comments") { StyleId = "Heading1" });

    // Comment alongside tracked insertion
    var comment1 = new Comment(1, "Excellent addition! This phrase strengthens the argument.", "Editor", "ED");
    doc.Comments[1] = comment1;

    var p1 = new Paragraph();
    p1.Runs.Add(new Run("The methodology section now includes "));
    p1.Runs.Add(new Run("a comprehensive statistical analysis framework ")
    {
        Revision = RevisionKind.Inserted,
        RevisionAuthor = "Lead Author",
        RevisionDateXml = "2026-06-23T08:00:00Z",
        CommentId = 1
    });
    p1.Runs.Add(Run.CommentReference(1));
    p1.Runs.Add(new Run(" that validates the approach."));
    doc.Blocks.Add(p1);

    // Deletion with a comment noting the reason
    var comment2 = new Comment(2, "Removed per reviewer feedback — too detailed for the abstract.", "Lead Author", "LA");
    doc.Comments[2] = comment2;

    var p2 = new Paragraph();
    p2.Runs.Add(new Run("Abstract text: "));
    p2.Runs.Add(new Run("detailed implementation specifics and technical appendices included herein ")
    {
        Revision = RevisionKind.Deleted,
        RevisionAuthor = "Lead Author",
        RevisionDateXml = "2026-06-23T08:30:00Z",
        CommentId = 2
    });
    p2.Runs.Add(Run.CommentReference(2));
    p2.Runs.Add(new Run(" are referenced in the appendix."));
    doc.Blocks.Add(p2);

    // Format revision
    var p3 = new Paragraph();
    p3.Runs.Add(new Run("This heading was reformatted by the editor."));
    doc.Blocks.Add(p3);

    AddFillerParagraphs(doc, 10);
    Write("tracked-changes-with-comments.docx", doc);
}

Console.WriteLine("[12/12] multipage-headers-repeating.docx — multi-page repeating headers/footers");
{
    var doc = TextDocument.CreateEmpty();
    // Section 1: heading + 40 body paragraphs, then section break on a terminating paragraph
    doc.Blocks.Add(new Paragraph("Section 1 Opening") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 38, "Section 1 filler");
    // The section-break marker paragraph ends section 1 and carries its page settings + headers/footers
    var section1 = new Section(new PageSettings(), SectionBreakKind.NextPage);
    section1.HeadersFooters.Header = new HeaderFooter("SECTION 1 HEADER — This should appear on every page of section 1");
    section1.HeadersFooters.Footer = new HeaderFooter("Section 1 Footer — repeats on each page");
    var s1TermPara = new Paragraph("(end of section 1)") { SectionBreak = section1 };
    doc.Blocks.Add(s1TermPara);

    // Section 2 (final section): page-number header
    doc.Blocks.Add(new Paragraph("Section 2 Opening") { StyleId = "Heading1" });
    AddFillerParagraphs(doc, 38, "Section 2 filler");
    var hdr2Para = new Paragraph();
    hdr2Para.Runs.Add(new Run("SECTION 2 HEADER — Page "));
    hdr2Para.Runs.Add(Run.PageNumberField());
    hdr2Para.Runs.Add(new Run(" of "));
    hdr2Para.Runs.Add(Run.NumPagesField());
    doc.Header = new HeaderFooter { Paragraphs = { hdr2Para } };
    doc.Footer = new HeaderFooter("Section 2 Footer — repeated on each page of section 2");

    Write("multipage-headers-repeating.docx", doc);
}

Console.WriteLine($"Done — 12 corpus files written to {outDir}");
return 0;
