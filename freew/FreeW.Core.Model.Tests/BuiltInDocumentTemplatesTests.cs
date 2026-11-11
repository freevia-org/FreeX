using FluentAssertions;
using FreeW.Core.Model;

namespace FreeW.Core.Model.Tests;

public sealed class BuiltInDocumentTemplatesTests
{
    public static TheoryData<string> TemplateIds()
    {
        var data = new TheoryData<string>();
        foreach (var template in BuiltInDocumentTemplates.All)
            data.Add(template.Id);
        return data;
    }

    [Fact]
    public void Gallery_OffersBlankFirstAndTheWordStartScreenSet()
    {
        BuiltInDocumentTemplates.All.Select(template => template.Id).Should().Equal(
            BuiltInDocumentTemplates.BlankId,
            BuiltInDocumentTemplates.BusinessLetterId,
            BuiltInDocumentTemplates.ResumeId,
            BuiltInDocumentTemplates.ReportId,
            BuiltInDocumentTemplates.MeetingNotesId,
            BuiltInDocumentTemplates.MemoId);
        BuiltInDocumentTemplates.All.Should().OnlyContain(template =>
            template.DefaultName.Length > 0 && template.DefaultDescription.Length > 0);
        BuiltInDocumentTemplates.All.Select(template => template.DefaultName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Find_IsCaseInsensitiveAndNullForUnknownIds()
    {
        BuiltInDocumentTemplates.Find("REPORT")!.Id.Should().Be(BuiltInDocumentTemplates.ReportId);
        BuiltInDocumentTemplates.Find("no-such-template").Should().BeNull();
        BuiltInDocumentTemplates.Find(null).Should().BeNull();
        BuiltInDocumentTemplates.Create("no-such-template").Should().BeNull();
    }

    [Fact]
    public void Blank_IsTheSameDocumentAsFileNew()
    {
        var blank = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.BlankId)!;

        blank.Blocks.Should().ContainSingle().Which.Should().BeOfType<Paragraph>()
            .Which.PlainText.Should().BeEmpty();
        blank.Styles.Keys.Should().BeEquivalentTo(TextDocument.CreateEmpty().Styles.Keys);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Create_ReturnsAnIndependentDocumentOnEveryCall(string id)
    {
        var first = BuiltInDocumentTemplates.Create(id)!;
        var second = BuiltInDocumentTemplates.Create(id)!;

        second.Should().NotBeSameAs(first);
        second.PlainText.Should().Be(first.PlainText);
        first.Blocks.Add(new Paragraph("edited"));
        BuiltInDocumentTemplates.Create(id)!.PlainText.Should().Be(second.PlainText);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void Create_EveryStyleReferenceResolvesInTheDocumentCatalog(string id)
    {
        var document = BuiltInDocumentTemplates.Create(id)!;

        var styleIds = AllParagraphs(document)
            .Select(paragraph => paragraph.StyleId)
            .Where(styleId => !string.IsNullOrEmpty(styleId))
            .Distinct();

        styleIds.Should().OnlyContain(styleId => document.Styles.ContainsKey(styleId!));
        document.Styles.Values
            .Where(style => style.BasedOnStyleId is not null)
            .Should().OnlyContain(style => document.Styles.ContainsKey(style.BasedOnStyleId!));
    }

    [Theory]
    [InlineData(BuiltInDocumentTemplates.BusinessLetterId)]
    [InlineData(BuiltInDocumentTemplates.ResumeId)]
    [InlineData(BuiltInDocumentTemplates.ReportId)]
    [InlineData(BuiltInDocumentTemplates.MeetingNotesId)]
    [InlineData(BuiltInDocumentTemplates.MemoId)]
    public void Create_ContentTemplatesAreCredibleStartingPoints(string id)
    {
        var document = BuiltInDocumentTemplates.Create(id)!;

        document.Blocks.Count.Should().BeGreaterThan(8);
        document.Properties.Title.Should().NotBeNullOrWhiteSpace();
        document.Paragraphs.Should().Contain(paragraph => paragraph.StyleId != null,
            "templates are built from named styles, not direct formatting alone");
        document.PlainText.Should().Contain("[", "bracketed placeholders mark what the author replaces");
    }

    [Fact]
    public void BusinessLetter_HasLetterheadDateSalutationAndSignature()
    {
        var letter = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.BusinessLetterId)!;

        letter.FinalSectionHeadersFooters.Header!.Paragraphs.Should().NotBeEmpty();
        letter.FinalSectionHeadersFooters.Footer!.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.FieldKind == RunFieldKind.PageNumber);
        letter.Paragraphs.SelectMany(paragraph => paragraph.Runs)
            .Should().Contain(run => run.FieldKind == RunFieldKind.Date);
        letter.PlainText.Should().Contain("Dear [Recipient name],").And.Contain("Yours sincerely,");
    }

    [Fact]
    public void Resume_HasNameSectionsBulletsAndASkillsTable()
    {
        var resume = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.ResumeId)!;

        resume.Paragraphs.First().StyleId.Should().Be("Title");
        resume.Paragraphs.Where(paragraph => paragraph.StyleId == "Heading1")
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Profile", "Experience", "Education", "Skills");
        resume.Paragraphs.Should().Contain(paragraph => paragraph.Formatting.ListKind == ListKind.Bullet);
        resume.Blocks.OfType<Table>().Should().ContainSingle();
        resume.Page.MarginLeftPt.Should().Be(54);
    }

