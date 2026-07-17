using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ReviewDisplayPolicyTests
{
    [Theory]
    [InlineData(ReviewDisplayMode.AllMarkup, true, true)]
    [InlineData(ReviewDisplayMode.SimpleMarkup, true, false)]
    [InlineData(ReviewDisplayMode.NoMarkup, true, false)]
    [InlineData(ReviewDisplayMode.Original, false, false)]
    public void Inserted_revision_visibility_and_styling_follow_display_mode(
        ReviewDisplayMode mode,
        bool expectedVisible,
        bool expectedStyled)
    {
        var policy = new ReviewDisplayPolicy(mode);

        var decision = policy.RevisionDecision(RevisionKind.Inserted);

        decision.IsTextVisible.Should().Be(expectedVisible);
        decision.IsRevisionStylingApplied.Should().Be(expectedStyled);
        decision.IsInsertionDecorationApplied.Should().Be(expectedStyled);
        decision.IsDeletionDecorationApplied.Should().BeFalse();
    }

    [Theory]
    [InlineData(ReviewDisplayMode.AllMarkup, true, true)]
    [InlineData(ReviewDisplayMode.SimpleMarkup, false, false)]
    [InlineData(ReviewDisplayMode.NoMarkup, false, false)]
    [InlineData(ReviewDisplayMode.Original, true, false)]
    public void Deleted_revision_visibility_and_styling_follow_display_mode(
        ReviewDisplayMode mode,
        bool expectedVisible,
        bool expectedStyled)
    {
        var policy = new ReviewDisplayPolicy(mode);

        var decision = policy.RevisionDecision(RevisionKind.Deleted);

        decision.IsTextVisible.Should().Be(expectedVisible);
        decision.IsRevisionStylingApplied.Should().Be(expectedStyled);
        decision.IsInsertionDecorationApplied.Should().BeFalse();
        decision.IsDeletionDecorationApplied.Should().Be(expectedStyled);
    }

    [Fact]
    public void Show_insertions_deletions_toggle_suppresses_revision_styling_without_hiding_text()
    {
        var policy = new ReviewDisplayPolicy(
            ReviewDisplayMode.AllMarkup,
            ShowInsertionsAndDeletions: false);

        policy.IsRevisionTextVisible(RevisionKind.Inserted).Should().BeTrue();
        policy.IsRevisionTextVisible(RevisionKind.Deleted).Should().BeTrue();
        policy.ShouldApplyRevisionStyling(RevisionKind.Inserted).Should().BeFalse();
        policy.ShouldApplyRevisionStyling(RevisionKind.Deleted).Should().BeFalse();
    }

    [Fact]
    public void Simple_markup_shows_change_bar_cue_and_uses_final_inline_text()
    {
        var policy = new ReviewDisplayPolicy(ReviewDisplayMode.SimpleMarkup);

        policy.ShouldShowSimpleMarkupChangeBar.Should().BeTrue();
        policy.IsRevisionTextVisible(RevisionKind.Inserted).Should().BeTrue();
        policy.IsRevisionTextVisible(RevisionKind.Deleted).Should().BeFalse();
        policy.ShouldApplyRevisionStyling(RevisionKind.Inserted).Should().BeFalse();
        policy.ShouldApplyRevisionStyling(RevisionKind.Deleted).Should().BeFalse();
    }

    [Theory]
    [InlineData(ReviewDisplayMode.AllMarkup, true)]
    [InlineData(ReviewDisplayMode.SimpleMarkup, true)]
    [InlineData(ReviewDisplayMode.NoMarkup, false)]
    [InlineData(ReviewDisplayMode.Original, false)]
    public void Revision_margin_bars_follow_markup_display_mode(
        ReviewDisplayMode mode,
        bool expected)
    {
        new ReviewDisplayPolicy(mode).ShouldShowRevisionMarginBar.Should().Be(expected);
    }

    [Fact]
    public void Revision_margin_bars_respect_the_insertions_and_deletions_toggle()
    {
        var policy = new ReviewDisplayPolicy(
            ReviewDisplayMode.AllMarkup,
            ShowInsertionsAndDeletions: false);

        policy.ShouldShowRevisionMarginBar.Should().BeFalse();
    }

    [Fact]
    public void Comment_and_formatting_highlights_follow_show_markup_toggles()
    {
        var policy = new ReviewDisplayPolicy(
            ReviewDisplayMode.AllMarkup,
            ShowComments: false,
            ShowFormatting: false);

        policy.ShouldHighlightComments.Should().BeFalse();
        policy.ShouldHighlightFormattingChanges.Should().BeFalse();
    }

    [Theory]
    [InlineData(ReviewDisplayMode.AllMarkup, true, true)]
    [InlineData(ReviewDisplayMode.SimpleMarkup, true, false)]
    [InlineData(ReviewDisplayMode.NoMarkup, false, false)]
    [InlineData(ReviewDisplayMode.Original, false, false)]
    public void Display_mode_controls_comment_and_formatting_markup_chrome(
        ReviewDisplayMode mode,
        bool expectedComments,
        bool expectedFormatting)
    {
        var policy = new ReviewDisplayPolicy(mode);

        policy.ShouldHighlightComments.Should().Be(expectedComments);
        policy.ShouldHighlightFormattingChanges.Should().Be(expectedFormatting);
    }

    [Fact]
    public void Hidden_revisions_are_reported_as_hidden_but_preserved()
    {
        var noMarkup = new ReviewDisplayPolicy(ReviewDisplayMode.NoMarkup);
        var original = new ReviewDisplayPolicy(ReviewDisplayMode.Original);

        noMarkup.RevisionDecision(RevisionKind.Deleted).IsHiddenButPreserved.Should().BeTrue();
        original.RevisionDecision(RevisionKind.Inserted).IsHiddenButPreserved.Should().BeTrue();
        noMarkup.RevisionDecision(RevisionKind.None).IsHiddenButPreserved.Should().BeFalse();
    }
}
