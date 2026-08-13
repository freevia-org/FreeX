using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewingPaneRowPlannerTests
{
    [Theory]
    [InlineData(RevisionEntryKind.Insertion, "Insertion", "Alice • Inserted", "Accept this insertion change")]
    [InlineData(RevisionEntryKind.Deletion, "Deletion", "Alice • Deleted", "Accept this deletion change")]
    [InlineData(RevisionEntryKind.Formatting, "Formatting", "Alice • Formatted", "Accept this formatting change")]
    public void Build_maps_kind_title_and_action_wording(
        RevisionEntryKind kind,
        string expectedKind,
        string expectedTitle,
        string expectedAcceptToolTip)
    {
        var plan = ReviewingPaneRowPlanner.Build(Entry(kind, "Alice", "text", "2026-08-13T10:30:00Z"));

        plan.KindLabel.Should().Be(expectedKind);
        plan.Title.Should().Be(expectedTitle);
        plan.AcceptToolTip.Should().Be(expectedAcceptToolTip);
        plan.RejectToolTip.Should().Be(expectedAcceptToolTip.Replace("Accept", "Reject"));
        plan.DateLabel.Should().Be("2026-08-13");
    }

    [Fact]
    public void Build_preserves_WPF_unknown_author_and_preview_normalization()
    {
        var plan = ReviewingPaneRowPlanner.Build(Entry(
            RevisionEntryKind.Insertion,
            "  ",
            "  first\r\nsecond\n  ",
            "provider-date"));

        plan.AuthorLabel.Should().Be("Unknown");
        plan.Title.Should().Be("Unknown • Inserted");
        plan.PreviewText.Should().Be("first  second");
        plan.DateLabel.Should().Be("provider-date");
    }

    [Fact]
    public void Build_keeps_blank_optional_text_blank()
    {
        var plan = ReviewingPaneRowPlanner.Build(Entry(
            RevisionEntryKind.Formatting,
            null,
            " \r\n ",
            null));

        plan.PreviewText.Should().BeEmpty();
        plan.DateLabel.Should().BeEmpty();
    }

    private static RevisionEntry Entry(
        RevisionEntryKind kind,
        string? author,
        string text,
        string? dateXml)
    {
        var paragraph = new Paragraph();
        var run = new Run(text);
        paragraph.Runs.Add(run);
        return new RevisionEntry(0, kind, author, dateXml, text, paragraph, run);
    }
}
