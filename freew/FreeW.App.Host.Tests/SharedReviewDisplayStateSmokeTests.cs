using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Host.Tests;

public sealed class SharedReviewDisplayStateSmokeTests
{
    [StaFact]
    public void WpfDocumentView_DefaultsToAndTransitionsThroughSharedState()
    {
        var view = new DocumentView();

        view.CurrentReviewDisplayState.Should().Be(ReviewDisplayState.Default);
        view.ApplyDisplayForReview(ReviewDisplayMode.NoMarkup);
        view.ApplyShowMarkupComments(false);

        view.CurrentReviewDisplayState.DisplayMode.Should().Be(ReviewDisplayMode.NoMarkup);
        view.CurrentReviewDisplayState.ShowComments.Should().BeFalse();
        view.CurrentReviewDisplayPolicy.Should().Be(new ReviewDisplayPolicy(
            ReviewDisplayMode.NoMarkup,
            ShowInsertionsAndDeletions: true,
            ShowComments: false,
            ShowFormatting: true));
    }
}
