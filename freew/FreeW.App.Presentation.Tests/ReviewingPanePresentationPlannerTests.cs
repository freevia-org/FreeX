using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Panes;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewingPanePresentationPlannerTests
{
    [Fact]
    public void Profiles_preserve_renderer_titles_sort_options_and_action_wording()
    {
        var compact = ReviewingPanePresentationPlanner.For(ReviewingPanePresentationProfile.CompactWpf);
        var detailed = ReviewingPanePresentationPlanner.For(ReviewingPanePresentationProfile.DetailedAvalonia);

        compact.PaneTitle.Should().Be("Revisions");
        detailed.PaneTitle.Should().Be("Tracked Changes");
        compact.SortLabel.Should().Be("Sort:");
        compact.SortOptions.Select(option => (option.Order, option.Label)).Should().Equal(
            (ReviewRevisionSortOrder.Sequence, "By Sequence"),
            (ReviewRevisionSortOrder.Author, "By Author"),
            (ReviewRevisionSortOrder.Kind, "By Type"),
            (ReviewRevisionSortOrder.Date, "By Date"));
        detailed.SortOptions.Should().BeSameAs(compact.SortOptions);
        compact.Actions.AcceptSelected.Should().Be(new ReviewingPaneActionDescriptor("Accept", "Accept the selected change"));
        compact.Actions.Previous.Should().Be(new ReviewingPaneActionDescriptor("\u25B2", "Previous change (jump up)"));
        detailed.Actions.AcceptAll.Should().Be(new ReviewingPaneActionDescriptor("Accept All", "Accept all tracked changes"));
        detailed.Actions.RejectAll.Should().Be(new ReviewingPaneActionDescriptor("Reject All", "Reject all tracked changes"));
    }

    [Theory]
    [InlineData(ReviewingPanePresentationProfile.CompactWpf, 0, "No tracked changes")]
    [InlineData(ReviewingPanePresentationProfile.CompactWpf, 1, "1 change")]
    [InlineData(ReviewingPanePresentationProfile.CompactWpf, 4, "4 changes")]
    [InlineData(ReviewingPanePresentationProfile.DetailedAvalonia, 0, "No tracked changes")]
    [InlineData(ReviewingPanePresentationProfile.DetailedAvalonia, 1, "1 change")]
    [InlineData(ReviewingPanePresentationProfile.DetailedAvalonia, 4, "4 changes")]
    public void Count_text_preserves_each_renderer_profile(
        ReviewingPanePresentationProfile profile,
        int count,
        string expected)
    {
        ReviewingPanePresentationPlanner.BuildCountText(count, profile).Should().Be(expected);
    }

    [Fact]
    public void Compact_profile_normalizes_snippet_and_uses_wpf_author_and_kind_wording()
    {
        var entry = Entry(
            RevisionEntryKind.Formatting,
            author: " ",
            dateXml: "2026-08-10T14:30:00Z",
            text: "  first\r\nsecond  ");

        var presentation = ReviewingPanePresentationPlanner.BuildRevision(
            entry,
            ReviewingPanePresentationProfile.CompactWpf);

        presentation.KindLabel.Should().Be("Formatted");
        presentation.AuthorText.Should().Be("Unknown");
        presentation.CaptionText.Should().Be("Unknown \u2022 Formatted");
        presentation.SnippetText.Should().Be("first  second");
        presentation.DateText.Should().Be("2026-08-10");
    }

    [Fact]
    public void Detailed_profile_preserves_live_avalonia_row_semantics()
    {
        var text = new string('x', 61);
        var entry = Entry(
            RevisionEntryKind.Insertion,
            author: null,
            dateXml: "2026-08-10T14:30:00Z",
            text);

        var presentation = ReviewingPanePresentationPlanner.BuildRevision(
            entry,
            ReviewingPanePresentationProfile.DetailedAvalonia);

        presentation.KindLabel.Should().Be("Insertion");
        presentation.AuthorText.Should().Be("Unknown");
        presentation.CaptionText.Should().Be(ReviewingPaneRowPlanner.Build(entry).Title);
        presentation.SnippetText.Should().Be(text);
        presentation.DateText.Should().Be("2026-08-10");
        presentation.AcceptToolTip.Should().Be("Accept this insertion change");
        presentation.RejectToolTip.Should().Be("Reject this insertion change");
    }

    private static RevisionEntry Entry(
        RevisionEntryKind kind,
        string? author,
        string? dateXml,
        string text)
    {
        var paragraph = new Paragraph();
        var run = new Run(text);
        paragraph.Runs.Add(run);
        return new RevisionEntry(0, kind, author, dateXml, text, paragraph, run);
    }
}

public sealed class ReviewPresentationOwnershipSourceTests
{
    [Fact]
    public void Reviewing_pane_renderers_delegate_text_and_row_projection()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "MainWindow.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "ReviewingPane.cs");

        wpf.Should().Contain("ReviewingPanePresentationPlanner.BuildRevision(");
        avalonia.Should().Contain("ReviewingPanePresentationPlanner.BuildRevision(");
        wpf.Should().NotContain("Content = \"By Sequence\"");
        avalonia.Should().NotContain("Content = \"By Sequence\"");
        wpf.Should().NotContain("entry.Text.Replace(\"\\r\"");
        avalonia.Should().NotContain("private static string KindLabel(");
        avalonia.Should().NotContain("private static string FormatDate(");
    }

    [Fact]
    public void Balloon_renderers_delegate_semantic_palette_and_preview_truncation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpf = Read(root, "freew", "FreeW.App.Host", "BalloonOverlay.cs");
        var avalonia = Read(root, "freew", "FreeW.App.Avalonia", "ReviewBalloonsPane.cs");

        wpf.Should().Contain("ReviewBalloonStyleCatalog.Resolve(");
        avalonia.Should().Contain("ReviewBalloonStyleCatalog.Resolve(");
        wpf.Should().Contain("ReviewBalloonLayoutPlanner.TruncatePreview(");
        wpf.Should().NotContain("private static string TruncatePreview(");
        avalonia.Should().NotContain("private static IBrush FillFor(");
        avalonia.Should().NotContain("private static IBrush StrokeFor(");
        wpf.Should().NotContain("Color.FromRgb(0xFF, 0xF4, 0xCE)");
        avalonia.Should().NotContain("Color.FromRgb(0xFF, 0xF4, 0xCE)");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(parts.Aggregate(root, Path.Combine));
}
