using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class ReviewCommentAndCustomShowSessionTests
{
    [Fact]
    public void CommentMutationService_PreservesMutationAndSelectionBehavior()
    {
        var slide = new Slide { Title = "Review" };
        var slides = new[] { slide };
        var addedAt = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc);

        var added = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.AddComment,
                0,
                null,
                Text: "Initial note",
                Timestamp: addedAt,
                Author: "Alice",
                Initials: "AL",
                Xemu: 120,
                Yemu: 240));

        added.Applied.Should().BeTrue();
        added.SelectedCommentIndex.Should().Be(0);
        slide.Comments.Should().ContainSingle().Which.Should().Match<SlideComment>(comment =>
            comment.Text == "Initial note" &&
            comment.Author == "Alice" &&
            comment.DateTime == addedAt &&
            comment.Xemu == 120 &&
            comment.Yemu == 240);

        var edited = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.EditComment,
                0,
                0,
                Text: "Updated note"));
        edited.Applied.Should().BeTrue();
        edited.SelectedCommentIndex.Should().Be(0);

        var replied = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.ReplyComment,
                0,
                0,
                Text: "Reply",
                Timestamp: addedAt.AddMinutes(1),
                Author: "Bob",
                Initials: "B"));
        replied.Applied.Should().BeTrue();
        slide.Comments[0].Replies.Should().ContainSingle().Which.Text.Should().Be("Reply");

        var resolved = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.ResolveComment,
                0,
                0,
                ResolvedAt: addedAt.AddMinutes(2),
                ResolvedBy: "Alice"));
        resolved.Applied.Should().BeTrue();
        slide.Comments[0].IsResolved.Should().BeTrue();

        var reopened = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.ReopenComment,
                0,
                0));
        reopened.Applied.Should().BeTrue();
        slide.Comments[0].IsResolved.Should().BeFalse();

        var deleted = PresentationCommentMutationService.Apply(
            slides,
            new PresentationCommentMutationRequest(
                PresentationReviewWorkflowIntentKind.DeleteComment,
                0,
                0));
        deleted.Applied.Should().BeTrue();
        deleted.SelectedCommentIndex.Should().BeNull();
        slide.Comments.Should().BeEmpty();
    }

    [Fact]
    public void CustomShowSessionPlanner_ProjectsSelectionLabelsAndReorderState()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "intro", Title = "Intro" });
        presentation.Slides.Add(new Slide { Id = "deep", Title = "Deep dive" });
        presentation.Slides.Add(new Slide { Id = "appendix", Title = string.Empty });
        var show = new PresentationCustomShow { Name = "Executive review" };
        show.SlideIds.Add("appendix");
        show.SlideIds.Add("deep");
        show.SlideIds.Add("missing");
        presentation.CustomShows.Add(show);

        var authoringFromPresentation = SlideShowCustomShowPlanner.BuildAuthoringPlan(presentation);
        var authoring = new SlideShowCustomShowAuthoringPlan(
            new[]
            {
                new SlideShowCustomShowSummary(
                    0,
                    show.Id,
                    show.Name,
                    new[] { "appendix", "deep", "missing" })
            },
            authoringFromPresentation.AvailableSlides);
        var session = SlideShowCustomShowSessionPlanner.BuildPlan(
            authoring,
            new SlideShowCustomShowSessionState(0, 1));

        session.CustomShows.Should().ContainSingle().Which.DisplayText
            .Should().Be("Executive review (3 slides)");
        session.SelectedShow.Should().BeSameAs(authoring.CustomShows[0]);
        session.SelectedSlideIds.Should().Equal("appendix", "deep", "missing");
        session.SelectedSlides.Select(slide => slide.DisplayText)
            .Should().Equal("Slide 3: Slide 3", "Slide 2: Deep dive", "Missing slide: missing");
        session.SelectedSlideIndex.Should().Be(1);
        session.CanStart.Should().BeTrue();
        session.CanMoveUp.Should().BeTrue();
        session.CanMoveDown.Should().BeTrue();

        var reorder = SlideShowCustomShowSessionPlanner.BuildDragReorderPlan(
            session,
            sourceSlideIndex: 1,
            targetDropIndex: 0);
        reorder.IsValid.Should().BeTrue();
        reorder.ShouldApplyMutation.Should().BeTrue();
        reorder.SourceSlideId.Should().Be("deep");
        reorder.TargetSlideIndex.Should().Be(0);
        reorder.SlideIds.Should().Equal("deep", "appendix", "missing");
    }

    [Fact]
    public void HostAdaptersKeepCommentAndCustomShowMutationLogicInPresentationLayer()
    {
        var wpfMainWindow = ReadWorkspaceFile("freep", "FreeP.App.Host", "MainWindow.cs");
        var avaloniaMainWindow = ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var wpfDialog = ReadWorkspaceFile("freep", "FreeP.App.Host", "CustomShowDialog.cs");
        var avaloniaDialog = ReadWorkspaceFile("freep", "FreeP.App.Avalonia", "CustomShowDialog.cs");

        foreach (var source in new[] { wpfMainWindow, avaloniaMainWindow })
        {
            source.Should().Contain("PresentationReviewWorkflowSession");
            source.Should().Contain("_reviewWorkflowSession");
            source.Should().Contain("RenderCommentPane");
            source.Should().Contain("RenderProofingPaneIfVisible");
            source.Should().NotContain("PresentationCommentMutationService.Apply(");
            source.Should().NotContain("BuildAddCommentPlan(");
            source.Should().NotContain("TryApplyCommentMutationPlan(");
            source.Should().Contain("new CustomShowDialog(");
            source.Should().Contain("_customShowSession");
            source.Should().Contain("TryStartCustomSlideShow");
            source.Should().NotContain("BuildCustomShowSessionPlan(");
            source.Should().NotContain("ApplyCustomShowDialogMutation(");
            source.Should().NotContain("internal SlideShowCustomShowMutationResult CreateCustomShow(");
            source.Should().NotContain("internal SlideShowCustomShowMutationResult RenameCustomShow(");
            source.Should().NotContain("internal SlideShowCustomShowMutationResult DeleteCustomShow(");
            source.Should().NotContain("internal SlideShowCustomShowMutationResult UpdateCustomShowSlides(");
            source.Should().NotContain("internal SlideShowCustomShowMutationResult MoveCustomShowSlide(");
        }

        foreach (var source in new[] { wpfDialog, avaloniaDialog })
        {
            source.Should().Contain("SlideShowCustomShowDialogSession");
            source.Should().Contain("customShowSession.CreateDialogSession(");
            source.Should().Contain("_session.Reorder(");
            source.Should().NotContain("MainWindow _host");
            source.Should().NotContain("SlideShowCustomShowSessionPlanner.");
            source.Should().NotContain("SlideShowCustomShowPlanner.");
            source.Should().NotContain("BuildCustomShowSlideDragReorderPlan(");
            source.Should().NotContain("FormatShowListText(");
            source.Should().NotContain("new SlideShowCustomShowDragReorderPlan(");
            source.Should().NotContain("_host.CreateCustomShow(");
            source.Should().NotContain("_host.RenameCustomShow(");
            source.Should().NotContain("_host.UpdateCustomShowSlides(");
            source.Should().NotContain("_host.DeleteCustomShow(");
            source.Should().NotContain("_host.MoveCustomShowSlide(");
        }
    }

    private static string ReadWorkspaceFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(relativeParts);
}
