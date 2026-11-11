namespace FreeW.Core.Model;

/// <summary>
/// One built-in starting document offered by File &gt; New. <see cref="Id"/> is the stable key hosts
/// and resource catalogs use; <see cref="DefaultName"/>/<see cref="DefaultDescription"/> are the
/// English fallbacks for the localized tile caption. <see cref="Create"/> builds a fresh, independent
/// document on every call so a caller may hand the result straight to an editor.
/// </summary>
public sealed record BuiltInDocumentTemplate(
    string Id,
    string DefaultName,
    string DefaultDescription,
    Func<TextDocument> Create);

/// <summary>
/// FreeW's built-in document templates (Word's start-screen gallery). Every template is generated
/// from the document model -- real styles, headings, lists, tables, headers and footers -- so there
/// are no binary template files to ship, and each one saves as an ordinary .docx or .dotx. All
/// placeholder wording is original; bracketed text marks what the author is expected to replace.
/// </summary>
public static class BuiltInDocumentTemplates
{
    public const string BlankId = "blank";
    public const string BusinessLetterId = "business-letter";
    public const string ResumeId = "resume";
    public const string ReportId = "report";
    public const string MeetingNotesId = "meeting-notes";
    public const string MemoId = "memo";

    private const string Accent = "#2F5496";
    private const string Muted = "#595959";
    private const string HeaderFill = "#DEEAF6";
    private const double TextWidthPt = 468; // Letter page, 1in margins.

    private const string ContactStyleId = "ContactInfo";
    private const string FieldLabelStyleId = "FieldLabel";
    private const string CoverTitleStyleId = "CoverTitle";

    /// <summary>The gallery, in display order. "Blank document" is always first.</summary>
    public static IReadOnlyList<BuiltInDocumentTemplate> All { get; } =
    [
        new(BlankId, "Blank document", "Start from an empty page.", TextDocument.CreateEmpty),
        new(BusinessLetterId, "Business letter", "A formal letter with sender details, date, and signature block.", CreateBusinessLetter),
        new(ResumeId, "Resume", "A one-page resume with profile, experience, education, and skills.", CreateResume),
        new(ReportId, "Report", "A report with a cover page, table of contents, and numbered pages.", CreateReport),
        new(MeetingNotesId, "Meeting notes", "An agenda with attendees, discussion notes, and action items.", CreateMeetingNotes),
        new(MemoId, "Memo", "An internal memo with To, From, Date, and Subject lines.", CreateMemo),
    ];

    /// <summary>Finds a template by <see cref="BuiltInDocumentTemplate.Id"/> (ordinal, case-insensitive).</summary>
    public static BuiltInDocumentTemplate? Find(string? id) =>
        All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Builds the template with the given id, or null when the id is unknown.</summary>
    public static TextDocument? Create(string? id) => Find(id)?.Create();

    // ── Business letter ──────────────────────────────────────────────────────────────────────────

    private static TextDocument CreateBusinessLetter()
    {
        var doc = NewDocument("[Letter subject]");

        doc.FinalSectionHeadersFooters.Header = new HeaderFooter();
        doc.FinalSectionHeadersFooters.Header.Paragraphs.Add(Styled(
            "[Company name]",
            "Heading2",
            ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder(Accent, 1, BottomOnly: true),
                SpaceAfterPt = 0,
                SpaceAfterIsSet = true,
            }));
        doc.FinalSectionHeadersFooters.Header.Paragraphs.Add(Styled(
            "[Street address]  |  [City, postal code]  |  [Phone]  |  [Email]",
            ContactStyleId));
        doc.FinalSectionHeadersFooters.Footer = PageNumberFooter("[Company name]  |  [Website]");

        Add(doc, Tight(new Paragraph { Runs = { Run.DateField("[Date]") } }, spaceAfterPt: 18));

