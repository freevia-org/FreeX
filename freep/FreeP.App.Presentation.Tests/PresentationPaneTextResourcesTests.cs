using FreeP.App.Localization;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPaneTextResourcesTests
{
    [Fact]
    public void Catalog_ExposesStableLocalizedPaneTextAndPlaybackOptions()
    {
        PresentationPaneTextResources.MediaCaptionsHeading.Should().Be("Media Captions");
        PresentationPaneTextResources.BuildMediaCaptionsHeading("Video 1")
            .Should().Be("Media Captions - Video 1");
        PresentationPaneTextResources.BuildAltTextHeading("Title 2")
            .Should().Be("Alt Text - Title 2");
        PresentationPaneTextResources.BuildReadingOrderHeading(1, 3)
            .Should().Be("Reading Order - slide 2 (3 shapes)");
        PresentationPaneTextResources.BuildReadingOrderSelectedMessage("Chart 4")
            .Should().Be("Selected: Chart 4");
        PresentationPaneTextResources.BuildReadingOrderItemTitle(2, "Chart 4")
            .Should().Be("3. Chart 4");
        PresentationPaneTextResources.BuildReadingOrderItemMetadata("Chart", 1)
            .Should().Be("Chart - depth 1");
        PresentationPaneTextResources.BuildProofingHeading(2)
            .Should().Be("Spelling - 2 issues");
        PresentationPaneTextResources.BuildProofingSelectedMessage("Slide 3", "teh", "the")
            .Should().Be("Slide 3: change \"teh\" to \"the\"");
        PresentationPaneTextResources.MediaPlaybackStartOptions.Should().Equal(
            new PresentationMediaPlaybackStartOptionPlan(
                MediaPlaybackStartMode.InClickSequence,
                "On click"),
            new PresentationMediaPlaybackStartOptionPlan(
                MediaPlaybackStartMode.Automatically,
                "Automatically"));

        Loc.GetNeutralResourceKeys().Should().Contain([
            "Pane_MediaCaptions_Heading",
            "Pane_Media_StartOnClick",
            "Pane_AltText_Heading",
            "Pane_ReadingOrder_HeadingFormat",
            "Pane_Proofing_SelectedFormat",
            "Pane_Comments_NewCommentDefault",
            "Pane_Comments_ReplyCommand",
        ]);
    }

    [Fact]
    public void Plans_ExposeRendererReadyHeadingsMessagesAndReadingOrderRows()
    {
        var media = new PresentationMediaCaptionAuthoringPanePlan(
            0,
            42,
            "Video 1",
            -1,
            -1,
            "Ready",
            null!,
            null!,
            null!,
            null!,
            [],
            []);
        var altText = new PresentationAltTextPanePlan(
            true,
            42,
            "Video 1",
            "Suggested",
            null!,
            null!,
            false,
            true,
            PresentationWorkflowCapabilityStatus.Available,
            "Ready",
            []);
        var item = new PresentationReadingOrderItemPlan(
            0,
            2,
            42,
            "Video 1",
            SlideShapeKind.Media,
            "Media",
            string.Empty,
            string.Empty,
            false,
            "Missing alt text",
            true);
        var readingOrder = new PresentationReadingOrderPlan(
            0,
            true,
            true,
            42,
            0,
            [item],
            []);
        var proofing = new PresentationProofingPanePlan(
            true,
            PresentationWorkflowCapabilityStatus.Available,
            1,
            0,
            -1,
            [],
            [],
            "No spelling issues found.");

        media.Heading.Should().Be("Media Captions - Video 1");
        altText.Heading.Should().Be("Alt Text - Video 1");
        readingOrder.Heading.Should().Be("Reading Order - slide 1 (1 shapes)");
        readingOrder.DisplayMessage.Should().Be("Selected: Video 1");
        item.DisplayTitle.Should().Be("1. Video 1");
        item.Metadata.Should().Be("Media - depth 2");
        item.SelectedLabel.Should().Be("Selected item");
        item.SelectionToolTip.Should().Be("Select Video 1");
        proofing.Heading.Should().Be("Spelling - 0 issues");
        proofing.DisplayMessage.Should().Be("No spelling issues found.");
    }

    [Fact]
    public void MainWindowSourceGuards_KeepPaneTextOutOfNativeRenderers()
    {
        var root = FindWorkspaceRoot();
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs")),
        };
        var rendererOwnedLiterals = new[]
        {
            "\"Media Captions\"",
            "\"Playback volume\"",
            "\"Apply playback\"",
            "\"Media bookmarks\"",
            "\"Alt Text\"",
            "\"Reading Order\"",
            "\"Selected item\"",
            "\"Spelling\"",
            "\"Selected issue\"",
            "\"New comment\"",
            "\"New reply\"",
        };

        foreach (var source in sources)
        {
            source.Should().Contain("PresentationPaneTextResources");
            source.Should().Contain("_mediaCaptionPaneHeading.Text = plan.Heading");
            source.Should().Contain("_readingOrderPaneMessage.Text = plan.DisplayMessage");
            source.Should().Contain("Text = item.DisplayTitle");
            source.Should().Contain("Text = item.Metadata");
            source.Should().Contain("item.SelectionToolTip");
            foreach (var literal in rendererOwnedLiterals)
                source.Should().NotContain(literal);
        }
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
