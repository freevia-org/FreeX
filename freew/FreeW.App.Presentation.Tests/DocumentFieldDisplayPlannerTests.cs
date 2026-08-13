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
}
