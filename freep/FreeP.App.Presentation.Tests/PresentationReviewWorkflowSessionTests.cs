using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationReviewWorkflowSessionTests
{
    [Fact]
    public void CommentMutation_RefreshesSharedPlansInCallbackOrderAndPreservesSelection()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Review";
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var callbacks = new List<string>();
        var session = CreateSession(editor, callbacks);

        var mutation = session.AddComment("New comment", author: "Alice", initials: "AL");

        mutation.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.AddComment,
            true,
            0,
            null,
            presentation.Slides[0].Comments[0],
            null));
        session.SelectedCommentIndex.Should().Be(0);
        session.LastCommentPanePlan!.SelectedCommentIndex.Should().Be(0);
        callbacks.Should().ContainInOrder(
            "dirty",
            "comment-pane",
            "accessibility",
            "alt-text",
            "proofing-pane",
            "comment-updated");
    }

    [Fact]
    public void CommentMentionInput_NormalizesRendererCaretAndRoutesEditThroughSession()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice Writer",
            Initials = "AW",
            Text = "Please ask @No",
            Idx = 1
        });
        presentation.Slides[0].Comments.Add(new SlideComment
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "Available for review.",
            Idx = 2
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = CreateSession(editor, []);
        session.SetSelectedReviewCommentIndex(0);

        var picker = session.BuildCommentMentionPickerPlanForInput("Please ask @No", 0);
        var candidate = picker.Candidates.Should().ContainSingle().Subject;
        var result = session.ApplyCommentMention(
            PresentationReviewWorkflowIntentKind.EditComment,
            "Please ask @No",
            0,
            candidate);

        picker.Query.Should().Be("No");
        candidate.DisplayName.Should().Be("Nora Reviewer");
        result.InsertionPlan.ShouldApply.Should().BeTrue();
        result.InsertionPlan.UpdatedText.Should().Be("Please ask @Nora.Reviewer ");
        result.MutationPlan!.ShouldApply.Should().BeTrue();
        presentation.Slides[0].Comments[0].Text.Should().Be("Please ask @Nora.Reviewer");
        session.LastCommentMentionPickerPlan.Should().BeSameAs(picker);
        session.LastCommentMentionInsertionPlan.Should().BeSameAs(result.InsertionPlan);
    }

    [Fact]
    public void AltTextMutation_UpdatesShapeAndSharedAltTextPlans()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape { Id = 7, Name = "Chart" };
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        var session = CreateSession(editor, []);

        var mutation = session.ApplySelectedShapeAlternativeText(
            "Quarterly results by region.",
            "Quarterly results",
            isDecorative: false);

        mutation.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            shape.Id,
            "Quarterly results",
            "Quarterly results by region.",
            false,
            null));
        shape.AlternativeTextTitle.Should().Be("Quarterly results");
        shape.AlternativeText.Should().Be("Quarterly results by region.");
        session.LastAltTextRequestPlan!.CurrentTitle.Should().Be("Quarterly results");
        session.LastAltTextPanePlan!.Description.Value.Should().Be("Quarterly results by region.");
    }

    [Fact]
    public void ReadingOrderMutation_UsesSharedPlannerAndRefreshesSelection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 1, Name = "Back" });
        slide.Shapes.Add(new SlideShape { Id = 2, Name = "Selected" });
        slide.Shapes.Add(new SlideShape { Id = 3, Name = "Front" });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(2);
        var session = CreateSession(editor, []);

        var mutation = session.ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

        mutation.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            true,
            0,
            2,
            1,
            2,
            null));
        slide.Shapes.Select(shape => shape.Id).Should().Equal(1u, 3u, 2u);
        editor.SelectedShapeIds.Should().Equal(2u);
        session.LastReadingOrderPlan!.SelectedItem!.ShapeId.Should().Be(2u);
        session.LastReadingOrderPlan.Items.Select(item => item.ShapeId).Should().Equal(1u, 3u, 2u);
    }

    [Fact]
    public void PaneTransitions_PresentAndRefreshReadingOrderAndProofingThroughCallbacks()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Intro eror";
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 1, Name = "Back" });
        presentation.Slides[0].Shapes.Add(new SlideShape { Id = 2, Name = "Front" });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(1);
        var callbacks = new List<string>();
        var session = CreateSession(editor, callbacks);

        session.ShowReadingOrderPane();
        callbacks.Should().ContainSingle(entry => entry == "reading-order-presented");

        session.ApplyReadingOrderMove(PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);
        callbacks.Should().ContainSingle(entry => entry == "reading-order-pane");

        session.ShowProofingPane();
        callbacks.Should().ContainSingle(entry => entry == "proofing-presented");
        session.SelectProofingIssueRow(0);
        callbacks.Count(entry => entry == "proofing-presented").Should().Be(2);
    }

    [Fact]
    public void ProofingMutation_NormalizesSelectionAfterCorrectionAndTracksIgnoreAndDictionaryUpdates()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror";
        slide.Shapes.Add(new SlideShape { Id = 4, Name = "Caption", Text = "Teh caption" });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var session = CreateSession(editor, []);

        session.RefreshProofingRequestPlan();
        session.SelectProofingIssueRow(1).SelectedRow!.Text.Should().Be("Teh");
        session.ApplySelectedProofingCorrection().Should().BeEquivalentTo(
            new PresentationProofingCorrectionMutationPlan(
                true,
                session.LastProofingExecutionPlan!.Scopes.Single(scope => scope.ShapeId == 4) with
                {
                    Text = "Teh caption",
                    Snippet = "Teh caption"
                },
                0,
                3,
                "The",
                "The caption",
                null));
        session.LastProofingPanePlan!.SelectedRowIndex.Should().Be(0);
        session.LastProofingPanePlan.SelectedRow!.Text.Should().Be("eror");

        var ignoredPresentation = Presentation.CreateEmpty();
        ignoredPresentation.Slides[0].Title = "Intro eror";
        ignoredPresentation.Slides[0].Shapes.Add(new SlideShape { Id = 4, Text = "Teh caption" });
        var ignoredEditor = new EditingSession(
            ignoredPresentation,
            new PresentationCommandBus(ignoredPresentation));
        var ignoredSession = CreateSession(ignoredEditor, []);
        ignoredSession.RefreshProofingRequestPlan();
        ignoredSession.SelectProofingIssueRow(1);
        ignoredSession.IgnoreSelectedProofingIssue().Rows.Select(row => row.Text).Should().Equal("eror");
        ignoredSession.ProofingIgnoreState.IgnoredIssues.Should().ContainSingle();
        ignoredSession.SelectProofingIssueRow(0).SelectedRow!.Text.Should().Be("eror");
        ignoredSession.AddSelectedProofingWordToDictionary().Rows.Should().BeEmpty();
        ignoredSession.ProofingDictionaryState.NormalizedWords.Should().ContainSingle().Which.Should().Be("EROR");
        ignoredSession.LastProofingPanePlan!.Message.Should().Be(PresentationReviewWorkflowPlanner.ProofingNoIssuesMessage);
    }

    [Fact]
    public void MainWindowSourceGuards_KeepReviewInputAndPaneOrchestrationInSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("_reviewWorkflowSession.BuildCommentMentionPickerPlanForInput(");
            source.Should().Contain("_reviewWorkflowSession.ApplyCommentMention(");
            source.Should().Contain("PresentReadingOrderPane:");
            source.Should().Contain("PresentProofingPane:");
            source.Should().Contain("=> _reviewWorkflowSession.ShowReadingOrderPane();");
            source.Should().Contain("=> _reviewWorkflowSession.ShowProofingPane();");
            source.Should().NotContain("ResolveCommentInputCaret(");
            source.Should().NotContain("BuildCommentMentionPickerPlanForInsertionContext(");
            source.Should().NotContain("PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(");
            source.Should().NotContain("if (LastProofingPanePlan is null)");
        }
    }

    private static PresentationReviewWorkflowSession CreateSession(
        EditingSession editor,
        List<string> callbacks)
        => new(
            () => editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: () => callbacks.Add("dirty"),
                RefreshCanvas: () => callbacks.Add("canvas"),
                RefreshNotesPane: () => callbacks.Add("notes"),
                RenderAccessibilityCheckerPaneIfVisible: _ => callbacks.Add("accessibility"),
                PresentAccessibilityCheckerPane: _ => callbacks.Add("accessibility-presented"),
                OpenAltTextPane: () => callbacks.Add("alt-text-opened"),
                OpenHyperlinkDialog: () => callbacks.Add("hyperlink-opened"),
                OpenMediaCaptionPane: () => callbacks.Add("media-captions-opened"),
                RenderCommentPane: _ => callbacks.Add("comment-pane"),
                RenderAltTextPaneIfVisible: _ => callbacks.Add("alt-text"),
                RenderReadingOrderPaneIfVisible: _ => callbacks.Add("reading-order-pane"),
                PresentReadingOrderPane: _ => callbacks.Add("reading-order-presented"),
                RenderProofingPaneIfVisible: _ => callbacks.Add("proofing-pane"),
                PresentProofingPane: _ => callbacks.Add("proofing-presented"),
                UpdateAfterCommentMutation: () => callbacks.Add("comment-updated"),
                UpdateAfterCommentNavigation: () => callbacks.Add("comment-navigated"),
                UpdateAfterProofingCorrection: () => callbacks.Add("proofing-updated")));
}
