// Generate-F2FlowCorpus.cs
// Standalone corpus generator for the f2-flow visual-verification pass.
// Produces .docx files that exercise: headers/footers (default, first-page, odd/even),
// footnotes, endnotes, section-break page-size change, tracked insertions/deletions, comments.
//
// Compiled via FreeW.FidelityRender project (add as file reference or compile separately).
// Usage: dotnet-script or compile as part of FreeW.FidelityRender and call GenerateF2FlowCorpus().

using FreeW.Core.IO;
using FreeW.Core.Model;

public static class F2FlowCorpusGenerator
{
    static Paragraph MP(string text, string? styleId = null)
    {
        var p = new Paragraph(text);
        if (styleId is not null)
            p.Formatting = p.Formatting with { StyleId = styleId };
        return p;
    }

    public static void Generate(string outDir)
    {
        Directory.CreateDirectory(outDir);

        // ─── 1. Header + Footer basic (default, repeating across 3 pages) ─────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("My Document Header — Page [PAGE]");
            doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("Footer text — [PAGE] of [NUMPAGES]");
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Header/Footer Basic Test", "Heading1"));
            doc.Blocks.Add(MP("This document should have a header at the top and a footer at the bottom of every page. The header says \"My Document Header\" and the footer contains page numbers."));
            for (int i = 1; i <= 40; i++)
                doc.Blocks.Add(MP($"Body paragraph {i}: Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua."));
            DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-basic.docx"));
            Console.WriteLine("  wrote f2-hf-basic.docx");
        }

