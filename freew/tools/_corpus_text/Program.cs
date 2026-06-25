// Corpus generator for FreeW text/layout visual-fidelity triage.
// Writes 11 .docx files to freew-fidelity-corpus/files/text/ covering:
//   01-heading-styles, 02-char-formatting, 03-lists, 04-para-alignment,
//   05-line-spacing, 06-indents, 07-tab-stops, 08-drop-cap, 09-multicolumn,
//   10-section-break-pagesetup, 11-page-border-watermark
using System.IO;
using FreeW.Core.IO;
using FreeW.Core.Model;

var outDir = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory,
        "../../../../../../freew-fidelity-corpus/files/text"));
Directory.CreateDirectory(outDir);
Console.WriteLine($"Writing corpus to: {outDir}");

// ─── 01  Heading styles H1–H3 + Title + Normal ────────────────────────────────
{
    var doc = new TextDocument();

    var title = new Paragraph("FreeW Text Fidelity: Heading & Paragraph Styles");
    title.StyleId = "Title";
    doc.Blocks.Add(title);

    var h1 = new Paragraph("1. Heading One — H1 Style");
    h1.StyleId = "Heading1";
    doc.Blocks.Add(h1);

    doc.Blocks.Add(new Paragraph(
        "This is Normal paragraph text below a Heading 1. The quick brown fox jumps over the lazy dog. " +
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit."));

    var h2 = new Paragraph("1.1 Heading Two — H2 Style");
    h2.StyleId = "Heading2";
    doc.Blocks.Add(h2);

    doc.Blocks.Add(new Paragraph(
        "Paragraph below H2. Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium " +
        "doloremque laudantium totam rem aperiam eaque ipsa quae ab illo inventore veritatis."));

    var h3 = new Paragraph("1.1.1 Heading Three — H3 Style");
    h3.StyleId = "Heading3";
    doc.Blocks.Add(h3);

    doc.Blocks.Add(new Paragraph(
        "Paragraph below H3. At vero eos et accusamus et iusto odio dignissimos ducimus qui blanditiis " +
        "praesentium voluptatum deleniti atque corrupti quos dolores et quas molestias."));

    var h1b = new Paragraph("2. Second Top-Level Heading");
    h1b.StyleId = "Heading1";
    doc.Blocks.Add(h1b);

    doc.Blocks.Add(new Paragraph(
        "Another Normal paragraph. The heading hierarchy above exercises H1, H2, H3, " +
        "Title and Normal styles. FreeW should render each with distinct visual treatment."));

    DocxWriter.Write(doc, Path.Combine(outDir, "01-heading-styles.docx"));
    Console.WriteLine("  01-heading-styles.docx");
}

// ─── 02  Character formatting ──────────────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Character Formatting Showcase");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    // One paragraph with labelled mixed runs
    static Paragraph CharPara(string label, RunFormatting fmt, string sample = "The quick brown fox jumps over the lazy dog") =>
        new Paragraph
        {
            Runs =
            {
                new Run(label + ": "),
                new Run(sample, fmt),
                new Run("  (back to normal)")
            }
        };

    doc.Blocks.Add(CharPara("Bold", new RunFormatting { Bold = true }));
    doc.Blocks.Add(CharPara("Italic", new RunFormatting { Italic = true }));
    doc.Blocks.Add(CharPara("Underline", new RunFormatting { Underline = true }));
    doc.Blocks.Add(CharPara("Strikethrough", new RunFormatting { Strikethrough = true }));
    doc.Blocks.Add(CharPara("Bold+Italic+Underline",
        new RunFormatting { Bold = true, Italic = true, Underline = true }));

    // Superscript / subscript
    var superPara = new Paragraph();
    superPara.Runs.Add(new Run("Superscript: E = mc"));
    superPara.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Superscript }));
    superPara.Runs.Add(new Run("   Subscript: H"));
    superPara.Runs.Add(new Run("2", new RunFormatting { VerticalAlign = VerticalAlign.Subscript }));
    superPara.Runs.Add(new Run("O"));
    doc.Blocks.Add(superPara);

    // Highlight / color
    doc.Blocks.Add(CharPara("Highlight yellow",
        new RunFormatting { HighlightColorHex = "#FFFF00" }));
    doc.Blocks.Add(CharPara("Font color red",
        new RunFormatting { ColorHex = "#CC0000" }));
    doc.Blocks.Add(CharPara("Font color blue",
        new RunFormatting { ColorHex = "#0000CC" }));

    // All-caps / small-caps
    doc.Blocks.Add(CharPara("All Caps",
        new RunFormatting { AllCaps = true }, "this text is all-caps"));
    doc.Blocks.Add(CharPara("Small Caps",
        new RunFormatting { SmallCaps = true }, "Small Capitals Example"));

    // Font family / size combos
    var fontPara = new Paragraph();
    fontPara.Runs.Add(new Run("Calibri 11pt (default)  "));
    fontPara.Runs.Add(new Run("Times New Roman 14pt", new RunFormatting { FontFamily = "Times New Roman", FontSizePt = 14 }));
    fontPara.Runs.Add(new Run("  Courier New 10pt", new RunFormatting { FontFamily = "Courier New", FontSizePt = 10 }));
    fontPara.Runs.Add(new Run("  Georgia 16pt bold", new RunFormatting { FontFamily = "Georgia", FontSizePt = 16, Bold = true }));
    doc.Blocks.Add(fontPara);

    // Character spacing
    doc.Blocks.Add(CharPara("Expanded spacing +2pt",
        new RunFormatting { CharacterSpacingPt = 2 }));
    doc.Blocks.Add(CharPara("Condensed spacing -1pt",
        new RunFormatting { CharacterSpacingPt = -1 }));

    DocxWriter.Write(doc, Path.Combine(outDir, "02-char-formatting.docx"));
    Console.WriteLine("  02-char-formatting.docx");
}

