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
    public void Compare_dialog_plan_projects_shared_copy_and_option_catalogs()
    {
        var plan = ReviewCompareCombineWorkflow.BuildCompareDialogPlan(
            @"C:\Docs\Review\base.docx",
            new CompareDocumentsPromptState("Alice", " "));

        plan.Title.Should().Be("Compare Documents");
        plan.OriginalDisplayPath.Should().Be(@"...\Review\base.docx");
        plan.RevisedDisplayName.Should().Be("(current document)");
        plan.AuthorLabel.Should().Be("Label revisions with:");
        plan.DefaultAuthor.Should().Be("Alice");
        plan.ChangeOptions.Select(option => option.Kind).Should().Equal(
            CompareChangeKind.Insertions,
            CompareChangeKind.Deletions,
            CompareChangeKind.Moves,
            CompareChangeKind.Comments,
            CompareChangeKind.Formatting,
            CompareChangeKind.CaseChanges,
            CompareChangeKind.Whitespace);
        plan.ChangeOptions.Should().OnlyContain(option => option.IsChecked);
        plan.ShowOptions.Single(option => option.IsChecked).Value
            .Should().Be(CompareShowChangesIn.NewDocument);
    }

    [Fact]
    public void Compare_dialog_result_normalizes_author_and_builds_settings()
    {
        var selection = new CompareDocumentsDialogSelection(
            Insertions: true,
            Deletions: false,
            Moves: true,
            Comments: false,
            Formatting: true,
            CaseChanges: false,
            Whitespace: true,
            ShowChangesIn: CompareShowChangesIn.Revised);

        var accepted = ReviewCompareCombineWorkflow.TryBuildCompareDialogResult(
            @"C:\Docs\base.docx",
            "  Alice  ",
            selection,
            out var result,
            out var validationMessage);

        accepted.Should().BeTrue();
        validationMessage.Should().BeNull();
        result!.Author.Should().Be("Alice");
        result.OriginalFilePath.Should().Be(@"C:\Docs\base.docx");
        result.Settings.Deletions.Should().BeFalse();
        result.Settings.Comments.Should().BeFalse();
        result.Settings.CaseChanges.Should().BeFalse();
        result.Settings.ShowChangesIn.Should().Be(CompareShowChangesIn.Revised);
    }

    [Fact]
    public void Compare_dialog_result_rejects_missing_author()
    {
        var accepted = ReviewCompareCombineWorkflow.TryBuildCompareDialogResult(
            @"C:\Docs\base.docx",
            " ",
            new CompareDocumentsDialogSelection(true, true, true, true, true, true, true, CompareShowChangesIn.NewDocument),
            out var result,
            out var validationMessage);

        accepted.Should().BeFalse();
        result.Should().BeNull();
        validationMessage.Should().Be(ReviewCompareCombineWorkflow.MissingCompareAuthorMessage);
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
    public void Combine_dialog_plan_projects_shared_labels_and_short_paths()
    {
        var plan = ReviewCompareCombineWorkflow.BuildCombineDialogPlan(
            @"C:\Docs\Review\base.docx",
            "/tmp/review/revised.docx",
            new CombineDocumentsPromptState("Alice", "Bob", " "));

        plan.Title.Should().Be("Combine Documents");
        plan.OriginalLabel.Should().Be("Original:");
        plan.OriginalDisplayPath.Should().Be(@"...\Review\base.docx");
        plan.ReviewerADisplayName.Should().Be("(current document)");
        plan.ReviewerBDisplayPath.Should().Be(@"...\review\revised.docx");
        plan.AuthorALabel.Should().Be("Label Reviewer A with:");
        plan.AuthorBLabel.Should().Be("Label Reviewer B with:");
        plan.DefaultAuthorA.Should().Be("Alice");
        plan.DefaultAuthorB.Should().Be("Bob");
    }

    [Fact]
    public void Combine_dialog_result_trims_authors_and_preserves_paths()
    {
        var accepted = ReviewCompareCombineWorkflow.TryBuildCombineDialogResult(
            @"C:\Docs\base.docx",
            @"C:\Docs\revised.docx",
            "  Alice  ",
            "  Bob  ",
            out var result,
            out var validationMessage);

        accepted.Should().BeTrue();
        validationMessage.Should().BeNull();
        result.Should().Be(new CombineDocumentsDialogResult(
            @"C:\Docs\base.docx",
            @"C:\Docs\revised.docx",
            "Alice",
            "Bob"));
    }

    [Theory]
    [InlineData(" ", "Bob", ReviewCompareCombineWorkflow.MissingCombineAuthorAMessage)]
    [InlineData("Alice", " ", ReviewCompareCombineWorkflow.MissingCombineAuthorBMessage)]
    public void Combine_dialog_result_rejects_missing_reviewer_names(
        string authorA,
        string authorB,
        string expectedMessage)
    {
        var accepted = ReviewCompareCombineWorkflow.TryBuildCombineDialogResult(
            @"C:\Docs\base.docx",
            @"C:\Docs\revised.docx",
            authorA,
            authorB,
            out var result,
            out var validationMessage);

        accepted.Should().BeFalse();
        result.Should().BeNull();
        validationMessage.Should().Be(expectedMessage);
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