        Add(doc, Tight(new Paragraph("[Recipient name]")));
        Add(doc, Tight(new Paragraph("[Recipient title]")));
        Add(doc, Tight(new Paragraph("[Organization]")));
        Add(doc, Tight(new Paragraph("[Street address]")));
        Add(doc, Tight(new Paragraph("[City, postal code]"), spaceAfterPt: 18));

        Add(doc, new Paragraph("Dear [Recipient name],"));

        var subject = new Paragraph();
        subject.Runs.Add(new Run("Subject: ", new RunFormatting { Bold = true }));
        subject.Runs.Add(new Run("[What this letter is about]", new RunFormatting { Bold = true }));
        Add(doc, subject);

        Add(doc, new Paragraph(
            "[Open by stating why you are writing. One or two sentences is enough to tell the reader "
            + "what the letter concerns and what you would like them to do.]"));
        Add(doc, new Paragraph(
            "[Use the middle of the letter for the details the reader needs: background, dates, "
            + "amounts, or the reasoning behind a decision. Keep each paragraph to a single point.]"));
        Add(doc, new Paragraph(
            "[Close with the next step and how you can be reached. Thank the reader for their time.]"));

        Add(doc, Tight(new Paragraph("Yours sincerely,"), spaceAfterPt: 36));
        Add(doc, Tight(new Paragraph { Runs = { new Run("[Your name]", new RunFormatting { Bold = true }) } }));
        Add(doc, Tight(new Paragraph("[Your title]")));
        Add(doc, Tight(new Paragraph("[Company name]"), spaceAfterPt: 18));
        Add(doc, Styled("Enclosures: [List any enclosed documents]", ContactStyleId, LeftAligned()));