// ─── 03  Lists: bullets, numbered, multilevel ─────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("List Styles");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    // Bullet list
    var bHead = new Paragraph("Unordered (bullet) list:");
    bHead.StyleId = "Heading2";
    doc.Blocks.Add(bHead);

    foreach (var item in new[] { "Alpha item — first bullet", "Beta item — second bullet", "Gamma item — third bullet" })
    {
        var p = new Paragraph(item);
        p.Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 0 };
        doc.Blocks.Add(p);
    }

    var subBullet = new Paragraph("Nested sub-item (level 1)");
    subBullet.Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 1 };
    doc.Blocks.Add(subBullet);

    var subBullet2 = new Paragraph("Another nested sub-item");
    subBullet2.Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 1 };
    doc.Blocks.Add(subBullet2);

    var subBullet3 = new Paragraph("Back to top level");
    subBullet3.Formatting = new ParagraphFormatting { ListKind = ListKind.Bullet, ListLevel = 0 };
    doc.Blocks.Add(subBullet3);

    // Numbered list
    var nHead = new Paragraph("Ordered (numbered) list:");
    nHead.StyleId = "Heading2";
    doc.Blocks.Add(nHead);

    foreach (var item in new[] { "First step", "Second step", "Third step", "Fourth step" })
    {
        var p = new Paragraph(item);
        p.Formatting = new ParagraphFormatting { ListKind = ListKind.Number, ListLevel = 0 };
        doc.Blocks.Add(p);
    }

    // Multilevel
    var mlHead = new Paragraph("Multilevel (outline) list:");
    mlHead.StyleId = "Heading2";
    doc.Blocks.Add(mlHead);

    var mlItems = new (string text, int level)[]
    {
        ("Introduction", 0),
        ("Background", 0),
        ("Historical context", 1),
        ("Key events", 1),
        ("Primary source", 2),
        ("Methodology", 0),
        ("Data collection", 1),
        ("Analysis", 1),
        ("Conclusion", 0),
    };

    foreach (var (text, level) in mlItems)
    {
        var p = new Paragraph(text);
        p.Formatting = new ParagraphFormatting { ListKind = ListKind.MultiLevel, ListLevel = level };
        doc.Blocks.Add(p);
    }

    DocxWriter.Write(doc, Path.Combine(outDir, "03-lists.docx"));
    Console.WriteLine("  03-lists.docx");
}

