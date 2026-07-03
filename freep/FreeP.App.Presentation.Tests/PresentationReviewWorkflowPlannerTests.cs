using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationReviewWorkflowPlannerTests
{
    [Fact]
    public void BuildCommentPanePlan_DescribesLegacyCommentsAndActions()
    {
        var slides = new[]
        {
            new Slide { Title = "Intro" },
            new Slide { Title = "Review" }
        };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Tighten this message.",
            DateTime = new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            Xemu = 100,
            Yemu = 200,
            Idx = 1
        });
        slides[1].Comments.Add(new SlideComment { Author = "Bob", Initials = "B", Text = "Second slide.", Idx = 1 });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0);

        plan.SlideIndex.Should().Be(0);
        plan.SlideCount.Should().Be(2);
        plan.SlideCommentCount.Should().Be(1);
        plan.TotalCommentCount.Should().Be(2);
        plan.SelectedCommentIndex.Should().Be(0);
        plan.Comments.Should().ContainSingle().Which.Should().Be(new PresentationCommentDescriptor(
            0,
            0,
            1,
            "Alice",
            "AL",
            "Tighten this message.",
            new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            100,
            200,
            true,
            true,
            true,
            true,
            false,
            0,
            0,
            [],
            PresentationCommentThreadStatus.Open,
            true));
        plan.SelectedComment.Should().BeSameAs(plan.Comments[0]);

        Action(PresentationReviewWorkflowPlanner.EditCommentCommandId).IsEnabled.Should().BeTrue();
        Action(PresentationReviewWorkflowPlanner.DeleteCommentCommandId).IsEnabled.Should().BeTrue();
        Action(PresentationReviewWorkflowPlanner.NextCommentCommandId).IsEnabled.Should().BeTrue();
        Action(PresentationReviewWorkflowPlanner.ResolveCommentCommandId).IsEnabled.Should().BeTrue();
        Action(PresentationReviewWorkflowPlanner.ResolveCommentCommandId).Status
            .Should().Be(PresentationWorkflowCapabilityStatus.Available);
        Action(PresentationReviewWorkflowPlanner.ReopenCommentCommandId).DisabledReason
            .Should().Be(PresentationReviewWorkflowPlanner.CommentAlreadyOpenMessage);

        PresentationReviewWorkflowActionPlan Action(string commandId) =>
            plan.Actions.Single(action => action.CommandId == commandId);
    }

    [Fact]
    public void BuildCommentPanePlan_DefaultsToFirstCurrentSlideCommentSelection()
    {
        var slides = new[]
        {
            new Slide { Title = "Intro" },
            new Slide { Title = "Review" }
        };
        slides[0].Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "First", Idx = 1 });
        slides[0].Comments.Add(new SlideComment { Author = "Bob", Initials = "B", Text = "Second", Idx = 2 });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0);

        plan.SelectedCommentIndex.Should().Be(0);
        plan.SelectedComment.Should().BeSameAs(plan.Comments[0]);
        plan.Comments.Select(comment => comment.IsSelected).Should().Equal(true, false);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.EditCommentCommandId)
            .IsEnabled.Should().BeTrue();
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.PreviousCommentCommandId)
            .DisabledReason.Should().Be("No previous comment.");
    }

    [Fact]
    public void BuildCommentPanePlan_InvalidSelection_DisablesEditDelete()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment { Text = "Existing", Idx = 1 });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 5);

        plan.SelectedCommentIndex.Should().Be(-1);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.EditCommentCommandId)
            .IsEnabled.Should().BeFalse();
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);
    }

    [Fact]
    public void BuildCommentNavigationPlan_TargetsAdjacentThreadsAcrossSlides()
    {
        var slides = new[]
        {
            new Slide { Title = "Intro" },
            new Slide { Title = "Empty" },
            new Slide { Title = "Review" }
        };
        slides[0].Comments.Add(new SlideComment { Author = "Alice", Text = "First", Idx = 1 });
        slides[0].Comments.Add(new SlideComment { Author = "Bob", Text = "Second", Idx = 2 });
        slides[2].Comments.Add(new SlideComment { Author = "Nora", Text = "Third", Idx = 1 });

        var sameSlideNext = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            slides,
            0,
            0,
            PresentationReviewWorkflowIntentKind.NextComment);
        var crossSlideNext = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            slides,
            0,
            1,
            PresentationReviewWorkflowIntentKind.NextComment);
        var previousAcrossEmptySlide = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            slides,
            1,
            null,
            PresentationReviewWorkflowIntentKind.PreviousComment);
        var nextAcrossEmptySlide = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            slides,
            1,
            null,
            PresentationReviewWorkflowIntentKind.NextComment);
        var noNext = PresentationReviewWorkflowPlanner.BuildCommentNavigationPlan(
            slides,
            2,
            0,
            PresentationReviewWorkflowIntentKind.NextComment);
        var middlePane = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 1);

        sameSlideNext.Should().Be(new PresentationCommentNavigationPlan(
            PresentationReviewWorkflowIntentKind.NextComment,
            true,
            0,
            0,
            0,
            1,
            null));
        crossSlideNext.Should().Be(new PresentationCommentNavigationPlan(
            PresentationReviewWorkflowIntentKind.NextComment,
            true,
            0,
            1,
            2,
            0,
            null));
        previousAcrossEmptySlide.TargetSlideIndex.Should().Be(0);
        previousAcrossEmptySlide.TargetCommentIndex.Should().Be(1);
        nextAcrossEmptySlide.TargetSlideIndex.Should().Be(2);
        nextAcrossEmptySlide.TargetCommentIndex.Should().Be(0);
        noNext.Should().Be(new PresentationCommentNavigationPlan(
            PresentationReviewWorkflowIntentKind.NextComment,
            false,
            2,
            0,
            2,
            0,
            "No next comment."));
        middlePane.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.PreviousCommentCommandId)
            .IsEnabled.Should().BeTrue();
        middlePane.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.NextCommentCommandId)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildAddCommentPlan_ValidatesAndCreatesCommentPayload()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        var timestamp = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        var plan = PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
            slides,
            0,
            "  Add evidence. ",
            " Ada Lovelace ",
            null,
            -10,
            200,
            timestamp);

        plan.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.AddComment,
            true,
            0,
            null,
            new SlideComment
            {
                Author = "Ada Lovelace",
                Initials = "AL",
                Text = "Add evidence.",
                DateTime = timestamp,
                Xemu = 0,
                Yemu = 200,
                Idx = 1
            },
            null));
    }

    [Fact]
    public void TryApplyCommentMutationPlan_AddAndEdit_NormalizesSelection()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        var add = PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
            slides,
            0,
            " Add parity evidence. ",
            "FreeP User",
            null,
            120,
            240);

        var added = PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, add);
        var selectionAfterAdd = PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(slides, add);
        var edit = PresentationReviewWorkflowPlanner.BuildEditCommentPlan(
            slides,
            0,
            0,
            " Edited parity evidence. ",
            initials: "FP");
        var edited = PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, edit);
        var selectionAfterEdit = PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(slides, edit);

        added.Should().BeTrue();
        selectionAfterAdd.Should().Be(0);
        edited.Should().BeTrue();
        selectionAfterEdit.Should().Be(0);
        slides[0].Comments.Should().ContainSingle().Which.Should().Match<SlideComment>(comment =>
            comment.Text == "Edited parity evidence." &&
            comment.Author == "FreeP User" &&
            comment.Initials == "FP" &&
            comment.Xemu == 120 &&
            comment.Yemu == 240);
    }

    [Fact]
    public void BuildCommentMutationPlans_RejectInvalidOrModernOnlyRequests()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "Old", Idx = 4 });
        var resolvedAt = new DateTime(2026, 7, 2, 8, 15, 0, DateTimeKind.Utc);

        var emptyAdd = PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
            slides, 0, " ", "Alice", "AL", 0, 0);
        var edit = PresentationReviewWorkflowPlanner.BuildEditCommentPlan(
            slides, 0, 0, " New text ", initials: "AX");
        var delete = PresentationReviewWorkflowPlanner.BuildDeleteCommentPlan(slides, 0, 0);
        var resolve = PresentationReviewWorkflowPlanner.BuildResolveCommentPlan(
            slides,
            0,
            0,
            resolvedAt,
            "  Reviewer ");

        emptyAdd.Should().Be(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.AddComment,
            false,
            0,
            null,
            null,
            PresentationReviewWorkflowPlanner.EmptyCommentMessage));
        edit.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.EditComment,
            true,
            0,
            0,
            new SlideComment
            {
                Author = "Alice",
                Initials = "AX",
                Text = "New text",
                Idx = 4
            },
            null));
        delete.Should().Be(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.DeleteComment,
            true,
            0,
            0,
            null,
            null));
        resolve.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ResolveComment,
            true,
            0,
            0,
            new SlideComment
            {
                Author = "Alice",
                Initials = "AL",
                Text = "Old",
                DateTime = null,
                IsResolved = true,
                ResolvedDateTime = resolvedAt,
                ResolvedBy = "Reviewer",
                Idx = 4
            },
            null));
    }

    [Fact]
    public void BuildCommentPanePlan_ModelsResolvedThreadsAndReopenAction()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Resolved thread.",
            Idx = 1,
            IsResolved = true,
            ResolvedBy = "Reviewer",
            ResolvedDateTime = new DateTime(2026, 7, 2, 8, 15, 0, DateTimeKind.Utc)
        });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            slides,
            0,
            selectedCommentIndex: 0);
        var reopen = PresentationReviewWorkflowPlanner.BuildReopenCommentPlan(slides, 0, 0);

        plan.Comments.Should().ContainSingle().Which.Should().Match<PresentationCommentDescriptor>(comment =>
            comment.ThreadStatus == PresentationCommentThreadStatus.Resolved &&
            !comment.CanResolve &&
            comment.CanReopen);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ResolveCommentCommandId,
                "Resolve Comment",
                PresentationReviewWorkflowIntentKind.ResolveComment,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                PresentationReviewWorkflowPlanner.CommentAlreadyResolvedMessage));
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReopenCommentCommandId,
                "Reopen Comment",
                PresentationReviewWorkflowIntentKind.ReopenComment,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
        reopen.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ReopenComment,
            true,
            0,
            0,
            new SlideComment
            {
                Author = "Alice",
                Initials = "AL",
                Text = "Resolved thread.",
                Idx = 1,
                IsResolved = false,
                ResolvedDateTime = null,
                ResolvedBy = string.Empty
            },
            null));
    }

    [Fact]
    public void BuildCommentPanePlan_DescribesModernReplyChainsAndReplyActionState()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Please ask @Nora to review.",
            Idx = 1,
            Replies =
            {
                new SlideCommentReply
                {
                    Author = "Nora",
                    Initials = "NO",
                    Text = "@Alice looks good after the chart update.",
                    DateTime = new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc)
                },
                new SlideCommentReply
                {
                    Author = "Alice",
                    Initials = "AL",
                    Text = "Thanks.",
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0);

        var comment = plan.Comments.Single();
        comment.CanReply.Should().BeTrue();
        comment.ReplyCount.Should().Be(2);
        comment.MentionCount.Should().Be(2);
        comment.Replies.Select(reply => reply.TextPreview).Should().Equal(
            "@Alice looks good after the chart update.",
            "Thanks.");
        comment.Replies[0].Should().Be(new PresentationCommentReplyDescriptor(
            0,
            "Nora",
            "NO",
            "@Alice looks good after the chart update.",
            new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc),
            1));
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReplyCommentCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReplyCommentCommandId,
                "Reply",
                PresentationReviewWorkflowIntentKind.ReplyComment,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
    }

    [Fact]
    public void TryApplyCommentMutationPlan_AppendsReplyAndBlocksResolvedThread()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Needs review.",
            Idx = 1
        });
        var timestamp = new DateTime(2026, 7, 2, 11, 0, 0, DateTimeKind.Utc);

        var reply = PresentationReviewWorkflowPlanner.BuildReplyCommentPlan(
            slides,
            0,
            0,
            "  @Alice fixed. ",
            "  Nora Reviewer ",
            null,
            timestamp);

        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, reply).Should().BeTrue();
        slides[0].Comments[0].Replies.Should().ContainSingle().Which.Should().BeEquivalentTo(new SlideCommentReply
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "@Alice fixed.",
            DateTime = timestamp
        });
        PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(slides, reply, 0)
            .Should().Be(0);

        slides[0].Comments[0].IsResolved = true;
        var blocked = PresentationReviewWorkflowPlanner.BuildReplyCommentPlan(
            slides,
            0,
            0,
            "Cannot add",
            "Nora",
            "NO");

        blocked.Should().Be(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.ReplyComment,
            false,
            0,
            0,
            null,
            PresentationReviewWorkflowPlanner.CannotReplyToResolvedCommentMessage));
        PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0)
            .Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReplyCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.CannotReplyToResolvedCommentMessage);
    }

    [Fact]
    public void TryApplyCommentMutationPlan_ResolvesAndReopensSelectedComment()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Resolve me.",
            Idx = 1
        });
        var resolvedAt = new DateTime(2026, 7, 2, 12, 30, 0, DateTimeKind.Utc);

        var resolve = PresentationReviewWorkflowPlanner.BuildResolveCommentPlan(
            slides,
            0,
            0,
            resolvedAt,
            "  FreeP User ");

        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, resolve).Should().BeTrue();
        slides[0].Comments[0].Should().Match<SlideComment>(comment =>
            comment.IsResolved &&
            comment.ResolvedDateTime == resolvedAt &&
            comment.ResolvedBy == "FreeP User" &&
            comment.Text == "Resolve me.");
        PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0)
            .Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.CommentAlreadyResolvedMessage);

        var reopen = PresentationReviewWorkflowPlanner.BuildReopenCommentPlan(slides, 0, 0);

        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, reopen).Should().BeTrue();
        slides[0].Comments[0].Should().Match<SlideComment>(comment =>
            !comment.IsResolved &&
            comment.ResolvedDateTime == null &&
            comment.ResolvedBy == string.Empty);
        PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: null)
            .Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.CommentAlreadyOpenMessage);
    }

    [Fact]
    public void TryApplyCommentMutationPlan_DeletesSelectedCommentAndNormalizesSelection()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment { Author = "Alice", Initials = "AL", Text = "Keep me.", Idx = 1 });
        slides[0].Comments.Add(new SlideComment { Author = "Bob", Initials = "B", Text = "Delete me.", Idx = 2 });

        var delete = PresentationReviewWorkflowPlanner.BuildDeleteCommentPlan(slides, 0, 1);

        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, delete).Should().BeTrue();
        slides[0].Comments.Should().ContainSingle().Which.Text.Should().Be("Keep me.");
        PresentationReviewWorkflowPlanner.NormalizeCommentSelectionAfterMutation(slides, delete, previousSelectedCommentIndex: 1)
            .Should().Be(0);
        PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 1)
            .Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);

        var invalid = PresentationReviewWorkflowPlanner.BuildDeleteCommentPlan(slides, 0, 5);

        invalid.Should().Be(new PresentationCommentMutationPlan(
            PresentationReviewWorkflowIntentKind.DeleteComment,
            false,
            0,
            5,
            null,
            PresentationReviewWorkflowPlanner.MissingCommentMessage));
        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, invalid).Should().BeFalse();
        slides[0].Comments.Should().ContainSingle();
    }

    [Fact]
    public void BuildAltTextRequestPlan_UsesSelectedShapePersistentMetadata()
    {
        var slide = new Slide { Title = "Intro" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeTextTitle = "Sales summary",
            AlternativeText = "Existing sales chart description."
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(
            slide,
            7,
            "  Quarterly sales by region. ",
            "  Regional sales chart ");

        plan.Should().Be(new PresentationAltTextRequestPlan(
            true,
            7,
            "Sales chart",
            "Sales summary",
            "Sales summary",
            "Regional sales chart",
            "Existing sales chart description.",
            "Quarterly sales by region.",
            false,
            true,
            PresentationWorkflowCapabilityStatus.Available,
            "Edit the persistent alt-text description for the selected shape."));
    }

    [Fact]
    public void BuildAltTextRequestPlan_ReportsDecorativeShapeWithoutRequiredDescription()
    {
        var slide = new Slide { Title = "Intro" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Divider flourish",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            IsDecorative = true
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(slide, 9, " ignored ");

        plan.Should().Be(new PresentationAltTextRequestPlan(
            true,
            9,
            "Divider flourish",
            "Divider flourish",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            true,
            true,
            PresentationWorkflowCapabilityStatus.Available,
            "Selected shape is marked decorative and does not require alt text."));
    }

    [Fact]
    public void BuildAltTextPanePlan_DescribesFieldsValidationAndHostNeutralActions()
    {
        var slide = new Slide { Title = "Intro" };
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeTextTitle = "Sales summary"
        });

        var missingDescription = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
            slide,
            7,
            proposedDescription: null);
        var valid = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
            slide,
            7,
            "  Quarterly sales by region. ",
            "  Regional sales chart ");
        var decorative = PresentationReviewWorkflowPlanner.BuildAltTextPanePlan(
            slide,
            7,
            " ignored ",
            " ignored ",
            isDecorative: true);

        missingDescription.Description.Should().Be(new PresentationAltTextPaneFieldPlan(
            PresentationReviewWorkflowPlanner.AltTextDescriptionFieldId,
            "Description",
            string.Empty,
            "Describe the selected object for people who cannot see it.",
            true,
            true,
            PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage));
        missingDescription.CanApply.Should().BeFalse();
        missingDescription.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);

        valid.Title.Should().Be(new PresentationAltTextPaneFieldPlan(
            PresentationReviewWorkflowPlanner.AltTextTitleFieldId,
            "Title",
            "Regional sales chart",
            "Sales summary",
            true,
            false,
            null));
        valid.Description.Value.Should().Be("Quarterly sales by region.");
        valid.CanApply.Should().BeTrue();
        valid.Actions.Select(action => action.CommandId).Should().Equal(
            PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId,
            PresentationReviewWorkflowPlanner.AltTextPaneDecorativeCommandId,
            PresentationReviewWorkflowPlanner.AltTextPaneCloseCommandId);

        decorative.IsDecorative.Should().BeTrue();
        decorative.Title.IsEnabled.Should().BeFalse();
        decorative.Description.IsEnabled.Should().BeFalse();
        decorative.Description.IsRequired.Should().BeFalse();
        decorative.CanApply.Should().BeTrue();
    }

    [Fact]
    public void BuildAltTextMutationPlan_NormalizesTitleDescriptionAndDecorativeState()
    {
        var slide = new Slide { Title = "Intro" };
        slide.Shapes.Add(new SlideShape { Id = 7, Name = "Sales chart" });

        var metadata = PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(
            slide,
            0,
            7,
            "  Quarterly sales by region. ",
            "  Regional sales chart ");
        var decorative = PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(
            slide,
            0,
            7,
            "  ignored ",
            "  ignored ",
            isDecorative: true);

        metadata.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            7,
            "Regional sales chart",
            "Quarterly sales by region.",
            false,
            null));
        decorative.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            7,
            string.Empty,
            string.Empty,
            true,
            null));
    }

    [Fact]
    public void BuildAltTextMutationPlan_NormalizesSelectedShapeDescription()
    {
        var slide = new Slide { Title = "Intro" };
        slide.Shapes.Add(new SlideShape { Id = 7, Name = "Sales chart" });

        var plan = PresentationReviewWorkflowPlanner.BuildAltTextMutationPlan(
            slide,
            0,
            7,
            "  Quarterly sales by region. ");

        plan.Should().Be(new PresentationAltTextMutationPlan(
            true,
            0,
            7,
            string.Empty,
            "Quarterly sales by region.",
            false,
            null));
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_SkipsDecorativeObjects()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro";
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Decorative divider",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            IsDecorative = true
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        plan.Issues.Should().NotContain(issue =>
            issue.ShapeId == 10 && issue.Title == "Alt text missing");
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsMissingTitlesAltTextAndHyperlinkTips()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = string.Empty;
        slide.Notes = TextBody("Speaker note");
        slide.Comments.Add(new SlideComment { Text = "Review this" });
        slide.Shapes.Add(new SlideShape
        {
            Id = 8,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart()
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Website link",
            Hyperlink = new Hyperlink { Url = "https://example.test" }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        plan.SlideCount.Should().Be(1);
        plan.CommentCount.Should().Be(1);
        plan.NotesSlideCount.Should().Be(1);
        var missingTitle = plan.Issues.Single(issue => issue.Title == "Missing slide title" && issue.ShapeId == null);
        var missingAltText = plan.Issues.Single(issue => issue.Title == "Alt text missing" && issue.ShapeId == 8);
        var missingScreenTip = plan.Issues.Single(issue => issue.Title == "Hyperlink ScreenTip missing" && issue.ShapeId == 9);
        missingTitle.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
            PresentationReviewWorkflowPlanner.MissingSlideTitleActionSummary,
            null,
            false));
        missingAltText.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
            PresentationReviewWorkflowPlanner.MissingAltTextActionSummary,
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            true));
        missingScreenTip.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
            PresentationReviewWorkflowPlanner.MissingHyperlinkScreenTipActionSummary,
            PresentationReviewWorkflowPlanner.InsertLinkCommandId,
            true));
        plan.Actions.Select(action => action.CommandId).Should().Contain(new[]
        {
            PresentationReviewWorkflowPlanner.AccessibilityCommandId,
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            PresentationReviewWorkflowPlanner.ProofingCommandId
        });
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsTableDiagnosticsWithSharedActionSummaries()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = string.Empty;
        slide.Shapes.Add(new SlideShape
        {
            Id = 8,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart()
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Website link",
            Hyperlink = new Hyperlink { Url = "https://example.test" }
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 10,
            Name = "Results table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region"), GridSpan = 2 },
                            new TableCell { HMerge = true }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("42") }
                        }
                    }
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        plan.Issues.Select(issue => issue.Title).Should().Equal(
            "Missing slide title",
            "Alt text missing",
            "Hyperlink ScreenTip missing",
            "Table header row missing",
            "Merged or split table cells");
        var missingTitle = plan.Issues.Single(issue => issue.Title == "Missing slide title");
        var missingAltText = plan.Issues.Single(issue => issue.Title == "Alt text missing");
        var missingScreenTip = plan.Issues.Single(issue => issue.Title == "Hyperlink ScreenTip missing");
        var missingHeader = plan.Issues.Single(issue => issue.Title == "Table header row missing");
        var mergedCells = plan.Issues.Single(issue => issue.Title == "Merged or split table cells");

        missingTitle.Should().Match<PresentationAccessibilityIssueDescriptor>(issue =>
            issue.SlideIndex == 0 &&
            issue.ShapeId == null &&
            issue.Severity == PresentationAccessibilityIssueSeverity.Warning);
        missingAltText.Should().Match<PresentationAccessibilityIssueDescriptor>(issue =>
            issue.SlideIndex == 0 &&
            issue.ShapeId == 8 &&
            issue.Severity == PresentationAccessibilityIssueSeverity.Warning);
        missingScreenTip.Should().Match<PresentationAccessibilityIssueDescriptor>(issue =>
            issue.SlideIndex == 0 &&
            issue.ShapeId == 9 &&
            issue.Severity == PresentationAccessibilityIssueSeverity.Info);
        missingHeader.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            10,
            "Table header row missing",
            "Results table does not mark the first row as a header row.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingTableHeaderRowActionSummary,
                null,
                true)));
        mergedCells.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            10,
            "Merged or split table cells",
            "Results table contains merged or split cells that can make table reading order ambiguous.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MergedTableCellsActionSummary,
                null,
                true)));
    }

    [Fact]
    public void BuildAccessibilityCheckerPanePlan_ProjectsOrderedIssuesIntoSelectableRows()
    {
        var presentation = Presentation.CreateEmpty();
        var first = presentation.Slides[0];
        first.Title = string.Empty;
        first.Shapes.Add(new SlideShape
        {
            Id = 8,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart()
        });
        first.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Website link",
            Hyperlink = new Hyperlink { Url = "https://example.test" }
        });
        presentation.Slides.Add(new Slide { Title = "Second slide" });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            presentation,
            summary,
            selectedRowIndex: 1);

        plan.SlideCount.Should().Be(2);
        plan.IssueCount.Should().Be(3);
        plan.SelectedRowIndex.Should().Be(1);
        plan.SelectedRow.Should().BeSameAs(plan.Rows[1]);
        plan.Rows.Select(row => row.Title).Should().Equal(
            "Missing slide title",
            "Alt text missing",
            "Hyperlink ScreenTip missing");
        plan.Rows.Select(row => row.IsSelected).Should().Equal(false, true, false);
        plan.Rows[0].Should().Be(new PresentationAccessibilityCheckerRowPlan(
            0,
            PresentationAccessibilityIssueSeverity.Warning,
            "Slide title",
            0,
            "Slide 1",
            null,
            string.Empty,
            "Missing slide title",
            "PowerPoint accessibility checks expect each slide to have a meaningful title.",
            false,
            "Go to Slide",
            null,
            true,
            false));
        plan.Rows[1].Should().Be(new PresentationAccessibilityCheckerRowPlan(
            1,
            PresentationAccessibilityIssueSeverity.Warning,
            "Alt text",
            0,
            "Slide 1",
            8,
            "Product image",
            "Alt text missing",
            "Product image should have persistent alt text.",
            true,
            "Open Alt Text",
            PresentationReviewWorkflowPlanner.AltTextCommandId,
            true,
            true));
        plan.Rows[2].Should().Be(new PresentationAccessibilityCheckerRowPlan(
            2,
            PresentationAccessibilityIssueSeverity.Info,
            "Hyperlink",
            0,
            "Slide 1",
            9,
            "Website link",
            "Hyperlink ScreenTip missing",
            "Website link has a hyperlink without hover/help text.",
            false,
            "Edit Hyperlink",
            PresentationReviewWorkflowPlanner.InsertLinkCommandId,
            true,
            true));
    }

    [Fact]
    public void BuildAccessibilityCheckerPanePlan_ProjectsTableDiagnosticsAsSelectableRows()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro";
        slide.Shapes.Add(new SlideShape
        {
            Id = 12,
            Name = "Milestone table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = false },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Milestone"), RowSpan = 2 },
                            new TableCell { TextBody = TextBody("Owner") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { VMerge = true },
                            new TableCell { TextBody = TextBody("Design") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            presentation,
            summary,
            selectedRowIndex: 1);

        plan.Rows.Select(row => row.Title).Should().Equal(
            "Table header row missing",
            "Merged or split table cells");
        plan.Rows.Should().AllSatisfy(row =>
        {
            row.Category.Should().Be("Table");
            row.SlideIndex.Should().Be(0);
            row.SlideDisplay.Should().Be("Slide 1");
            row.ShapeId.Should().Be(12);
            row.ShapeName.Should().Be("Milestone table");
            row.ActionLabel.Should().Be("Select Object");
            row.CommandHint.Should().BeNull();
            row.ShouldNavigateToSlide.Should().BeTrue();
            row.ShouldSelectShape.Should().BeTrue();
        });
        plan.SelectedRowIndex.Should().Be(1);
        plan.SelectedRow.Should().BeSameAs(plan.Rows[1]);
    }

    [Fact]
    public void BuildAccessibilityCheckerPanePlan_NormalizesSelectionAndKeepsEmptyState()
    {
        var clean = Presentation.CreateEmpty();
        clean.Slides[0].Title = "Intro";
        var emptySummary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(clean);
        var empty = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            clean,
            emptySummary,
            selectedRowIndex: 2);

        empty.SelectedRowIndex.Should().Be(-1);
        empty.SelectedRow.Should().BeNull();
        empty.Rows.Should().BeEmpty();

        var dirty = Presentation.CreateEmpty();
        dirty.Slides[0].Title = string.Empty;
        var dirtySummary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(dirty);
        var normalized = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            dirty,
            dirtySummary,
            selectedRowIndex: 99);

        normalized.SelectedRowIndex.Should().Be(0);
        normalized.SelectedRow.Should().BeSameAs(normalized.Rows[0]);
    }

    [Fact]
    public void BuildReadingOrderPlan_DescribesCurrentSlideShapesAndSelectedItem()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 5,
            Name = "Title placeholder",
            Kind = SlideShapeKind.AutoShape,
            Text = "Quarterly update",
            AlternativeTextTitle = "Slide title"
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 7,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeTextTitle = "Regional sales",
            AlternativeText = "Quarterly sales by region."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Divider flourish",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            IsDecorative = true,
            Children =
            {
                new SlideShape
                {
                    Id = 10,
                    Name = "Grouped caption",
                    Kind = SlideShapeKind.AutoShape,
                    Text = "Internal caption"
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 2, [7]);

        plan.SlideIndex.Should().Be(2);
        plan.HasSlide.Should().BeTrue();
        plan.HasSingleSelectedItem.Should().BeTrue();
        plan.SelectedShapeId.Should().Be(7);
        plan.SelectedItemIndex.Should().Be(1);
        plan.SelectedItem.Should().NotBeNull();
        plan.Items.Select(item => item.ShapeId).Should().Equal(5u, 7u, 9u, 10u);
        plan.Items.Select(item => item.ReadingOrderIndex).Should().Equal(0, 1, 2, 3);
        plan.Items.Select(item => item.NestingDepth).Should().Equal(0, 0, 0, 1);
        plan.Items[1].Should().Be(new PresentationReadingOrderItemPlan(
            1,
            0,
            7,
            "Sales chart",
            SlideShapeKind.Chart,
            "Chart",
            "Regional sales",
            "Quarterly sales by region.",
            false,
            "Regional sales: Quarterly sales by region.",
            true));
        plan.Items[2].AccessibilitySummary.Should().Be("Decorative");
        plan.Items[3].AccessibilitySummary.Should().Be("No alt text");
        plan.Actions.Select(action => action.CommandId).Should().Equal(
            PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId,
            PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
            PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
            PresentationReviewWorkflowPlanner.ReadingOrderSelectItemCommandId);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
                "Move Later",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderSelectItemCommandId)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildReadingOrderPlan_RequiresExactlyOneSelectedShapeForSelectedItemState()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 7, Name = "Sales chart", Kind = SlideShapeKind.Chart });
        slide.Shapes.Add(new SlideShape { Id = 8, Name = "Product image", Kind = SlideShapeKind.Picture });

        var multiSelection = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [7, 8]);
        var missingSelection = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, []);
        var emptySlide = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(new Slide(), 0, [7]);

        multiSelection.HasSingleSelectedItem.Should().BeFalse();
        multiSelection.SelectedItem.Should().BeNull();
        multiSelection.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingReadingOrderSelectionMessage);
        missingSelection.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingReadingOrderSelectionMessage);
        emptySlide.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderSelectItemCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderSelectItemCommandId,
                "Select Item",
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                PresentationReviewWorkflowPlanner.EmptyReadingOrderMessage));
    }

    [Fact]
    public void BuildReadingOrderPlan_EnablesMovesOnlyWhenSiblingDirectionIsAvailable()
    {
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape { Id = 1, Name = "Back shape" });
        slide.Shapes.Add(new SlideShape { Id = 2, Name = "Middle shape" });
        slide.Shapes.Add(new SlideShape
        {
            Id = 3,
            Name = "Group",
            Kind = SlideShapeKind.Group,
            Children =
            {
                new SlideShape { Id = 4, Name = "Nested child" },
                new SlideShape { Id = 5, Name = "Nested sibling" }
            }
        });

        var first = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [1]);
        var middle = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [2]);
        var last = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [3]);
        var nestedFirst = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [4]);
        var nestedLast = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [5]);

        Action(first, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage));
        Action(first, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId).IsEnabled.Should().BeTrue();
        Action(middle, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId).IsEnabled.Should().BeTrue();
        Action(middle, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId).IsEnabled.Should().BeTrue();
        Action(last, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage);
        Action(nestedFirst, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage));
        Action(nestedFirst, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId).IsEnabled.Should().BeTrue();
        Action(nestedLast, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId).IsEnabled.Should().BeTrue();
        Action(nestedLast, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
                "Move Later",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                false,
                PresentationWorkflowCapabilityStatus.Available,
                PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage));

        static PresentationReviewWorkflowActionPlan Action(PresentationReadingOrderPlan plan, string commandId) =>
            plan.Actions.Single(action => action.CommandId == commandId);
    }

    [Fact]
    public void TryApplyReadingOrderMove_MutatesTopLevelShapeOrderAndPreservesChildren()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        var first = new SlideShape { Id = 1, Name = "Back shape" };
        var middle = new SlideShape { Id = 2, Name = "Middle shape" };
        var group = new SlideShape
        {
            Id = 3,
            Name = "Group",
            Kind = SlideShapeKind.Group,
            Children =
            {
                new SlideShape { Id = 4, Name = "Nested child" },
                new SlideShape { Id = 5, Name = "Nested sibling" }
            }
        };
        slide.Shapes.Add(first);
        slide.Shapes.Add(middle);
        slide.Shapes.Add(group);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(middle.Id);

        var earlier = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(
            editor,
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier);
        var boundary = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(
            editor,
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier);

        earlier.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
            true,
            0,
            middle.Id,
            1,
            0,
            null));
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u, 3u);
        group.Children.Select(shape => shape.Id).Should().Equal(4u, 5u);
        boundary.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
            false,
            0,
            middle.Id,
            -1,
            -1,
            PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage));
        editor.Select(4);
        var nested = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(
            editor,
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);
        var nestedBoundary = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(
            editor,
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

        nested.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            true,
            0,
            4,
            0,
            1,
            null));
        nestedBoundary.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            false,
            0,
            4,
            -1,
            -1,
            PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage));
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u, 3u);
        group.Children.Select(shape => shape.Id).Should().Equal(5u, 4u);
        editor.SelectedShapeIds.Should().Equal(4u);
    }

    [Fact]
    public void BuildProofingRequestPlan_CountsEditableTextAndReadOnlyComments()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro";
        slide.Notes = TextBody("Speaker note");
        slide.Comments.Add(new SlideComment { Text = "Comment text" });
        slide.Shapes.Add(new SlideShape { Id = 4, Text = "Body text" });

        var plan = PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(presentation);

        plan.Should().Be(new PresentationProofingRequestPlan(
            true,
            PresentationWorkflowCapabilityStatus.Available,
            2,
            1,
            1,
            PresentationReviewWorkflowPlanner.ProofingReadyMessage));
    }

    [Fact]
    public void BuildProofingExecutionPlan_EnumeratesSlideTextTablesNotesCommentsAndReplies()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "Body text"
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 9,
            Name = "Results table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Table cell") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("Speaker notes");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment text",
            Replies =
            {
                new SlideCommentReply { Text = "Reply text" }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(
            presentation,
            scope => scope.Text.Contains("eror", StringComparison.Ordinal)
                ? [new PresentationProofingIssueMatch(
                    scope.Text.IndexOf("eror", StringComparison.Ordinal),
                    4,
                    "eror",
                    "Possible misspelling.")]
                : []);

        plan.CanRun.Should().BeTrue();
        plan.Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);
        plan.ScopeCount.Should().Be(6);
        plan.Scopes.Select(scope => scope.Kind).Should().Equal(
            PresentationProofingScopeKind.SlideTitle,
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.TableCellText,
            PresentationProofingScopeKind.SpeakerNotes,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        plan.Scopes[0].Should().Match<PresentationProofingScopeDescriptor>(scope =>
            scope.SlideIndex == 0 &&
            scope.ShapeId == 1 &&
            scope.Text == "Intro eror" &&
            scope.Snippet == "Intro eror");
        plan.Scopes[1].ShapeId.Should().Be(4);
        plan.Scopes[2].Should().Match<PresentationProofingScopeDescriptor>(scope =>
            scope.ShapeId == 9 &&
            scope.TableRowIndex == 0 &&
            scope.TableColumnIndex == 0 &&
            scope.Text == "Table cell");
        plan.Scopes[4].CommentIndex.Should().Be(0);
        plan.Scopes[5].Should().Match<PresentationProofingScopeDescriptor>(scope =>
            scope.CommentIndex == 0 &&
            scope.ReplyIndex == 0 &&
            scope.Text == "Reply text");
        plan.Issues.Should().ContainSingle().Which.Should().Match<PresentationProofingIssueDescriptor>(issue =>
            issue.Scope.Kind == PresentationProofingScopeKind.SlideTitle &&
            issue.Start == 6 &&
            issue.Length == 4 &&
            issue.Text == "eror");
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ProofingCommandId,
                "Spelling",
                PresentationReviewWorkflowIntentKind.RunProofing,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
    }

    [Fact]
    public void BuildProofingExecutionPlan_NoContentDisablesProofingAction()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide());

        var plan = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var request = PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(presentation);

        plan.CanRun.Should().BeFalse();
        plan.ScopeCount.Should().Be(0);
        plan.IssueCount.Should().Be(0);
        plan.Status.Should().Be(PresentationWorkflowCapabilityStatus.Deferred);
        plan.Actions.Single().Should().Be(new PresentationReviewWorkflowActionPlan(
            PresentationReviewWorkflowPlanner.ProofingCommandId,
            "Spelling",
            PresentationReviewWorkflowIntentKind.RunProofing,
            false,
            PresentationWorkflowCapabilityStatus.Deferred,
            PresentationReviewWorkflowPlanner.ProofingNoTextMessage));
        request.Should().Be(new PresentationProofingRequestPlan(
            false,
            PresentationWorkflowCapabilityStatus.Deferred,
            0,
            0,
            0,
            PresentationReviewWorkflowPlanner.ProofingNoTextMessage));
    }

    [Fact]
    public void TryApplyProofingCorrection_UpdatesAllNormalizedProofingScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror deck";
        var body = new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "Body eror text"
        };
        var table = new SlideShape
        {
            Id = 9,
            Name = "Results table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Table eror cell") }
                        }
                    }
                }
            }
        };
        slide.Shapes.Add(body);
        slide.Shapes.Add(table);
        slide.Notes = TextBody("Speaker eror notes");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment eror text",
            Replies =
            {
                new SlideCommentReply { Text = "Reply eror text" }
            }
        });

        var scopes = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation).Scopes;

        var title = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.SlideTitle);
        var shape = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.ShapeText);
        var cell = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.TableCellText);
        var notes = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.SpeakerNotes);
        var comment = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.Comment);
        var reply = ApplyCorrection(presentation, scopes, PresentationProofingScopeKind.CommentReply);

        title.UpdatedText.Should().Be("Intro error deck");
        shape.UpdatedText.Should().Be("Body error text");
        cell.UpdatedText.Should().Be("Table error cell");
        notes.UpdatedText.Should().Be("Speaker error notes");
        comment.UpdatedText.Should().Be("Comment error text");
        reply.UpdatedText.Should().Be("Reply error text");
        slide.Title.Should().Be("Intro error deck");
        body.Text.Should().Be("Body error text");
        table.Table!.Rows[0].Cells[0].TextBody.Should().NotBeNull();
        TextBodyPlainText(table.Table.Rows[0].Cells[0].TextBody!).Should().Be("Table error cell");
        TextBodyPlainText(slide.Notes!).Should().Be("Speaker error notes");
        slide.Comments[0].Text.Should().Be("Comment error text");
        slide.Comments[0].Replies[0].Text.Should().Be("Reply error text");
    }

    [Fact]
    public void TryApplyProofingCorrection_InvalidScopeRangeOrReplacement_NoOpsWithValidationMessage()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror deck";
        var titleScope = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation)
            .Scopes
            .Single(scope => scope.Kind == PresentationProofingScopeKind.SlideTitle);
        var missingSlideScope = titleScope with { SlideIndex = 4 };

        var missingSlide = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            missingSlideScope,
            6,
            4,
            "error");
        var invalidRange = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            titleScope,
            40,
            4,
            "error");
        var emptyReplacement = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            titleScope,
            6,
            4,
            string.Empty);

        missingSlide.Should().Be(new PresentationProofingCorrectionMutationPlan(
            false,
            missingSlideScope,
            6,
            4,
            "error",
            null,
            PresentationReviewWorkflowPlanner.ProofingCorrectionMissingSlideMessage));
        invalidRange.Should().Be(new PresentationProofingCorrectionMutationPlan(
            false,
            titleScope,
            40,
            4,
            "error",
            null,
            PresentationReviewWorkflowPlanner.ProofingCorrectionInvalidRangeMessage));
        emptyReplacement.Should().Be(new PresentationProofingCorrectionMutationPlan(
            false,
            titleScope,
            6,
            4,
            string.Empty,
            null,
            PresentationReviewWorkflowPlanner.ProofingCorrectionEmptyReplacementMessage));
        slide.Title.Should().Be("Intro eror deck");
    }

    private static PresentationProofingCorrectionMutationPlan ApplyCorrection(
        Presentation presentation,
        IReadOnlyList<PresentationProofingScopeDescriptor> scopes,
        PresentationProofingScopeKind kind)
    {
        var scope = scopes.Single(s => s.Kind == kind);
        var start = scope.Text.IndexOf("eror", StringComparison.Ordinal);
        var plan = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            scope,
            start,
            4,
            "error");

        plan.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            scope,
            start,
            4,
            "error",
            scope.Text.Replace("eror", "error", StringComparison.Ordinal),
            null));
        return plan;
    }

    private static TextBody TextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static string TextBodyPlainText(TextBody textBody)
        => string.Join("\n", textBody.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
}