        // ─── 2. Different first-page header ──────────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Page.DifferentFirstPage = true;
            doc.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter("=== FIRST PAGE HEADER (COVER) ===");
            doc.FinalSectionHeadersFooters.FirstFooter = new HeaderFooter("=== FIRST PAGE FOOTER ===");
            doc.FinalSectionHeadersFooters.Header = new HeaderFooter("=== SUBSEQUENT PAGE HEADER ===");
            doc.FinalSectionHeadersFooters.Footer = new HeaderFooter("=== SUBSEQUENT PAGE FOOTER ===");
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Cover Page", "Title"));
            doc.Blocks.Add(MP("This is the first page. It should have the first-page-specific header and footer."));
            for (int i = 1; i <= 40; i++)
                doc.Blocks.Add(MP($"Content paragraph {i}: On pages 2+, the subsequent header/footer should be visible."));
            DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-firstpage.docx"));
            Console.WriteLine("  wrote f2-hf-firstpage.docx");
        }

        // ─── 3. Odd/even (mirror) headers ────────────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Page.DifferentOddEvenPages = true;
            doc.FinalSectionHeadersFooters.Header     = new HeaderFooter("=== ODD PAGE HEADER ===");
            doc.FinalSectionHeadersFooters.EvenHeader = new HeaderFooter("=== EVEN PAGE HEADER ===");
            doc.FinalSectionHeadersFooters.Footer     = new HeaderFooter("=== ODD PAGE FOOTER ===");
            doc.FinalSectionHeadersFooters.EvenFooter = new HeaderFooter("=== EVEN PAGE FOOTER ===");
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Odd/Even Headers Demo", "Heading1"));
            doc.Blocks.Add(MP("Page 1 (odd) should show ODD PAGE HEADER. Page 2 (even) should show EVEN PAGE HEADER."));
            for (int i = 1; i <= 40; i++)
                doc.Blocks.Add(MP($"Paragraph {i}: Alternate header content expected on each page."));
            DocxWriter.Write(doc, Path.Combine(outDir, "f2-hf-oddeven.docx"));
            Console.WriteLine("  wrote f2-hf-oddeven.docx");
        }

        // ─── 4. Footnotes ────────────────────────────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Footnotes Test", "Heading1"));

            // Paragraph with first footnote reference
            var p1 = MP("This sentence has a footnote at the end.");
            var fn1 = new Run(string.Empty) { FootnoteId = 1 };
            p1.Runs.Add(fn1);
            p1.Runs.Add(new Run(" The footnote should appear at the bottom of this page."));
            doc.Blocks.Add(p1);
            doc.Footnotes[1] = new Footnote(1, "First footnote: This is footnote content that should appear at the bottom of page 1.");

            // Add filler to push to page 2
            for (int i = 1; i <= 20; i++)
                doc.Blocks.Add(MP($"Filler paragraph {i}: Lorem ipsum dolor sit amet consectetur adipiscing elit."));

            // Paragraph with second footnote on page 2
            var p2 = MP("This sentence on page 2 also has a footnote.");
            var fn2 = new Run(string.Empty) { FootnoteId = 2 };
            p2.Runs.Add(fn2);
            p2.Runs.Add(new Run(" The second footnote should appear at the bottom of page 2."));
            doc.Blocks.Add(p2);
            doc.Footnotes[2] = new Footnote(2, "Second footnote: This content should appear at the bottom of page 2.");

            for (int i = 1; i <= 15; i++)
                doc.Blocks.Add(MP($"More content {i}: Additional paragraphs to ensure the footnotes land on page 2."));

            DocxWriter.Write(doc, Path.Combine(outDir, "f2-footnotes.docx"));
            Console.WriteLine("  wrote f2-footnotes.docx");
        }

        // ─── 5. Endnotes ─────────────────────────────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Endnotes Test", "Heading1"));

            var p1 = MP("This is the first sentence with an endnote reference.");
            p1.Runs.Add(new Run(string.Empty) { EndnoteId = 1 });
            p1.Runs.Add(new Run(" Endnotes collect at the document end."));
            doc.Blocks.Add(p1);
            doc.Endnotes[1] = new Endnote(1, "First endnote: This content should appear at the very end of the document.");

            for (int i = 1; i <= 20; i++)
                doc.Blocks.Add(MP($"Body paragraph {i}: Lorem ipsum dolor sit amet consectetur."));

            var p2 = MP("This sentence on page 2 has a second endnote.");
            p2.Runs.Add(new Run(string.Empty) { EndnoteId = 2 });
            doc.Blocks.Add(p2);
            doc.Endnotes[2] = new Endnote(2, "Second endnote: This content should also appear at the very end of the document.");

            for (int i = 1; i <= 15; i++)
                doc.Blocks.Add(MP($"More body content {i}: Additional text."));

            DocxWriter.Write(doc, Path.Combine(outDir, "f2-endnotes.docx"));
            Console.WriteLine("  wrote f2-endnotes.docx");
        }

        // ─── 6. Section break with page-size change (portrait → landscape) ───────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            // Default page: portrait 8.5x11
            doc.Page.WidthPt  = 612;  // 8.5in
            doc.Page.HeightPt = 792;  // 11in
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Section 1: Portrait (8.5x11)", "Heading1"));
            doc.Blocks.Add(MP("This section is in portrait orientation (8.5 x 11 inches). The text flows in a standard portrait layout. A next-page section break follows this paragraph, switching to landscape."));
            for (int i = 1; i <= 5; i++)
                doc.Blocks.Add(MP($"Portrait paragraph {i}: The page is taller than it is wide."));

            // Marker paragraph carries section break to landscape
            var sectionMarker = MP("[ End of Portrait Section — Landscape Follows ]");
            var landscapePage = new PageSettings
            {
                WidthPt  = 792,  // 11in
                HeightPt = 612,  // 8.5in (landscape = swapped)
                Landscape = true,
                MarginLeftPt   = 72,
                MarginRightPt  = 72,
                MarginTopPt    = 72,
                MarginBottomPt = 72,
            };
            sectionMarker.SectionBreak = new Section(landscapePage, SectionBreakKind.NextPage);
            doc.Blocks.Add(sectionMarker);

            // Section 2: landscape
            doc.Blocks.Add(MP("Section 2: Landscape (11x8.5)", "Heading1"));
            doc.Blocks.Add(MP("This section should be in landscape orientation (11 x 8.5 inches). The page is now wider than it is tall. If the section break is honoured, this text should appear on a new page with a wider line length."));
            for (int i = 1; i <= 5; i++)
                doc.Blocks.Add(MP($"Landscape paragraph {i}: The page should now be wider than it is tall."));

            DocxWriter.Write(doc, Path.Combine(outDir, "f2-section-landscape.docx"));
            Console.WriteLine("  wrote f2-section-landscape.docx");
        }

        // ─── 7. Tracked insertions and deletions ─────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Tracked Changes Test", "Heading1"));

            // Paragraph mixing normal text, insertion, and deletion
            var p1 = new Paragraph();
            p1.Runs.Add(new Run("This is the original text. "));
            p1.Runs.Add(new Run("This text was INSERTED by Alice.")
            {
                Revision = RevisionKind.Inserted,
                RevisionAuthor = "Alice",
                RevisionDateXml = "2026-06-25T10:00:00Z"
            });
            p1.Runs.Add(new Run(" Normal text continues here. "));
            p1.Runs.Add(new Run("This phrase was DELETED by Bob.")
            {
                Revision = RevisionKind.Deleted,
                RevisionAuthor = "Bob",
                RevisionDateXml = "2026-06-25T11:00:00Z"
            });
            p1.Runs.Add(new Run(" End of paragraph."));
            doc.Blocks.Add(p1);

            doc.Blocks.Add(MP("Expected: insertions should be underlined; deletions should be struck-through. Both in a revision colour. Comments pending acceptance/rejection."));

            // Second paragraph — whole-paragraph insertion
            var p2 = new Paragraph();
            p2.Runs.Add(new Run("This entire paragraph is a tracked insertion by Carol.")
            {
                Revision = RevisionKind.Inserted,
                RevisionAuthor = "Carol",
                RevisionDateXml = "2026-06-25T12:00:00Z"
            });
            doc.Blocks.Add(p2);

            // Third paragraph — multiple authors
            var p3 = new Paragraph();
            p3.Runs.Add(new Run("Alice added this. ") { Revision = RevisionKind.Inserted, RevisionAuthor = "Alice", RevisionDateXml = "2026-06-25T10:00:00Z" });
            p3.Runs.Add(new Run("Bob deleted this. ") { Revision = RevisionKind.Deleted, RevisionAuthor = "Bob", RevisionDateXml = "2026-06-25T11:00:00Z" });
            p3.Runs.Add(new Run("Carol added this too.") { Revision = RevisionKind.Inserted, RevisionAuthor = "Carol", RevisionDateXml = "2026-06-25T12:00:00Z" });
            doc.Blocks.Add(p3);

            for (int i = 1; i <= 30; i++)
                doc.Blocks.Add(MP($"Normal body paragraph {i}: No tracked changes here."));

            DocxWriter.Write(doc, Path.Combine(outDir, "f2-tracked-changes.docx"));
            Console.WriteLine("  wrote f2-tracked-changes.docx");
        }

        // ─── 8. Comments (anchored) ───────────────────────────────────────────────────────────────
        {
            var doc = TextDocument.CreateEmpty();
            doc.Blocks.Clear();
            doc.Blocks.Add(MP("Comments Test", "Heading1"));

            // Paragraph with a comment anchor
            var p1 = new Paragraph();
            p1.Runs.Add(new Run("This is the text before the commented span. "));
            // Comment anchor: range is marked with CommentId on covered runs
            var anchor1Start = Run.CommentReference(1);  // anchor start marker
            p1.Runs.Add(anchor1Start);
            p1.Runs.Add(new Run("The commented text appears here.") { CommentId = 1 });
            p1.Runs.Add(new Run(" Text after the comment continues normally."));
            doc.Blocks.Add(p1);

            // Register comment in document
            doc.Comments[1] = new Comment(1, "This is the comment text by Alice. It should appear as a balloon or sidebar.")
            {
                Author   = "Alice",
                Initials = "A",
                DateXml  = "2026-06-25T10:00:00Z"
            };

            doc.Blocks.Add(MP("The comment anchor above should be highlighted. The comment content ('This is the comment text by Alice') should appear somewhere—either a balloon in the right margin or a reviewing pane entry."));

            // Second comment
            var p2 = new Paragraph();
            p2.Runs.Add(new Run("Another comment follows this text: "));
            var anchor2 = Run.CommentReference(2);
            p2.Runs.Add(anchor2);
            p2.Runs.Add(new Run("second commented phrase.") { CommentId = 2 });
            p2.Runs.Add(new Run(" End of paragraph with second comment."));
            doc.Blocks.Add(p2);

            doc.Comments[2] = new Comment(2, "Second comment by Bob. Distinct from Alice's comment.")
            {
                Author   = "Bob",
                Initials = "B",
                DateXml  = "2026-06-25T11:00:00Z"
            };

            for (int i = 1; i <= 25; i++)
                doc.Blocks.Add(MP($"Normal paragraph {i}: No comments here."));

            DocxWriter.Write(doc, Path.Combine(outDir, "f2-comments.docx"));
            Console.WriteLine("  wrote f2-comments.docx");
        }

        Console.WriteLine($"\nDone — 8 corpus files written to {outDir}");
    }
}