// ─── 04  Paragraph alignment: L / C / R / J ──────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Paragraph Alignment");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    var loremLong =
        "The quick brown fox jumps over the lazy dog near the riverbank. " +
        "Pack my box with five dozen liquor jugs for the long journey ahead. " +
        "Sphinx of black quartz, judge my vow with great wisdom and care.";

    static Paragraph AlignPara(string label, TextAlignment align, string body)
    {
        var p = new Paragraph();
        p.Formatting = new ParagraphFormatting { Alignment = align, SpaceAfterPt = 12 };
        p.Runs.Add(new Run(label + "  ", new RunFormatting { Bold = true }));
        p.Runs.Add(new Run(body));
        return p;
    }

    doc.Blocks.Add(AlignPara("LEFT:", TextAlignment.Left, loremLong));
    doc.Blocks.Add(AlignPara("CENTER:", TextAlignment.Center, loremLong));
    doc.Blocks.Add(AlignPara("RIGHT:", TextAlignment.Right, loremLong));
    doc.Blocks.Add(AlignPara("JUSTIFY:", TextAlignment.Justify, loremLong));

    DocxWriter.Write(doc, Path.Combine(outDir, "04-para-alignment.docx"));
    Console.WriteLine("  04-para-alignment.docx");
}

// ─── 05  Line spacing + space before/after ────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Line Spacing & Paragraph Spacing");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    var body = "The quick brown fox jumps over the lazy dog. Lorem ipsum dolor sit amet, consectetur adipiscing elit.";

    Paragraph SpacingPara(string label, double lineSpacing, LineSpacingRule rule = LineSpacingRule.Multiple,
        double lineHeightPt = 0, double spaceBefore = 0, double spaceAfter = 8)
    {
        var p = new Paragraph();
        p.Formatting = new ParagraphFormatting
        {
            LineSpacing = lineSpacing,
            LineRule = rule,
            LineHeightPt = lineHeightPt,
            SpaceBeforePt = spaceBefore,
            SpaceAfterPt = spaceAfter,
            SpaceBeforeIsSet = spaceBefore > 0,
            SpaceAfterIsSet = true,
        };
        p.Runs.Add(new Run(label + ": ", new RunFormatting { Bold = true }));
        p.Runs.Add(new Run(body));
        return p;
    }

    doc.Blocks.Add(SpacingPara("Single (1.0)", 1.0));
    doc.Blocks.Add(SpacingPara("1.15 (default)", 1.15));
    doc.Blocks.Add(SpacingPara("1.5 lines", 1.5));
    doc.Blocks.Add(SpacingPara("Double (2.0)", 2.0));
    doc.Blocks.Add(SpacingPara("At Least 18pt", 0, LineSpacingRule.AtLeast, lineHeightPt: 18));
    doc.Blocks.Add(SpacingPara("Exact 24pt", 0, LineSpacingRule.Exact, lineHeightPt: 24));
    doc.Blocks.Add(SpacingPara("Space Before 24pt", 1.15, spaceBefore: 24, spaceAfter: 8));
    doc.Blocks.Add(SpacingPara("Space After 24pt", 1.15, spaceBefore: 0, spaceAfter: 24));

    DocxWriter.Write(doc, Path.Combine(outDir, "05-line-spacing.docx"));
    Console.WriteLine("  05-line-spacing.docx");
}

// ─── 06  Indents ──────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Paragraph Indentation");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    var body = "The quick brown fox jumps over the lazy dog. Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.";

    Paragraph IndentPara(string label, double left, double right, double firstLine)
    {
        var p = new Paragraph();
        p.Formatting = new ParagraphFormatting
        {
            IndentLeftPt = left,
            IndentRightPt = right,
            FirstLineIndentPt = firstLine,
            SpaceAfterPt = 12,
        };
        p.Runs.Add(new Run(label + ": ", new RunFormatting { Bold = true }));
        p.Runs.Add(new Run(body));
        return p;
    }

    doc.Blocks.Add(IndentPara("No indent (baseline)", 0, 0, 0));
    doc.Blocks.Add(IndentPara("Left indent 36pt", 36, 0, 0));
    doc.Blocks.Add(IndentPara("Right indent 36pt", 0, 36, 0));
    doc.Blocks.Add(IndentPara("Both 36pt", 36, 36, 0));
    doc.Blocks.Add(IndentPara("First-line +36pt", 0, 0, 36));
    doc.Blocks.Add(IndentPara("Hanging -36pt (first-line=-36, left=36)", 36, 0, -36));
    doc.Blocks.Add(IndentPara("Deep left 72pt", 72, 0, 0));
    doc.Blocks.Add(IndentPara("Deep hanging 72/−36", 72, 0, -36));

    DocxWriter.Write(doc, Path.Combine(outDir, "06-indents.docx"));
    Console.WriteLine("  06-indents.docx");
}

