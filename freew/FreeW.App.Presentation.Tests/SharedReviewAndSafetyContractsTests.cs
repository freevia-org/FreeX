using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class SharedReviewAndSafetyContractsTests
{
    [Fact]
    public void PasteSpecialCatalog_PreservesWordOrderAndDescriptions()
    {
        PasteSpecialOptionCatalog.Options.Should().Equal(
            new PasteSpecialOptionChoice(
                "Keep Source Formatting",
                "Paste with the source's character and paragraph formatting.",
                PasteSpecialOption.KeepSourceFormatting),
            new PasteSpecialOptionChoice(
                "Merge Formatting",
                "Paste text with the destination's formatting.",
                PasteSpecialOption.MergeFormatting),
            new PasteSpecialOptionChoice(
                "Keep Text Only",
                "Paste as unformatted plain text.",
                PasteSpecialOption.KeepTextOnly));
    }

    [Fact]
    public void InspectorRemovalChoice_UsesOneAnyContractForBothRenderers()
    {
        new InspectorRemovalChoice(false, false, false, false).Any.Should().BeFalse();
        new InspectorRemovalChoice(false, true, false, false).Any.Should().BeTrue();
    }

    [Fact]
    public void ReviewDisplayState_TransitionsPreserveUnchangedFlagsAndFeedPolicy()
    {
        var state = ReviewDisplayState.Default
            .WithDisplayMode(ReviewDisplayMode.SimpleMarkup)
            .WithShowComments(false)
            .WithShowFormatting(false);

        state.ShowInsertionsAndDeletions.Should().BeTrue();
        state.DisplayMode.Should().Be(ReviewDisplayMode.SimpleMarkup);
        state.ToPolicy().Should().Be(new ReviewDisplayPolicy(
            ReviewDisplayMode.SimpleMarkup,
            ShowInsertionsAndDeletions: true,
            ShowComments: false,
            ShowFormatting: false));

        state.WithShowInsertionsAndDeletions(false).ToPolicy()
            .Should().Be(new ReviewDisplayPolicy(
                ReviewDisplayMode.SimpleMarkup,
                ShowInsertionsAndDeletions: false,
                ShowComments: false,
                ShowFormatting: false));
    }
}
