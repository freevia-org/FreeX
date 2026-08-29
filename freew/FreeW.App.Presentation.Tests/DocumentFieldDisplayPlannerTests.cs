using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentFieldDisplayPlannerTests
{
    private static readonly DateTime EvaluatedAt = new(2026, 7, 25, 16, 5, 0);

    [Theory]
    [InlineData(RunFieldKind.Date, "7/25/2026")]
    [InlineData(RunFieldKind.Time, "4:05 PM")]
    public void Resolve_TemporalFieldsUseInvariantWordDefaults(RunFieldKind kind, string expected)
    {
        var document = TextDocument.CreateEmpty();

        DocumentFieldDisplayPlanner.Resolve(
                kind,
                "stale",
                document,
                new DocumentFieldDisplayContext(EvaluatedAt))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(RunFieldKind.Author, "Ada Lovelace")]
    [InlineData(RunFieldKind.Title, "Engine Notes")]
    [InlineData(RunFieldKind.Subject, "Deduplication")]
    [InlineData(RunFieldKind.Keywords, "shared,fields")]
    [InlineData(RunFieldKind.DocComments, "Reviewed")]
    public void Resolve_DocumentPropertiesPreferLiveNonEmptyValues(RunFieldKind kind, string expected)
    {
        var document = TextDocument.CreateEmpty();
        document.Properties.Author = "Ada Lovelace";
        document.Properties.Title = "Engine Notes";
        document.Properties.Subject = "Deduplication";
        document.Properties.Keywords = "shared,fields";
        document.Properties.Comments = "Reviewed";

        DocumentFieldDisplayPlanner.Resolve(
                kind,
                "stale",
                document,
                new DocumentFieldDisplayContext(EvaluatedAt))
            .Should().Be(expected);
    }

    [Fact]
    public void Resolve_FileAndPageContextUseLiveValuesAndInvariantPageCount()
    {
        var document = TextDocument.CreateEmpty();
        document.Page.PageNumberStartAt = 4;
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        var context = new DocumentFieldDisplayContext(
            EvaluatedAt,
            FileName: "current.docx",
            PageNumberText: "VII",
            PageCount: 12);

        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.FileName, "stale", document, context)
            .Should().Be("current.docx");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.PageNumber, "stale", document, context)
            .Should().Be("VII");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.NumPages, "stale", document, context)
            .Should().Be("12");
    }

    [Fact]
    public void Resolve_MissingLiveValuesKeepCacheAndPageUsesDocumentStart()
    {
        var document = TextDocument.CreateEmpty();
        document.Page.PageNumberStartAt = 4;
        document.Page.PageNumberFormat = PageNumberFormat.UpperRoman;
        var context = new DocumentFieldDisplayContext(EvaluatedAt);

        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.Author, "cached author", document, context)
            .Should().Be("cached author");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.FileName, "cached.docx", document, context)
            .Should().Be("cached.docx");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.NumPages, "cached pages", document, context)
            .Should().Be("cached pages");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.PageNumber, "cached page", document, context)
            .Should().Be("IV");
    }

    private static TextDocument TwoSectionDocument(out PageSettings section0Page)
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        section0Page = new PageSettings
        {
            PageNumberFormat = PageNumberFormat.LowerRoman,
            PageNumberStartAt = 1,
        };
        doc.Blocks.Add(new Paragraph("front matter")
        {
            SectionBreak = new Section(section0Page, SectionBreakKind.NextPage)
        });
        doc.Blocks.Add(new Paragraph("body"));
        doc.Page.PageNumberFormat = PageNumberFormat.Decimal;
        doc.Page.PageNumberStartAt = 1;
        return doc;
    }

    /// <summary>
    /// freew-page-numbering F1: a PAGE field's initial/fallback text must be resolved against the
    /// SECTION it is actually in, not unconditionally <see cref="TextDocument.Page"/> (the document's
    /// final section). Section 0 here is LowerRoman/start=1 (front matter); the final section is
    /// Decimal/start=1 (body) -- the pre-fix behavior returned "1" (the final section's value) for
    /// section 0 too.
    /// </summary>
    [Fact]
    public void ResolveFirstPageNumberText_UsesGivenSection_NotAlwaysDocumentFinalSection()
    {
        var doc = TwoSectionDocument(out var section0Page);

        DocumentFieldDisplayPlanner.ResolveFirstPageNumberText(doc, section0Page).Should().Be("i");
    }

    /// <summary>
    /// Sibling no-regression: the single-argument overload (used by every caller with no section
    /// context) keeps resolving against document.Page (the final section) exactly as before.
    /// </summary>
    [Fact]
    public void ResolveFirstPageNumberText_SingleArgOverload_StillUsesDocumentFinalSection()
    {
        var doc = TwoSectionDocument(out _);

        DocumentFieldDisplayPlanner.ResolveFirstPageNumberText(doc).Should().Be("1");
        DocumentFieldDisplayPlanner.ResolveFirstPageNumberText(doc, doc.Page).Should().Be("1");
    }

    /// <summary>
    /// Sibling no-regression: <see cref="DocumentFieldDisplayPlanner.Resolve"/>'s PageNumber branch, when
    /// given an explicit <see cref="DocumentFieldDisplayContext.PageNumberSection"/>, resolves against
    /// that section instead of document.Page -- exercising the same code path
    /// <see cref="ResolveFieldText"/> (the WPF host's body-field render fallback) drives.
    /// </summary>
    [Fact]
    public void Resolve_PageNumberWithExplicitSection_UsesThatSection_NotDocumentFinalSection()
    {
        var doc = TwoSectionDocument(out var section0Page);
        var contextForSection0 = new DocumentFieldDisplayContext(EvaluatedAt, PageNumberSection: section0Page);
        var contextWithNoSection = new DocumentFieldDisplayContext(EvaluatedAt);

        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.PageNumber, "stale", doc, contextForSection0)
            .Should().Be("i");
        DocumentFieldDisplayPlanner.Resolve(RunFieldKind.PageNumber, "stale", doc, contextWithNoSection)
            .Should().Be("1");
    }
}
