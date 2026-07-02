using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewCompareCombineWorkflowTests
{
    private const string DateXml = "2026-07-03T09:10:11Z";

    [Fact]
    public void Compare_prompt_prefers_document_author_and_title()
    {
        var document = Doc("Current text");
        document.Properties.Author = "  Alice  ";
        document.Properties.Title = "  Contract draft  ";

        var state = ReviewCompareCombineWorkflow.BuildComparePrompt(
            document,
            @"C:\Docs\fallback.docx",
            "OS user");

        state.DefaultAuthor.Should().Be("Alice");
        state.RevisedTitle.Should().Be("Contract draft");
    }

    [Fact]
    public void Compare_prompt_falls_back_to_os_author_and_current_file_name()
    {
        var state = ReviewCompareCombineWorkflow.BuildComparePrompt(
            Doc("Current text"),
            @"C:\Docs\revised.docx",
            "  OS user  ");

        state.DefaultAuthor.Should().Be("OS user");
        state.RevisedTitle.Should().Be("revised.docx");
    }

    [Fact]
    public void Combine_prompt_seeds_both_reviewer_labels()
    {
        var document = Doc("Reviewer A text");
        document.Properties.Author = " Reviewer A ";

        var state = ReviewCompareCombineWorkflow.BuildCombinePrompt(
            document,
            "/tmp/reviewer-a.docx",
            "OS user");

        state.DefaultAuthorA.Should().Be("Reviewer A");
        state.DefaultAuthorB.Should().Be(ReviewCompareCombineWorkflow.DefaultReviewerB);
        state.ReviewerATitle.Should().Be("reviewer-a.docx");
    }

    [Fact]
    public void Revision_date_xml_is_utc_and_second_precision()
    {
        var timestamp = new DateTimeOffset(2026, 7, 3, 12, 10, 11, TimeSpan.FromHours(3));

        ReviewCompareCombineWorkflow.CreateRevisionDateXml(timestamp)
            .Should().Be(DateXml);
    }

    [Fact]
    public void Execute_compare_runs_model_engine_with_trimmed_author()
    {
        var result = ReviewCompareCombineWorkflow.ExecuteCompare(
            new CompareDocumentsExecutionInput(
                Doc("Hello world"),
                Doc("Hello brave world"),
                "  Reviewer  ",
                DateXml,
                CompareSettings.Default));

        result.PlainText.Should().Contain("brave");
        result.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(run => run.Revision != RevisionKind.None)
            .Should()
            .OnlyContain(run => run.RevisionAuthor == "Reviewer" && run.RevisionDateXml == DateXml);
    }

    [Fact]
    public void Execute_combine_runs_model_engine_for_both_reviewers()
    {
        var result = ReviewCompareCombineWorkflow.ExecuteCombine(
            new CombineDocumentsExecutionInput(
                Doc("Alpha\nBeta"),
                Doc("Alpha revised\nBeta"),
                "Reviewer A",
                Doc("Alpha\nBeta revised"),
                "Reviewer B",
                DateXml));

        var authors = result.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(run => run.Revision != RevisionKind.None)
            .Select(run => run.RevisionAuthor)
            .ToHashSet();

        authors.Should().Contain("Reviewer A");
        authors.Should().Contain("Reviewer B");
    }

    [Fact]
    public void Execute_compare_rejects_empty_author()
    {
        var act = () => ReviewCompareCombineWorkflow.ExecuteCompare(
            new CompareDocumentsExecutionInput(
                Doc("Original"),
                Doc("Revised"),
                " ",
                DateXml,
                CompareSettings.Default));

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Dialog_path_truncation_keeps_parent_and_file_for_windows_or_unix_paths()
    {
        ReviewCompareCombineWorkflow.TruncatePathForDialog(@"C:\Docs\Review\base.docx")
            .Should().Be(@"...\Review\base.docx");
        ReviewCompareCombineWorkflow.TruncatePathForDialog("/home/me/review/revised.docx")
            .Should().Be(@"...\review\revised.docx");
    }

    private static TextDocument Doc(string text)
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        foreach (var paragraph in text.Split('\n'))
            document.Blocks.Add(new Paragraph(paragraph));
        return document;
    }
}