    [Fact]
    public void Report_HasCoverPageGeneratedContentsHeadingsAndRunningHeader()
    {
        var report = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.ReportId)!;
        var paragraphs = report.Paragraphs.ToList();

        // Cover page ends with an explicit page break and has no running header/footer.
        report.Page.DifferentFirstPage.Should().BeTrue();
        report.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        report.FinalSectionHeadersFooters.FirstHeader.Should().NotBeNull();
        paragraphs[1].PlainText.Should().Be("[Report Title]");
        paragraphs.Should().NotContain(paragraph => paragraph.StyleId == "Title",
            "a Title-styled cover would be listed in the generated contents");
        var coverBreak = paragraphs.FindIndex(paragraph => paragraph.Runs.Any(run => run.IsPageBreak));
        coverBreak.Should().BeGreaterThan(0);

        // A real, regenerable table of contents sits between the cover and the body.
        var tocHeading = paragraphs.FindIndex(paragraph => paragraph.StyleId == TableOfContents.HeadingStyleId);
        tocHeading.Should().Be(coverBreak + 1);
        var headings = paragraphs
            .Where(paragraph => paragraph.StyleId is "Heading1" or "Heading2")
            .Select(paragraph => paragraph.PlainText)
            .ToList();
        headings.Should().Contain(["Executive Summary", "Introduction", "Findings", "Recommendations", "Conclusion"]);
        var tocEntries = paragraphs
            .Where(paragraph => TableOfContents.IsTocParagraph(paragraph)
                && paragraph.StyleId != TableOfContents.HeadingStyleId)
            .ToList();
        tocEntries.Should().HaveCount(headings.Count);
        tocEntries[0].PlainText.Should().StartWith("Executive Summary");
        tocEntries[0].SpanningFieldStart.Should().NotBeNull("the contents must be a live TOC field");
        tocEntries[^1].EndsSpanningField.Should().BeTrue();

        // The body starts on its own page, after the contents.
        paragraphs.First(paragraph => paragraph.PlainText == "Executive Summary" && paragraph.StyleId == "Heading1")
            .Formatting.PageBreakBefore.Should().BeTrue();
        report.Blocks.OfType<Table>().Should().ContainSingle()
            .Which.Rows[0].IsRepeatingHeader.Should().BeTrue();
        paragraphs.Should().Contain(paragraph => paragraph.Formatting.ListKind == ListKind.Number);
    }

    [Fact]
    public void MeetingNotes_HasDetailsAgendaAndActionItemsTable()
    {
        var notes = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.MeetingNotesId)!;

        var tables = notes.Blocks.OfType<Table>().ToList();
        tables.Should().HaveCount(2);
        tables[0].Rows.Select(row => row.Cells[0].PlainText)
            .Should().Equal("Date", "Time", "Location", "Facilitator", "Note taker");
        tables[1].Rows[0].Cells.Select(cell => cell.PlainText)
            .Should().Equal("Action", "Owner", "Due", "Status");
        notes.Paragraphs.Where(paragraph => paragraph.StyleId == "Heading1")
            .Select(paragraph => paragraph.PlainText)
            .Should().Equal("Attendees", "Agenda", "Discussion", "Action items", "Next meeting");
    }

    [Fact]
    public void Memo_HasRoutingBlockAndSections()
    {
        var memo = BuiltInDocumentTemplates.Create(BuiltInDocumentTemplates.MemoId)!;

        memo.Paragraphs.First().PlainText.Should().Be("Memorandum");
        memo.Blocks.OfType<Table>().Should().ContainSingle()
            .Which.Rows.Select(row => row.Cells[0].PlainText)
            .Should().Equal("To", "From", "CC", "Date", "Subject");
        memo.FinalSectionHeadersFooters.Header.Should().NotBeNull();
        memo.FinalSectionHeadersFooters.Footer.Should().NotBeNull();
    }

    private static IEnumerable<Paragraph> AllParagraphs(TextDocument document)
    {
        foreach (var block in document.Blocks)
        {
            if (block is Paragraph paragraph)
                yield return paragraph;
            else if (block is Table table)
                foreach (var cellParagraph in table.Rows.SelectMany(row => row.Cells).SelectMany(cell => cell.Paragraphs))
                    yield return cellParagraph;
        }

        var parts = document.FinalSectionHeadersFooters;
        foreach (var part in new[] { parts.Header, parts.Footer, parts.FirstHeader, parts.FirstFooter })
            foreach (var paragraph in part?.Paragraphs ?? [])
                yield return paragraph;
    }
}