// ─── 07  Tab stops + leaders ──────────────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Tab Stops and Leaders");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    var desc = new Paragraph("Each line uses an explicit tab stop at the given position. Leader fills the tab gap.");
    doc.Blocks.Add(desc);

    // Helper: paragraph with tab stops
    static Paragraph TabPara(string leftLabel, string rightLabel,
        double posPt, TabStopAlignment align, TabLeader leader = TabLeader.None)
    {
        var p = new Paragraph();
        p.Formatting = new ParagraphFormatting
        {
            SpaceAfterPt = 6,
            TabStops = [new TabStop(posPt, align, leader)]
        };
        p.Runs.Add(new Run(leftLabel + "\t" + rightLabel));
        return p;
    }

    // Table-of-contents style: dots leader to right-aligned page number
    doc.Blocks.Add(new Paragraph("--- TOC-style dot-leader entries ---") { Formatting = new ParagraphFormatting { SpaceBeforePt = 12 } });
    doc.Blocks.Add(TabPara("Introduction", "1", 396, TabStopAlignment.Right, TabLeader.Dots));
    doc.Blocks.Add(TabPara("Background and Methodology", "5", 396, TabStopAlignment.Right, TabLeader.Dots));
    doc.Blocks.Add(TabPara("Results and Discussion", "12", 396, TabStopAlignment.Right, TabLeader.Dots));
    doc.Blocks.Add(TabPara("Conclusion", "18", 396, TabStopAlignment.Right, TabLeader.Dots));

    // Dash leader
    doc.Blocks.Add(new Paragraph("--- Dash leaders ---") { Formatting = new ParagraphFormatting { SpaceBeforePt = 12 } });
    doc.Blocks.Add(TabPara("Item A", "100.00", 360, TabStopAlignment.Right, TabLeader.Dashes));
    doc.Blocks.Add(TabPara("Item B", "250.50", 360, TabStopAlignment.Right, TabLeader.Dashes));

    // Center-aligned tab
    doc.Blocks.Add(new Paragraph("--- Center-aligned tab at 216pt ---") { Formatting = new ParagraphFormatting { SpaceBeforePt = 12 } });
    doc.Blocks.Add(TabPara("Left", "Centered text", 216, TabStopAlignment.Center));

    // Decimal tab
    doc.Blocks.Add(new Paragraph("--- Decimal-aligned tab at 288pt ---") { Formatting = new ParagraphFormatting { SpaceBeforePt = 12 } });
    doc.Blocks.Add(TabPara("Price", "1234.56", 288, TabStopAlignment.Decimal));
    doc.Blocks.Add(TabPara("Discount", "89.9", 288, TabStopAlignment.Decimal));
    doc.Blocks.Add(TabPara("Tax", "7.25", 288, TabStopAlignment.Decimal));

    DocxWriter.Write(doc, Path.Combine(outDir, "07-tab-stops.docx"));
    Console.WriteLine("  07-tab-stops.docx");
}

// ─── 08  Drop cap ─────────────────────────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Drop Cap");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    var desc = new Paragraph("The paragraph below has a drop cap applied to its first letter (42pt bold).");
    doc.Blocks.Add(desc);

    var dropcapPara = new Paragraph(
        "Once upon a midnight dreary, while I pondered, weak and weary, " +
        "over many a quaint and curious volume of forgotten lore, " +
        "while I nodded, nearly napping, suddenly there came a tapping, " +
        "as of someone gently rapping, rapping at my chamber door.");
    dropcapPara.Formatting = new ParagraphFormatting { SpaceAfterPt = 12 };
    DropCap.ApplyDropCap(dropcapPara);
    doc.Blocks.Add(dropcapPara);

    var normal1 = new Paragraph(
        "This paragraph has no drop cap for comparison. Lorem ipsum dolor sit amet, consectetur " +
        "adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua.");
    doc.Blocks.Add(normal1);

    var dropcapPara2 = new Paragraph(
        "Every dark and stormy night is a reminder that perseverance leads to clearer skies.");
    dropcapPara2.Formatting = new ParagraphFormatting { SpaceAfterPt = 12 };
    DropCap.ApplyDropCap(dropcapPara2, sizePt: 56);
    doc.Blocks.Add(dropcapPara2);

    DocxWriter.Write(doc, Path.Combine(outDir, "08-drop-cap.docx"));
    Console.WriteLine("  08-drop-cap.docx");
}

