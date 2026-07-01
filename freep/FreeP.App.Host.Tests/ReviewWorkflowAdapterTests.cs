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
            window.LastLayoutPickerPlan!.Choices.Should().Contain(choice =>
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
    public void FreePRibbonCommands_RegistersSharedReviewWorkflowCommandIds()
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var invoked = false;

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onReviewAccessibility: () => invoked = true);

        registry.TryGet(PresentationReviewWorkflowPlanner.AccessibilityCommandId, out var command)
            .Should()
            .BeTrue("WPF should expose the shared review accessibility intent through its command registry");

        command!.Execute(RibbonCommandContext.Empty);
        invoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out _).Should().BeTrue();
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
