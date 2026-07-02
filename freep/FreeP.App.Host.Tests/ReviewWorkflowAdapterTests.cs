using System.IO;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class ReviewWorkflowAdapterTests
{
    [StaFact]
    public void MainWindow_ReviewWorkflowPlans_ComeFromSharedPlanner()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Use the shared plan.",
                Idx = 1,
            });

            var shape = new SlideShape
            {
                Id = 427,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Packaging photo",
            };
            window.Editor.CurrentSlide.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            window.RefreshReviewWorkflowPlans();

            window.LastCommentPanePlan.Should().NotBeNull();
            window.LastCommentPanePlan!.TotalCommentCount.Should().Be(1);
            window.LastCommentPanePlan.Actions.Select(action => action.CommandId)
                .Should()
                .Contain(PresentationReviewWorkflowPlanner.CommentsPaneCommandId);
            window.LastAccessibilitySummaryPlan.Should().NotBeNull();
            var missingAltText = window.LastAccessibilitySummaryPlan!.Issues.Single(issue =>
                issue.ShapeId == shape.Id && issue.Title == "Alt text missing");
            missingAltText.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingAltTextActionSummary,
                PresentationReviewWorkflowPlanner.AltTextCommandId,
                true));
            window.LastAltTextRequestPlan.Should().NotBeNull();
            window.LastAltTextRequestPlan!.Should().Be(new PresentationAltTextRequestPlan(
                true,
                shape.Id,
                "Product image",
                "Packaging photo",
                "Packaging photo",
                "Packaging photo",
                string.Empty,
                string.Empty,
                false,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                "Add a persistent alt-text description for the selected shape."));
            window.LastAltTextPanePlan.Should().BeEquivalentTo(
                PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
                    window.Editor.CurrentSlide,
                    shape.Id,
                    proposedDescription: null));
            window.LastAltTextPanePlan!.CanApply.Should().BeFalse();
            window.LastAltTextPanePlan.Actions
                .Single(action => action.CommandId == PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId)
                .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);

            var mutation = window.ApplySelectedShapeAlternativeText(
                "  Product packaging on a white background. ",
                "  Hero packaging photo ");
            mutation.Should().Be(new PresentationAltTextMutationPlan(
                true,
                0,
                shape.Id,
                "Hero packaging photo",
                "Product packaging on a white background.",
                false,
                null));
            shape.AlternativeTextTitle.Should().Be("Hero packaging photo");
            shape.AlternativeText.Should().Be("Product packaging on a white background.");
            shape.IsDecorative.Should().BeFalse();
            window.LastAltTextRequestPlan!.CurrentTitle.Should().Be("Hero packaging photo");
            window.LastAltTextRequestPlan!.CurrentDescription.Should().Be("Product packaging on a white background.");
            window.LastAltTextPanePlan.Should().BeEquivalentTo(
                PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
                    window.Editor.CurrentSlide,
                    shape.Id,
                    "Product packaging on a white background.",
                    "Hero packaging photo"));
            window.LastAltTextPanePlan!.CanApply.Should().BeTrue();
            window.LastAccessibilitySummaryPlan!.Issues.Should().NotContain(issue =>
                issue.ShapeId == shape.Id && issue.Title == "Alt text missing");
            window.LastProofingRequestPlan.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AltTextPane_ShowsSharedPlanAndAppliesThroughPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.ShowAltTextPane();

            window.IsAltTextPaneVisible.Should().BeTrue();
            window.IsAltTextPaneApplyEnabled.Should().BeFalse();
            window.AltTextPaneMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingShapeMessage);

            var shape = new SlideShape
            {
                Id = 428,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Packaging photo",
            };
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            window.ShowAltTextPane();

            window.AltTextPaneTitleLabel.Should().Be("Title");
            window.AltTextPaneDescriptionLabel.Should().Be("Description");
            window.AltTextPaneTitleText.Should().Be("Packaging photo");
            window.AltTextPaneTitlePlaceholder.Should().Be("Packaging photo");
            window.AltTextPaneDescriptionPlaceholder.Should().Be(
                "Describe the selected object for people who cannot see it.");
            window.IsAltTextPaneDecorativeChecked.Should().BeFalse();
            window.IsAltTextPaneApplyEnabled.Should().BeFalse();
            window.LastAltTextPanePlan!.Description.ValidationMessage
                .Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);

            window.SetAltTextPaneInput("Hero packaging photo", string.Empty, isDecorative: false);
            window.IsAltTextPaneApplyEnabled.Should().BeFalse();
            window.SetAltTextPaneInput("  Hero packaging photo  ", "  Product packaging on a white background.  ", isDecorative: false);
            window.IsAltTextPaneApplyEnabled.Should().BeTrue();

            var mutation = window.ApplyAltTextPane();

            mutation.Should().Be(new PresentationAltTextMutationPlan(
                true,
                0,
                shape.Id,
                "Hero packaging photo",
                "Product packaging on a white background.",
                false,
                null));
            shape.AlternativeTextTitle.Should().Be("Hero packaging photo");
            shape.AlternativeText.Should().Be("Product packaging on a white background.");
            shape.IsDecorative.Should().BeFalse();
            window.LastAccessibilitySummaryPlan!.Issues.Should().NotContain(issue =>
                issue.ShapeId == shape.Id && issue.Title == "Alt text missing");

            window.SetAltTextPaneInput("Ignored title", string.Empty, isDecorative: true);
            window.IsAltTextPaneApplyEnabled.Should().BeTrue();
            window.ApplyAltTextPane().Should().Be(new PresentationAltTextMutationPlan(
                true,
                0,
                shape.Id,
                string.Empty,
                string.Empty,
                true,
                null));
            shape.IsDecorative.Should().BeTrue();
            window.HideAltTextPane();
            window.IsAltTextPaneVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_LayoutPickerRequest_RecordsSharedDesignPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.Presentation.Layouts.Add(new SlideLayout
            {
                Id = "rId2",
                Name = "Blank",
                LayoutType = SlideLayoutType.Blank,
                MasterId = window.Editor.Presentation.Masters[0].Id,
                Placeholders =
                {
                    new SlideShape { Id = 211, Placeholder = new Placeholder { Type = PlaceholderType.Title } },
                }
            });

            window.OpenLayoutPicker();

            window.LastLayoutRequestPlan.Should().Be(PresentationDesignCommandPlanner.LayoutPlan);
            window.LastLayoutPickerPlan.Should().NotBeNull();
            window.IsLayoutPickerVisible.Should().BeTrue();
            window.LayoutPickerChoiceButtonCount.Should().Be(2);
            window.LayoutPickerGroupHeaderCount.Should().Be(1);
            window.LayoutPickerThumbnailPlaceholderCount.Should().BeGreaterThan(0);
            window.LayoutPickerCurrentChoiceCount.Should().Be(1);
            window.LastLayoutPickerPlan!.Groups.Should().ContainSingle(group =>
                group.Heading == "Master 1" &&
                group.Choices.Select(choice => choice.LayoutId).SequenceEqual(new[] { "rId1", "rId2" }));
            window.LastLayoutPickerPlan.Choices.Single(choice => choice.LayoutId == "rId1").Chrome.State
                .Should().Be(PresentationLayoutChoiceChromeState.Current);
            window.LastLayoutPickerPlan.Choices.Single(choice => choice.LayoutId == "rId2").ThumbnailPlaceholders
                .Should()
                .ContainSingle(slot => slot.PlaceholderType == PlaceholderType.Title);
            window.LastLayoutPickerPlan.Choices.Should().Contain(choice =>
                choice.LayoutId == "rId2" &&
                choice.DisplayName == "Blank" &&
                choice.LayoutType == SlideLayoutType.Blank &&
                choice.MasterId == "rId1" &&
                choice.MasterDisplayName == "Master 1" &&
                choice.PlaceholderCount == 1 &&
                choice.DisplayOrder == 1);

            window.ApplyLayoutChoice("rId2").Should().BeTrue();
            window.IsLayoutPickerVisible.Should().BeFalse();
            window.Editor.CurrentSlide!.LayoutId.Should().Be("rId2");
            window.LastAppliedLayoutChoice.Should().NotBeNull();
            window.LastAppliedLayoutChoice!.LayoutId.Should().Be("rId2");
            window.LastAppliedLayoutChoice.MasterDisplayName.Should().Be("Master 1");
            window.LastAppliedLayoutChoice.PlaceholderCount.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_TablePickerRequest_ShowsPickerAndAppliesChoice()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var before = window.Editor.CurrentSlide!.Shapes.Count;

            window.OpenTablePicker();

            window.LastTablePickerPlan.Should().NotBeNull();
            window.IsTablePickerVisible.Should().BeTrue();
            window.TablePickerChoiceButtonCount.Should().Be(25);
            window.TablePickerDefaultChoiceCount.Should().Be(1);
            window.LastTablePickerPlan!.Choices.Should().Contain(choice =>
                choice.Rows == 5 &&
                choice.Columns == 4 &&
                choice.Label == "5 x 4 Table");

            window.ApplyTablePickerChoice(5, 4).Should().BeTrue();

            window.IsTablePickerVisible.Should().BeFalse();
            window.Editor.CurrentSlide!.Shapes.Should().HaveCount(before + 1);
            var table = window.Editor.CurrentSlide.Shapes.Last().Table;
            table.Should().NotBeNull();
            table!.Rows.Should().HaveCount(5);
            table.ColumnWidthsEmu.Should().HaveCount(4);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void FreePRibbonCommands_RegistersSharedReviewWorkflowCommandIds()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var invoked = false;
        var altTextInvoked = false;

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onReviewAccessibility: () => invoked = true,
            onReviewAltText: () => altTextInvoked = true);

        registry.TryGet(PresentationReviewWorkflowPlanner.AccessibilityCommandId, out var command)
            .Should()
            .BeTrue("WPF should expose the shared review accessibility intent through its command registry");

        command!.Execute(RibbonCommandContext.Empty);
        invoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out var altTextCommand).Should().BeTrue();
        altTextCommand!.Execute(RibbonCommandContext.Empty);
        altTextInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.CommentsPaneCommandId, out _).Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.ProofingCommandId, out _).Should().BeTrue();
    }

    [Fact]
    public void MainWindow_Source_UsesPlannerForCommentPaneAndReviewState()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildCommentPanePlan(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(_presentation)");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(_presentation)");
        source.Should().Contain("LastCommentPanePlan = plan;");
        source.Should().Contain("onLayoutPicker:     () => OpenLayoutPicker()");
        source.Should().Contain("PresentationDesignCommandPlanner.BuildLayoutPickerPlan(");
        source.Should().Contain("PresentationDesignCommandPlanner.TryApplyLayoutChoice(");
        source.Should().Contain("ShowLayoutPicker(LastLayoutPickerPlan);");
        source.Should().Contain("BuildLayoutChoiceLabel(choice)");
        source.Should().Contain("BuildLayoutChoiceTile(choice)");
        source.Should().Contain("BuildLayoutThumbnail(choice)");
        source.Should().NotContain("Modern resolved-thread state is not modeled yet.\";");
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeP.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