// ─── 09  Multi-column section ─────────────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Multi-Column Layout");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    // The single section for this doc: 2 columns with divider
    doc.Page.ColumnCount = 2;
    doc.Page.ColumnSpacingPt = 36;
    doc.Page.ColumnsLineBetween = true;

    doc.Blocks.Add(new Paragraph(
        "This document uses a 2-column layout with a vertical rule between columns. " +
        "The text should flow from the bottom of the first column into the top of the second."));

    var lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt " +
        "ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris " +
        "nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit in voluptate velit " +
        "esse cillum dolore eu fugiat nulla pariatur. Excepteur sint occaecat cupidatat non proident, sunt in " +
        "culpa qui officia deserunt mollit anim id est laborum. ";

    for (int i = 1; i <= 6; i++)
        doc.Blocks.Add(new Paragraph($"Paragraph {i}: " + lorem));

    DocxWriter.Write(doc, Path.Combine(outDir, "09-multicolumn.docx"));
    Console.WriteLine("  09-multicolumn.docx");
}

// ─── 10  Section break + different page setup ─────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Section Break — Next Page");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    doc.Blocks.Add(new Paragraph(
        "This is Section 1 — Portrait orientation, US Letter (8.5\" x 11\"), 1-inch margins. " +
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt " +
        "ut labore et dolore magna aliqua."));
    doc.Blocks.Add(new Paragraph(
        "More content in Section 1. The next page will begin Section 2 with Landscape orientation."));

    // Section break paragraph — Section 1 ends here, Section 2 begins on next page
    var sectionBreakPara = new Paragraph("(Section 1 ends — next-page break)");
    sectionBreakPara.Formatting = new ParagraphFormatting { SpaceAfterPt = 0 };
    var sec2Page = new PageSettings
    {
        WidthPt = 792,   // landscape: swap width/height
        HeightPt = 612,
        MarginLeftPt = 54,
        MarginRightPt = 54,
        MarginTopPt = 54,
        MarginBottomPt = 54,
        Landscape = true,
    };
    sectionBreakPara.SectionBreak = new Section(sec2Page, SectionBreakKind.NextPage);
    doc.Blocks.Add(sectionBreakPara);

    // Section 2 content
    var h2 = new Paragraph("Section 2 — Landscape, Narrower Margins");
    h2.StyleId = "Heading1";
    doc.Blocks.Add(h2);

    doc.Blocks.Add(new Paragraph(
        "This page is in LANDSCAPE orientation (11\" x 8.5\") with narrower 0.75-inch margins. " +
        "FreeW should honour the page size change and render this page wider than tall."));
    doc.Blocks.Add(new Paragraph(
        "The layout switch from portrait to landscape is a common real-world section-break use case. " +
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt."));

    DocxWriter.Write(doc, Path.Combine(outDir, "10-section-break.docx"));
    Console.WriteLine("  10-section-break.docx");
}

// ─── 11  Page border + text watermark ────────────────────────────────────────
{
    var doc = new TextDocument();

    var heading = new Paragraph("Page Border and Watermark");
    heading.StyleId = "Heading1";
    doc.Blocks.Add(heading);

    // Page border
    doc.Page.PageBorder = new PageBorder("#003366", 2.25)
    {
        LineStyle = BorderLineStyle.Double
    };

    // Watermark
    doc.Page.WatermarkOptions = new WatermarkOptions("DRAFT")
    {
        FontFamily = "Calibri",
        FontColorHex = "#C0C0C0",
        Layout = WatermarkLayout.Diagonal,
        Opacity = 0.4,
    };

    doc.Blocks.Add(new Paragraph(
        "This page has a DOUBLE blue page border drawn around it. " +
        "Behind the text you should see a grey diagonal DRAFT watermark."));

    doc.Blocks.Add(new Paragraph(
        "The border is 2.25pt double-line in dark blue (#003366). " +
        "The watermark is 72pt Calibri at 40% opacity, angled diagonally."));

    var lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt " +
        "ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation.";
    for (int i = 1; i <= 4; i++)
        doc.Blocks.Add(new Paragraph(lorem));

    DocxWriter.Write(doc, Path.Combine(outDir, "11-page-border-watermark.docx"));
    Console.WriteLine("  11-page-border-watermark.docx");
}

Console.WriteLine("Done. 11 files written.");