        return doc;
    }

    // ── Resume ───────────────────────────────────────────────────────────────────────────────────

    private static TextDocument CreateResume()
    {
        var doc = NewDocument("[Your name] - Resume");
        doc.Page.MarginLeftPt = doc.Page.MarginRightPt = 54;
        doc.Page.MarginTopPt = doc.Page.MarginBottomPt = 54;
        const double width = 612 - 108;

        Add(doc, Styled("[Your Name]", "Title", Centered()));
        Add(doc, Styled("[Job title or professional headline]", "Subtitle", Centered()));
        Add(doc, Styled(
            "[City, Region]  |  [Phone]  |  [Email]  |  [Portfolio or profile link]",
            ContactStyleId,
            Centered() with { Border = new ParagraphBorder(Accent, 1, BottomOnly: true) }));

        Add(doc, Styled("Profile", "Heading1"));
        Add(doc, new Paragraph(
            "[Two or three sentences summarizing your experience, the kind of work you do best, and "
            + "what you are looking for next. Write it for the role you are applying to.]"));

        Add(doc, Styled("Experience", "Heading1"));
        AddRole(doc, "[Job title]", "[Employer], [City]", "[Start] - [End]", width,
            "[Lead with a result: what you delivered and the difference it made.]",
            "[Describe the scope of your responsibilities, with numbers where you have them.]",
            "[Mention a tool, method, or skill you used that the new role asks for.]");
        AddRole(doc, "[Earlier job title]", "[Employer], [City]", "[Start] - [End]", width,
            "[Summarize the most relevant achievement from this role.]",
            "[Add one more accomplishment that shows progression.]");

        Add(doc, Styled("Education", "Heading1"));
        AddRole(doc, "[Degree or qualification]", "[Institution], [City]", "[Year]", width,
            "[Optional: honors, thesis title, or relevant coursework.]");

        Add(doc, Styled("Skills", "Heading1"));
        var skills = Grid(
            [width / 3, width / 3, width / 3],
            borders: false,
            headerRow: false,
            ["[Skill or tool]", "[Skill or tool]", "[Skill or tool]"],
            ["[Skill or tool]", "[Language - level]", "[Certification]"]);
        doc.Blocks.Add(skills);
        Add(doc, new Paragraph());

        return doc;
    }

    private static void AddRole(
        TextDocument doc,
        string title,
        string organization,
        string dates,
        double widthPt,
        params string[] bullets)
    {
        var heading = new Paragraph
        {
            StyleId = "Heading2",
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(widthPt, TabStopAlignment.Right)],
            },
        };
        heading.Runs.Add(new Run(title));
        heading.Runs.Add(new Run("\t" + dates, new RunFormatting { Bold = false, ColorHex = Muted, FontSizePt = 10 }));
        doc.Blocks.Add(heading);

        Add(doc, Tight(new Paragraph
        {
            Runs = { new Run(organization, new RunFormatting { Italic = true, ColorHex = Muted }) },
        }, spaceAfterPt: 4));

        foreach (var bullet in bullets)
            Add(doc, Bullet(bullet));
    }

    // ── Report ───────────────────────────────────────────────────────────────────────────────────

    private static TextDocument CreateReport()
    {
        var doc = NewDocument("[Report title]");
        doc.Page.DifferentFirstPage = true; // The cover page carries no running header or footer.

        var header = new HeaderFooter();
        header.Paragraphs.Add(Styled(
            "[Report title]",
            ContactStyleId,
            ParagraphFormatting.Default with
            {
                Alignment = TextAlignment.Right,
                Border = new ParagraphBorder("#BFBFBF", 0.5, BottomOnly: true),
            }));
        doc.FinalSectionHeadersFooters.Header = header;
        doc.FinalSectionHeadersFooters.Footer = PageNumberFooter("[Organization]");
        doc.FinalSectionHeadersFooters.FirstHeader = new HeaderFooter(string.Empty);
        doc.FinalSectionHeadersFooters.FirstFooter = new HeaderFooter(string.Empty);

        // Cover page.
        Add(doc, Spacer(120));
        // A dedicated cover style: the built-in Title style sits in the document outline, which would
        // list the cover title as the first entry of the generated contents page.
        doc.Styles[CoverTitleStyleId] = new DocumentStyle
        {
            Id = CoverTitleStyleId,
            Name = "Cover Title",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, FontSizePt = 32, ColorHex = Accent },
            Paragraph = new ParagraphFormatting
            {
                Alignment = TextAlignment.Center,
                SpaceAfterPt = 8,
                SpaceAfterIsSet = true,
            },
        };
        Add(doc, Styled("[Report Title]", CoverTitleStyleId, Centered()));
        Add(doc, Styled("[Subtitle or one-line summary]", "Subtitle", Centered() with
        {
            Border = new ParagraphBorder(Accent, 1.5, BottomOnly: true),
        }));
        Add(doc, Spacer(48));
        Add(doc, Tight(Aligned("Prepared by [Author name]", TextAlignment.Center)));
        Add(doc, Tight(Aligned("[Organization or department]", TextAlignment.Center)));
        var coverDate = Tight(new Paragraph { Runs = { Run.DateField("[Date]") } });
        coverDate.Formatting = coverDate.Formatting with { Alignment = TextAlignment.Center };
        coverDate.Runs.Add(Run.PageBreak());
        Add(doc, coverDate);

        // Body. The first heading starts a fresh page so the contents page stands alone.
        var tocIndex = doc.Blocks.Count;
        Add(doc, Styled("Executive Summary", "Heading1", ParagraphFormatting.Default with { PageBreakBefore = true }));
        Add(doc, new Paragraph(
            "[Summarize the whole report in a short paragraph: the question you set out to answer, "
            + "what you found, and what you recommend. Many readers will read only this section.]"));

        Add(doc, Styled("Introduction", "Heading1"));
        Add(doc, new Paragraph(
            "[Explain the background and why the work was needed. State the scope so the reader "
            + "knows what is covered and what is not.]"));
        Add(doc, Styled("Objectives", "Heading2"));
        Add(doc, Bullet("[First objective]"));
        Add(doc, Bullet("[Second objective]"));
        Add(doc, Bullet("[Third objective]"));
        Add(doc, Styled("Approach", "Heading2"));
        Add(doc, new Paragraph(
            "[Describe how the work was carried out: the sources you used, who was consulted, and "
            + "any limits on the data.]"));

        Add(doc, Styled("Findings", "Heading1"));
        Add(doc, new Paragraph(
            "[Present the results in order of importance. Use one subsection per finding and support "
            + "each with evidence.]"));
        Add(doc, Styled("Key figures", "Heading2"));
        doc.Blocks.Add(Grid(
            [TextWidthPt * 0.4, TextWidthPt * 0.2, TextWidthPt * 0.2, TextWidthPt * 0.2],
            borders: true,
            headerRow: true,
            ["Measure", "Previous", "Current", "Change"],
            ["[Measure one]", "[Value]", "[Value]", "[%]"],
            ["[Measure two]", "[Value]", "[Value]", "[%]"],
            ["[Measure three]", "[Value]", "[Value]", "[%]"]));
        Add(doc, Spacer(6));
        Add(doc, Styled("Analysis", "Heading2"));
        Add(doc, new Paragraph(
            "[Interpret the figures above. Point out what changed, why it matters, and anything the "
            + "reader should treat with caution.]"));

        Add(doc, Styled("Recommendations", "Heading1"));
        Add(doc, Numbered("[First recommendation, with the owner and a target date.]"));
        Add(doc, Numbered("[Second recommendation.]"));
        Add(doc, Numbered("[Third recommendation.]"));

        Add(doc, Styled("Conclusion", "Heading1"));
        Add(doc, new Paragraph(
            "[Restate the main message and the decision or action you are asking for.]"));

        // The contents page is generated from the headings above, then placed after the cover.
        TableOfContents.EnsureStyles(doc);
        doc.Blocks.InsertRange(tocIndex, TableOfContents.Build(doc, insertionBlockIndex: tocIndex));

        return doc;
    }

    // ── Meeting notes ────────────────────────────────────────────────────────────────────────────

    private static TextDocument CreateMeetingNotes()
    {
        var doc = NewDocument("[Meeting title]");
        doc.FinalSectionHeadersFooters.Footer = PageNumberFooter("[Team or project name]");

        Add(doc, Styled("[Meeting Title]", "Title"));
        Add(doc, Styled("Agenda and notes", "Subtitle"));

        doc.Blocks.Add(LabelGrid(
            ("Date", "[Date]"),
            ("Time", "[Start] - [End]"),
            ("Location", "[Room or call link]"),
            ("Facilitator", "[Name]"),
            ("Note taker", "[Name]")));
        Add(doc, Spacer(6));

        Add(doc, Styled("Attendees", "Heading1"));
        Add(doc, new Paragraph("[Name], [Name], [Name], [Name]"));
        Add(doc, Tight(new Paragraph
        {
            Runs =
            {
                new Run("Apologies: ", new RunFormatting { Bold = true }),
                new Run("[Name], [Name]"),
            },
        }, spaceAfterPt: 8));

        Add(doc, Styled("Agenda", "Heading1"));
        Add(doc, Numbered("[Welcome and review of previous actions]"));
        Add(doc, Numbered("[First topic] - [Presenter], [minutes]"));
        Add(doc, Numbered("[Second topic] - [Presenter], [minutes]"));
        Add(doc, Numbered("[Any other business]"));

        Add(doc, Styled("Discussion", "Heading1"));
        Add(doc, Styled("[First topic]", "Heading2"));
        Add(doc, Bullet("[Main point raised]"));
        Add(doc, Bullet("[Decision reached, and who made it]"));
        Add(doc, Styled("[Second topic]", "Heading2"));
        Add(doc, Bullet("[Main point raised]"));
        Add(doc, Bullet("[Question left open]"));

        Add(doc, Styled("Action items", "Heading1"));
        doc.Blocks.Add(Grid(
            [TextWidthPt * 0.46, TextWidthPt * 0.2, TextWidthPt * 0.17, TextWidthPt * 0.17],
            borders: true,
            headerRow: true,
            ["Action", "Owner", "Due", "Status"],
            ["[What needs to be done]", "[Name]", "[Date]", "[Open]"],
            ["[What needs to be done]", "[Name]", "[Date]", "[Open]"],
            ["[What needs to be done]", "[Name]", "[Date]", "[Open]"]));
        Add(doc, Spacer(6));

        Add(doc, Styled("Next meeting", "Heading1"));
        Add(doc, new Paragraph("[Date, time, and location of the next meeting]"));

        return doc;
    }

    // ── Memo ─────────────────────────────────────────────────────────────────────────────────────

    private static TextDocument CreateMemo()
    {
        var doc = NewDocument("[Memo subject]");

        var header = new HeaderFooter();
        header.Paragraphs.Add(Styled("[Organization name]", ContactStyleId, ParagraphFormatting.Default with
        {
            Alignment = TextAlignment.Right,
        }));
        doc.FinalSectionHeadersFooters.Header = header;
        doc.FinalSectionHeadersFooters.Footer = PageNumberFooter("Internal");

        Add(doc, Styled("Memorandum", "Title"));

        doc.Blocks.Add(LabelGrid(
            ("To", "[Recipient names]"),
            ("From", "[Your name and title]"),
            ("CC", "[Other recipients]"),
            ("Date", "[Date]"),
            ("Subject", "[What this memo is about]")));

        Add(doc, new Paragraph
        {
            Formatting = ParagraphFormatting.Default with
            {
                Border = new ParagraphBorder(Accent, 1.5, BottomOnly: true),
                SpaceAfterPt = 12,
                SpaceAfterIsSet = true,
            },
        });

        Add(doc, Styled("Purpose", "Heading2"));
        Add(doc, new Paragraph(
            "[State the reason for the memo in one or two sentences. Say plainly what you need from "
            + "the reader and by when.]"));

        Add(doc, Styled("Background", "Heading2"));
        Add(doc, new Paragraph(
            "[Give only the context the reader needs to understand the request or decision.]"));

        Add(doc, Styled("Details", "Heading2"));
        Add(doc, Bullet("[First key point]"));
        Add(doc, Bullet("[Second key point]"));
        Add(doc, Bullet("[Third key point]"));

        Add(doc, Styled("Next steps", "Heading2"));
        Add(doc, Numbered("[Action, owner, and date]"));
        Add(doc, Numbered("[Action, owner, and date]"));

        Add(doc, new Paragraph("[Close with who to contact for questions.]"));

        return doc;
    }

    // ── Shared building blocks ───────────────────────────────────────────────────────────────────

    private static TextDocument NewDocument(string title)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        BuiltInStyles.EnsureSeeded(doc, "ListParagraph");
        doc.Styles[ContactStyleId] = new DocumentStyle
        {
            Id = ContactStyleId,
            Name = "Contact Info",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { FontSizePt = 9, ColorHex = Muted },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 6, SpaceAfterIsSet = true },
        };
        doc.Styles[FieldLabelStyleId] = new DocumentStyle
        {
            Id = FieldLabelStyleId,
            Name = "Field Label",
            BasedOnStyleId = "Normal",
            Run = new RunFormatting { Bold = true, ColorHex = Accent },
            Paragraph = new ParagraphFormatting { SpaceAfterPt = 0, SpaceAfterIsSet = true },
        };
        doc.Properties.Title = title;
        return doc;
    }

    private static void Add(TextDocument doc, Paragraph paragraph) => doc.Blocks.Add(paragraph);

    private static Paragraph Styled(string text, string styleId, ParagraphFormatting? formatting = null) =>
        new(text) { StyleId = styleId, Formatting = formatting ?? ParagraphFormatting.Default };

    private static ParagraphFormatting Centered() =>
        ParagraphFormatting.Default with { Alignment = TextAlignment.Center };

    private static ParagraphFormatting LeftAligned() =>
        ParagraphFormatting.Default with { Alignment = TextAlignment.Left };

    private static Paragraph Aligned(string text, TextAlignment alignment) =>
        new(text) { Formatting = ParagraphFormatting.Default with { Alignment = alignment } };

    /// <summary>Removes the default space after a paragraph (address blocks, signature lines).</summary>
    private static Paragraph Tight(Paragraph paragraph, double spaceAfterPt = 0)
    {
        paragraph.Formatting = paragraph.Formatting with
        {
            SpaceAfterPt = spaceAfterPt,
            SpaceAfterIsSet = true,
        };
        return paragraph;
    }

    private static Paragraph Spacer(double heightPt) =>
        new()
        {
            Formatting = ParagraphFormatting.Default with
            {
                SpaceBeforePt = heightPt,
                SpaceBeforeIsSet = true,
                SpaceAfterPt = 0,
                SpaceAfterIsSet = true,
            },
        };

    private static Paragraph Bullet(string text) =>
        new(text)
        {
            StyleId = "ListParagraph",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Bullet },
        };

    private static Paragraph Numbered(string text) =>
        new(text)
        {
            StyleId = "ListParagraph",
            Formatting = ParagraphFormatting.Default with { ListKind = ListKind.Number },
        };

    private static HeaderFooter PageNumberFooter(string leftText)
    {
        var footer = new HeaderFooter();
        var paragraph = new Paragraph
        {
            StyleId = ContactStyleId,
            Formatting = ParagraphFormatting.Default with
            {
                TabStops = [new TabStop(TextWidthPt, TabStopAlignment.Right)],
            },
        };
        paragraph.Runs.Add(new Run(leftText + "\tPage "));
        paragraph.Runs.Add(Run.PageNumberField());
        footer.Paragraphs.Add(paragraph);
        return footer;
    }

    /// <summary>A two-column label/value block (memo header, meeting details) without borders.</summary>
    private static Table LabelGrid(params (string Label, string Value)[] rows)
    {
        var table = new Table { Formatting = TableFormatting.Default with { Borders = false } };
        table.ColumnWidthsPt.AddRange([90, TextWidthPt - 90]);
        foreach (var (label, value) in rows)
        {
            var row = new TableRow();
            var labelCell = new TableCell();
            labelCell.Paragraphs.Add(new Paragraph(label) { StyleId = FieldLabelStyleId });
            var valueCell = new TableCell();
            valueCell.Paragraphs.Add(Tight(new Paragraph(value)));
            row.Cells.Add(labelCell);
            row.Cells.Add(valueCell);
            table.Rows.Add(row);
        }

        return table;
    }

    private static Table Grid(double[] columnWidthsPt, bool borders, bool headerRow, params string[][] rows)
    {
        var table = new Table
        {
            Formatting = TableFormatting.Default with
            {
                Borders = borders,
                HeaderRow = headerRow,
                RepeatHeaderRow = headerRow,
            },
        };
        table.ColumnWidthsPt.AddRange(columnWidthsPt);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var isHeader = headerRow && rowIndex == 0;
            var row = new TableRow { IsRepeatingHeader = isHeader };
            foreach (var text in rows[rowIndex])
            {
                var cell = new TableCell();
                var paragraph = Tight(new Paragraph());
                paragraph.Runs.Add(new Run(text, isHeader ? new RunFormatting { Bold = true } : null));
                cell.Paragraphs.Add(paragraph);
                if (isHeader)
                    cell.ShadingColorHex = HeaderFill;
                row.Cells.Add(cell);
            }

            table.Rows.Add(row);
        }

        return table;
    }
}
