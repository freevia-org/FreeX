using FreeP.App.Localization;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationPaneTextResourcesTests
{
    [Fact]
    public void AnimationPaneControlSchema_ProjectsStableBehaviorAndFutureEasingControls()
    {
        var schema = AnimationPanePlanner.BuildControlSchema();

        schema.Heading.Should().Be("Animation Pane");
        schema.Controls.Select(control => control.Id).Should().OnlyHaveUniqueItems();
        schema.Controls.Select(control => control.Kind).Should().Equal(
            AnimationPaneControlKind.EffectOptions,
            AnimationPaneControlKind.WheelSpokes,
            AnimationPaneControlKind.Trigger,
            AnimationPaneControlKind.Duration,
            AnimationPaneControlKind.Delay,
            AnimationPaneControlKind.Repeat,
            AnimationPaneControlKind.AutoReverse,
            AnimationPaneControlKind.SmoothStart,
            AnimationPaneControlKind.SmoothEnd,
            AnimationPaneControlKind.MoveEarlier,
            AnimationPaneControlKind.MoveLater,
            AnimationPaneControlKind.RemoveAnimation,
            AnimationPaneControlKind.ParagraphBuild,
            AnimationPaneControlKind.EditMotionPath);

        schema.GetRequired(AnimationPaneControlKind.Trigger).Options.Should().Equal(
            new AnimationPaneControlOptionPlan("on-click", "On Click"),
            new AnimationPaneControlOptionPlan("with-previous", "With Previous"),
            new AnimationPaneControlOptionPlan("after-previous", "After Previous"));
        schema.GetRequired(AnimationPaneControlKind.Repeat).Options.Should().Equal(
            new AnimationPaneControlOptionPlan("1", "1"),
            new AnimationPaneControlOptionPlan("2", "2"),
            new AnimationPaneControlOptionPlan("3", "3"),
            new AnimationPaneControlOptionPlan("4", "4"),
            new AnimationPaneControlOptionPlan("indefinitely", "Indefinitely"));
        schema.GetRequired(AnimationPaneControlKind.Duration).ValidationMessage
            .Should().Be(AnimationPanePlanner.InvalidDurationMessage);
        schema.GetRequired(AnimationPaneControlKind.Delay).ValidationMessage
            .Should().Be(AnimationPanePlanner.InvalidDelayMessage);
        schema.GetRequired(AnimationPaneControlKind.Repeat).ValidationMessage
            .Should().Be(AnimationPanePlanner.InvalidRepeatMessage);
        schema.GetRequired(AnimationPaneControlKind.SmoothStart).ValidationMessage
            .Should().Be(AnimationPanePlanner.InvalidEasingMessage);
        schema.GetRequired(AnimationPaneControlKind.SmoothEnd).ValidationMessage
            .Should().Be(AnimationPanePlanner.InvalidEasingMessage);
    }

    [Fact]
    public void AnimationPaneControlSchema_UsesCompleteLocalizationCatalog()
    {
        var keys = Loc.GetNeutralResourceKeys();

        keys.Should().Contain([
            "Pane_Animation_Heading",
            "Pane_Animation_HeadingFormat",
            "Pane_Animation_EmptyMessage",
            "Pane_Animation_EffectOptions",
            "Pane_Animation_DurationSeconds",
            "Pane_Animation_RepeatIndefinitely",
            "Pane_Animation_SmoothStart",
            "Pane_Animation_SmoothEnd",
            "Pane_Animation_Validation_InvalidDuration",
            "Pane_Animation_Validation_InvalidRepeat",
            "Pane_Animation_Validation_InvalidEasing",
            "Pane_Animation_Playback_PlayFromSelected",
        ]);

        var schema = PresentationPaneTextResources.BuildAnimationPaneControlSchema();
        schema.Heading.Should().Be(Loc.Get("Pane_Animation_Heading"));
        schema.Controls.Should().OnlyContain(control =>
            !control.Label.StartsWith("[[", StringComparison.Ordinal)
            && !control.ToolTip.StartsWith("[[", StringComparison.Ordinal));
        schema.Controls.SelectMany(control => control.Options).Should().OnlyContain(option =>
            !option.Label.StartsWith("[[", StringComparison.Ordinal));
    }

    [Fact]
    public void AnimationPaneRenderers_OnlyConstructNativeWidgetsFromSharedSchema()
    {
        var root = FindWorkspaceRoot();
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "AnimationPane.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs")),
        };
        var sessionSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "AnimationPaneSession.cs"));
        var plannerSource = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "FreeP.App.Presentation",
            "AnimationPanePlanner.cs"));
        var rendererOwnedLiterals = new[]
        {
            "\"Animation Pane\"",
            "\"Effect options\"",
            "\"Wheel spokes\"",
            "\"Trigger\"",
            "\"Duration (seconds)\"",
            "\"Delay (seconds)\"",
            "\"Repeat count\"",
            "\"Indefinitely\"",
            "\"Auto-reverse between repeats\"",
            "\"Smooth start\"",
            "\"Smooth end\"",
            "\"Move earlier\"",
            "\"Move later\"",
            "\"Remove animation\"",
            "\"Edit motion path geometry\"",
        };

        sessionSource.Should().Contain("AnimationPanePlanner.BuildControlSchema()");
        plannerSource.Should().Contain("schema.GetRequired(AnimationPaneControlKind.");
        foreach (var source in sources)
        {
            source.Should().Contain(".ControlSchema.Heading");
            source.Should().Contain(".BuildItemControlPlan(");
            source.Should().NotContain("new[] { \"1\", \"2\", \"3\", \"4\"");
            foreach (var literal in rendererOwnedLiterals)
                source.Should().NotContain(literal);
        }
    }

    [Fact]
    public void Catalog_ExposesStableLocalizedPaneTextAndPlaybackOptions()
    {
        var smartArt = PresentationPaneTextResources.BuildSmartArtTextPaneChrome();

        smartArt.Should().BeEquivalentTo(new PresentationSmartArtTextPaneChromeText(
            "SmartArt Text Pane",
            "Toggle Assistant",
            "Replace picture",
            "Remove picture",
            "Apply",
            "Close",
            [
                new(SmartArtNodeEditKind.AddSiblingAfter, "Add sibling", "Add a sibling row after the selected SmartArt row."),
                new(SmartArtNodeEditKind.AddChild, "Add child", "Add a child row below the selected SmartArt row."),
                new(SmartArtNodeEditKind.Remove, "Remove", "Remove the selected SmartArt row."),
                new(SmartArtNodeEditKind.MoveUp, "Move up", "Move the selected SmartArt row earlier."),
                new(SmartArtNodeEditKind.MoveDown, "Move down", "Move the selected SmartArt row later."),
                new(SmartArtNodeEditKind.Promote, "Promote", "Promote the selected SmartArt row."),
                new(SmartArtNodeEditKind.Demote, "Demote", "Demote the selected SmartArt row."),
                new(SmartArtNodeEditKind.AddAssistant, "Add assistant", "Add an assistant below the selected hierarchy row."),
            ]));
        PresentationPaneTextResources.MediaCaptionsHeading.Should().Be("Media Captions");
        PresentationPaneTextResources.AccessibilityHeading.Should().Be("Accessibility");
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
            "Pane_Accessibility_Heading",
            "Pane_Media_StartOnClick",
            "Common_AltText",
            "Pane_ReadingOrder_HeadingFormat",
            "Pane_Proofing_SelectedFormat",
            "Pane_Comments_NewCommentDefault",
            "Pane_Comments_ReplyCommand",
            "Pane_SmartArt_Heading",
            "Pane_SmartArt_AddAssistant_ToolTip",
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
            "\"SmartArt Text Pane\"",
            "\"Toggle Assistant\"",
            "\"Replace picture\"",
            "\"Remove picture\"",
            "\"Add sibling\"",
            "\"Add child\"",
            "\"Move up\"",
            "\"Move down\"",
            "\"Promote\"",
            "\"Demote\"",
            "\"Add assistant\"",
            "\"Add a sibling row after the selected SmartArt row.\"",
            "\"Add an assistant below the selected hierarchy row.\"",
        };

        foreach (var source in sources)
        {
            source.Should().Contain("PresentationPaneTextResources");
            source.Should().Contain("_mediaCaptionPaneHeading.Text = plan.Heading");
            source.Should().Contain("_readingOrderPaneMessage.Text = plan.DisplayMessage");
            source.Should().Contain("Text = item.DisplayTitle");
            source.Should().Contain("Text = item.Metadata");
            source.Should().Contain("item.SelectionToolTip");
            source.Should().Contain("PresentationPaneTextResources.BuildSmartArtTextPaneChrome()");
            source.Should().Contain("foreach (var action in chrome.OutlineActions)");
            foreach (var literal in rendererOwnedLiterals)
                source.Should().NotContain(literal);
        }
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
