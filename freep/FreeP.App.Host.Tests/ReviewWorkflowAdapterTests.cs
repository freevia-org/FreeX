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
            window.LastAccessibilitySummaryPlan!.Issues.Should().Contain(issue =>
                issue.ShapeId == shape.Id && issue.Title == "Alt text needs model support");
            window.LastAltTextRequestPlan.Should().NotBeNull();
            window.LastAltTextRequestPlan!.Should().Be(new PresentationAltTextRequestPlan(
                true,
                shape.Id,
                "Product image",
                "Product image",
                string.Empty,
                false,
                PresentationWorkflowCapabilityStatus.Deferred,
                PresentationReviewWorkflowPlanner.AltTextModelDeferredMessage));
            window.LastProofingRequestPlan.Should().NotBeNull();
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
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(_presentation)");
        source.Should().Contain("LastCommentPanePlan = plan;");
        source.Should().NotContain("Modern resolved-thread state is not modeled yet.\";");
        source.Should().NotContain("FreeP shapes do not have a persistent alt-text field yet.\";");
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
