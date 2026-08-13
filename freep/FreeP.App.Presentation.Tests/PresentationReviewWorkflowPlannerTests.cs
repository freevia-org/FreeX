using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.Core.Model;
using PresentationModel = FreeP.Core.Model.Presentation;

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
        plan.OpenThreadCount.Should().Be(2);
        plan.ResolvedThreadCount.Should().Be(0);
        plan.TotalReplyCount.Should().Be(0);
        plan.TotalMentionCount.Should().Be(0);
        plan.CurrentSlideSummaryLabel.Should().Be("Slide 1: 1 thread");
        plan.DeckSummaryLabel.Should().Be("2 threads: 2 open threads, 0 resolved threads, 0 replies, 0 mentions");
        plan.HeaderSummaryText.Should().Be(
            "Slide 1: 1 thread | 2 threads: 2 open threads, 0 resolved threads, 0 replies, 0 mentions");
        plan.FilterOptionsSummaryText.Should().Be(
            "All: 1 thread | Open: 1 thread | Resolved: 0 threads | Mentions: 0 threads");
        plan.SelectedCommentIndex.Should().Be(0);
        plan.Comments.Should().ContainSingle().Which.Should().BeEquivalentTo(new PresentationCommentDescriptor(
            0,
            0,
            1,
            "Alice",
            "AL",
            "Tighten this message.",
            new DateTime(2026, 7, 1, 9, 30, 0, DateTimeKind.Utc),
            100,
            200,
            "",
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
        plan.SelectedComment!.AuthorDisplayName.Should().Be("Alice");
        plan.SelectedComment.InitialsBadgeText.Should().Be("AL");
        plan.SelectedComment.AuthorIdentityKey.Should().Be("ALICE|AL");
        plan.SelectedComment.ThreadStatusLabel.Should().Be("Open");
        plan.SelectedComment.ThreadStatusSummary.Should().Be("Open");
        plan.SelectedComment.AnchorSummary.Should().Be("Legacy comment anchor at 100,200 EMU");
        plan.SelectedComment.ReplySummary.Should().Be("0 replies");
        plan.SelectedComment.MentionSummary.Should().Be("0 mentions");
        plan.CloseAction.Should().Be(new PresentationReviewSurfaceActionPlan("Close", true));
        plan.SaveEditAction.Should().Be(new PresentationReviewSurfaceActionPlan("Save", true));

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
    public void BuildCommentPanePlan_AppliesSharedAuthorIdentityAndInitialsPolicy()
    {
        var slides = new[] { new Slide { Title = "Intro" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "  Ada Lovelace King  ",
            Initials = "  ",
            Text = "Check the notes.",
            Idx = 1,
            Replies =
            {
                new SlideCommentReply
                {
                    Author = " ",
                    Initials = "",
                    Text = "Reply from imported metadata."
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0);

        var comment = plan.Comments.Single();
        comment.AuthorDisplayName.Should().Be("Ada Lovelace King");
        comment.InitialsBadgeText.Should().Be("ALK");
        comment.AuthorIdentityKey.Should().Be("ADA LOVELACE KING|ALK");
        comment.ThreadStatusLabel.Should().Be("Open");
        comment.ThreadStatusSummary.Should().Be("Open - 1 reply");
        comment.ReplySummary.Should().Be("1 reply");
        var reply = comment.Replies.Single();
        reply.AuthorDisplayName.Should().Be("Unknown reviewer");
        reply.InitialsBadgeText.Should().Be("UR");
        reply.AuthorIdentityKey.Should().Be("UNKNOWN REVIEWER|UR");
        reply.ReplyLabel.Should().Be("Reply 1");
        reply.MentionSummary.Should().Be("0 mentions");
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
    public void BuildCommentPanePlan_ProjectsThreadFiltersAndFilteredSelection()
    {
        var slides = new[] { new Slide { Title = "Review" }, new Slide { Title = "Wrap-up" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Ask @Nora to verify the chart.",
            Idx = 1
        });
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Bob",
            Initials = "BO",
            Text = "Resolved note.",
            Idx = 2,
            IsResolved = true
        });
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Nora",
            Initials = "NO",
            Text = "Follow-up without a mention.",
            Idx = 3,
            Replies =
            {
                new SlideCommentReply
                {
                    Author = "Alice",
                    Initials = "AL",
                    Text = "Looping @Bob in."
                }
            }
        });
        slides[1].Comments.Add(new SlideComment
        {
            Author = "Deck reviewer",
            Text = "Other slide thread.",
            Idx = 1
        });

        var all = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 2);
        var open = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            slides,
            0,
            selectedCommentIndex: 1,
            PresentationCommentPaneFilterKind.Open);
        var resolved = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            slides,
            0,
            selectedCommentIndex: 0,
            PresentationCommentPaneFilterKind.Resolved);
        var mentions = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(
            slides,
            0,
            selectedCommentIndex: 1,
            PresentationCommentPaneFilterKind.Mentions);

        all.FilterKind.Should().Be(PresentationCommentPaneFilterKind.All);
        all.FilteredCommentCount.Should().Be(3);
        all.Filters.Select(filter => filter.Summary).Should().Equal(
            "All: 3 threads",
            "Open: 2 threads",
            "Resolved: 1 thread",
            "Mentions: 2 threads");
        all.Filters.Single(filter => filter.Kind == PresentationCommentPaneFilterKind.All).IsSelected.Should().BeTrue();
        all.FilterSummaryLabel.Should().Be("Showing all threads");
        all.SelectedComment!.CommentIndex.Should().Be(2);
        all.DeckSummaryLabel.Should().Be("4 threads: 3 open threads, 1 resolved thread, 1 reply, 2 mentions");

        open.FilteredCommentCount.Should().Be(2);
        open.Comments.Select(comment => comment.CommentIndex).Should().Equal(0, 2);
        open.SelectedComment!.CommentIndex.Should().Be(0);
        open.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
            .IsEnabled.Should().BeTrue();
        open.FilterSummaryLabel.Should().Be("Showing 2 threads");

        resolved.Comments.Should().ContainSingle().Which.CommentIndex.Should().Be(1);
        resolved.SelectedCommentIndex.Should().Be(0);
        resolved.SelectedComment!.ThreadStatus.Should().Be(PresentationCommentThreadStatus.Resolved);
        resolved.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
            .IsEnabled.Should().BeTrue();

        mentions.Comments.Select(comment => comment.CommentIndex).Should().Equal(0, 2);
        mentions.SelectedComment!.CommentIndex.Should().Be(0);
        mentions.Comments.Select(comment => comment.MentionCount).Should().Equal(1, 1);
    }

    [Fact]
    public void BuildCommentMentionPickerPlan_DeduplicatesAuthorsAndFiltersByQuery()
    {
        var slides = new[] { new Slide { Title = "Review" }, new Slide { Title = "Wrap-up" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "Please verify.",
            Replies =
            {
                new SlideCommentReply
                {
                    Author = "Alice Writer",
                    Initials = "AW",
                    Text = "Taking it."
                }
            }
        });
        slides[1].Comments.Add(new SlideComment
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "Same reviewer appears again."
        });

        var all = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlan(
            slides,
            currentAuthor: "Current Editor",
            currentInitials: "CE");
        var filtered = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlan(slides, "@nor");

        all.Candidates.Select(candidate => candidate.DisplayName)
            .Should().Equal("Alice Writer", "Current Editor", "Nora Reviewer");
        all.Candidates.Select(candidate => candidate.IdentityKey).Should().OnlyHaveUniqueItems();
        all.Candidates.Single(candidate => candidate.DisplayName == "Nora Reviewer").Should().Be(
            new PresentationCommentMentionCandidate(
                "Nora Reviewer",
                "NR",
                "NORA REVIEWER|NR",
                "Nora.Reviewer",
                "Comment author"));
        all.SummaryLabel.Should().Be("3 mention candidates");
        all.ShouldAutoApplyDefaultCandidate.Should().BeFalse();
        all.TriggerLabel.Should().Be("@");

        filtered.Query.Should().Be("nor");
        filtered.Candidates.Should().ContainSingle().Which.DisplayName.Should().Be("Nora Reviewer");
    }

    [Fact]
    public void BuildCommentMentionPickerPlanForInsertionContext_UsesPartialMentionBeforeCaret()
    {
        var slides = new[] { new Slide { Title = "Review" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice Writer",
            Initials = "AW",
            Text = "Please ask @No"
        });
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "Available for review."
        });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentMentionPickerPlanForInsertionContext(
            slides,
            "Please ask @No",
            "Please ask @No".Length);

        plan.Query.Should().Be("No");
        plan.Candidates.Should().ContainSingle().Which.DisplayName.Should().Be("Nora Reviewer");
        plan.DefaultCandidate!.InsertToken.Should().Be("Nora.Reviewer");
        plan.ShouldAutoApplyDefaultCandidate.Should().BeTrue();
        plan.TriggerLabel.Should().Be("@Nora.Reviewer");
    }

    [Fact]
    public void BuildCommentMentionInsertionPlan_ReplacesPartialMentionToken()
    {
        var candidate = new PresentationCommentMentionCandidate(
            "Nora Reviewer",
            "NR",
            "NORA REVIEWER|NR",
            "Nora.Reviewer",
            "Comment author");

        var plan = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            "Please ask @No",
            "Please ask @No".Length,
            candidate);

        plan.Should().Be(new PresentationCommentMentionInsertionPlan(
            true,
            "Please ask @No",
            "Please ask @Nora.Reviewer ",
            "Please ask @Nora.Reviewer ".Length,
            0,
            candidate,
            null));
    }

    [Fact]
    public void BuildCommentMentionInsertionPlan_InsertedTextFlowsThroughAddCommentMutation()
    {
        var slides = new[] { new Slide { Title = "Review" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Nora Reviewer",
            Initials = "NR",
            Text = "Existing thread."
        });
        var candidate = PresentationReviewWorkflowPlanner
            .BuildCommentMentionPickerPlan(slides, "nora")
            .Candidates
            .Single();
        var insertion = PresentationReviewWorkflowPlanner.BuildCommentMentionInsertionPlan(
            "Loop in",
            "Loop in".Length,
            candidate);

        var mutation = PresentationReviewWorkflowPlanner.BuildAddCommentPlan(
            slides,
            0,
            insertion.UpdatedText,
            "Alice Writer",
            "AW",
            10,
            20);
        PresentationReviewWorkflowPlanner.TryApplyCommentMutationPlan(slides, mutation).Should().BeTrue();

        var pane = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 1);
        pane.SelectedComment!.TextPreview.Should().Be("Loop in @Nora.Reviewer");
        pane.SelectedComment.MentionCount.Should().Be(1);
        pane.SelectedComment.MentionDetailSummary.Should().Be("Mentions: @Nora.Reviewer");
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
            comment.ThreadStatusLabel == "Resolved" &&
            comment.ThreadStatusSummary == "Resolved by Reviewer" &&
            comment.ResolvedByDisplayName == "Reviewer" &&
            comment.ResolvedTimestamp == new DateTime(2026, 7, 2, 8, 15, 0, DateTimeKind.Utc) &&
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
        var slides = new[] { new Slide { Title = "Intro" }, new Slide { Title = "Wrap-up" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice",
            Initials = "AL",
            Text = "Please ask @Nora to review.",
            UsesModernCommentSchema = true,
            ModernAnchorKind = "unknownAnchor",
            ModernAnchorXml = """<p188:unknownAnchor xmlns:p188="http://schemas.microsoft.com/office/powerpoint/2018/8/main" />""",
            Xemu = 1200,
            Yemu = 2400,
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
        slides[1].Comments.Add(new SlideComment
        {
            Author = "Nora",
            Initials = "NO",
            Text = "Resolved follow-up for @Alice.",
            IsResolved = true,
            ResolvedBy = "Nora",
            Idx = 1
        });

        var plan = PresentationReviewWorkflowPlanner.BuildCommentPanePlan(slides, 0, selectedCommentIndex: 0);

        plan.TotalCommentCount.Should().Be(2);
        plan.OpenThreadCount.Should().Be(1);
        plan.ResolvedThreadCount.Should().Be(1);
        plan.TotalReplyCount.Should().Be(2);
        plan.TotalMentionCount.Should().Be(3);
        plan.DeckSummaryLabel.Should().Be("2 threads: 1 open thread, 1 resolved thread, 2 replies, 3 mentions");
        var comment = plan.Comments.Single();
        comment.ModernAnchorKind.Should().Be("unknownAnchor");
        comment.AnchorSummary.Should().Be("unknown anchor at 1200,2400 EMU");
        comment.CanReply.Should().BeTrue();
        comment.ReplyCount.Should().Be(2);
        comment.MentionCount.Should().Be(2);
        comment.Replies.Select(reply => reply.TextPreview).Should().Equal(
            "@Alice looks good after the chart update.",
            "Thanks.");
        comment.ThreadStatusSummary.Should().Be("Open - 2 replies");
        comment.ReplySummary.Should().Be("2 replies");
        comment.MentionSummary.Should().Be("2 mentions");
        comment.Mentions.Should().ContainSingle().Which.Should().Be(
            new PresentationCommentMentionDescriptor(0, 11, 5, "Nora", "NORA"));
        comment.MentionDetailSummary.Should().Be("Mentions: @Nora");
        comment.Replies[0].Should().BeEquivalentTo(new PresentationCommentReplyDescriptor(
            0,
            "Nora",
            "NO",
            "@Alice looks good after the chart update.",
            new DateTime(2026, 7, 2, 10, 0, 0, DateTimeKind.Utc),
            1)
        {
            Mentions = [new PresentationCommentMentionDescriptor(0, 0, 6, "Alice", "ALICE")]
        });
        comment.Replies[0].AuthorDisplayName.Should().Be("Nora");
        comment.Replies[0].InitialsBadgeText.Should().Be("NO");
        comment.Replies[0].AuthorIdentityKey.Should().Be("NORA|NO");
        comment.Replies[0].ReplyLabel.Should().Be("Reply 1");
        comment.Replies[0].MentionSummary.Should().Be("1 mention");
        comment.Replies[0].MentionDetailSummary.Should().Be("Mentions: @Alice");
        comment.Replies[1].MentionDetailSummary.Should().Be("No mentions");
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
    public void BuildReplyCommentPlan_ReusesMatchingModernAuthorIdentity()
    {
        var slides = new[] { new Slide { Title = "Modern review" } };
        slides[0].Comments.Add(new SlideComment
        {
            Author = "Alice Reviewer",
            Initials = "AR",
            Text = "Please verify the updated chart.",
            UsesModernCommentSchema = true,
            ModernAuthorId = "{11111111-1111-1111-1111-111111111111}",
            ModernAuthorUserId = "alice@example.com::powerpoint",
            ModernAuthorProviderId = "aad",
            Idx = 1,
            Replies =
            {
                new SlideCommentReply
                {
                    Author = "Bob Reviewer",
                    Initials = "BR",
                    Text = "I will check it.",
                    ModernAuthorId = "{22222222-2222-2222-2222-222222222222}",
                    ModernAuthorUserId = "bob@example.com::powerpoint",
                    ModernAuthorProviderId = "aad"
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildReplyCommentPlan(
            slides,
            0,
            0,
            "  Confirmed after the update. ",
            " bob reviewer ",
            "br",
            new DateTime(2026, 7, 4, 8, 30, 0, DateTimeKind.Utc));

        plan.ShouldApply.Should().BeTrue();
        plan.Comment.Should().NotBeNull();
        plan.Comment!.Replies.Should().HaveCount(2);
        var reply = plan.Comment.Replies[1];
        reply.Should().Match<SlideCommentReply>(value =>
            value.Author == "bob reviewer" &&
            value.Initials == "br" &&
            value.Text == "Confirmed after the update." &&
            value.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
            value.ModernAuthorUserId == "bob@example.com::powerpoint" &&
            value.ModernAuthorProviderId == "aad");
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
            "Existing sales chart description.",
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
            "Chart \"Sales chart\" (clustered column chart) on slide \"Intro\". Summarize the main trend, comparison, or takeaway.",
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
        decorative.Description.Placeholder.Should().BeEmpty();
        decorative.CanApply.Should().BeTrue();
    }

    [Fact]
    public void BuildAltTextRequestPlan_GeneratesDeterministicDescriptionSuggestionsFromMetadata()
    {
        var slide = new Slide { Title = "Quarterly review" };
        var chart = new SlideShape
        {
            Id = 20,
            Name = "Chart 4",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape
            {
                Title = "Revenue by region",
                ChartType = ChartType.BarStacked,
                Categories = { "Q1", "Q2", "Q3", "Q4" },
                Series =
                {
                    new ChartSeries { Name = "North", Values = { 42, 48, 51, 57 } },
                    new ChartSeries { Name = "South", Values = { 39, 41, 45, 49 } },
                    new ChartSeries { Name = "West", Values = { 35, 40, 43, 46 } },
                    new ChartSeries { Name = "East", Values = { 31, 36, 38, 44 } }
                }
            }
        };
        var table = new SlideShape
        {
            Id = 21,
            Name = "Results table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                ColumnWidthsEmu = { 100, 100 },
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody("Revenue") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("$42K") }
                        }
                    }
                }
            }
        };
        var picture = new SlideShape
        {
            Id = 23,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart { ContentType = "image/jpeg" },
            PictureFormat = new PictureFormat
            {
                CropLeft = 0.1,
                Grayscale = true
            },
            PictureFrameGeometry = "roundRect"
        };
        var text = new SlideShape
        {
            Id = 22,
            Name = "Callout",
            Kind = SlideShapeKind.AutoShape,
            Text = "Launch window moves to July."
        };
        slide.Shapes.Add(chart);
        slide.Shapes.Add(table);
        slide.Shapes.Add(picture);
        slide.Shapes.Add(text);

        var chartPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(slide, chart.Id, null);
        var tablePlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(slide, table.Id, null);
        var picturePlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(slide, picture.Id, null);
        var textPlan = PresentationReviewWorkflowPlanner.BuildAltTextRequestPlan(slide, text.Id, null);

        chartPlan.SuggestedDescription.Should().Be(
            "Chart \"Revenue by region\" (stacked bar chart, series \"North\", \"South\", and \"West\", 4 categories including \"Q1\", \"Q2\", and \"Q3\", 16 values) on slide \"Quarterly review\". Summarize the main trend, comparison, or takeaway.");
        chartPlan.ProposedDescription.Should().BeEmpty();
        tablePlan.SuggestedDescription.Should().Be(
            "Table \"Results table\" with 2 rows and 2 columns, headers \"Region\" and \"Revenue\", sample row \"North\" and \"$42K\" on slide \"Quarterly review\". Summarize the key headers, values, and takeaway.");
        picturePlan.SuggestedDescription.Should().Be(
            "Picture \"Product image\" (JPEG image, cropped, grayscale effect, rounded-rectangle frame) on slide \"Quarterly review\". Describe the important visual details and context.");
        textPlan.SuggestedDescription.Should().Be(
            "Text shape \"Launch window moves to July.\" on slide \"Quarterly review\". Describe the visible text or the shape's purpose.");
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
            PresentationReviewWorkflowPlanner.SetSlideTitleCommandId,
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
    public void BuildAccessibilitySummaryPlan_FlagsFilenameLikeAndDuplicateAltText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro";
        slide.Shapes.Add(new SlideShape
        {
            Id = 20,
            Name = "Hero photo",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            AlternativeText = "IMG_2048.JPG"
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 21,
            Name = "North product photo",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            AlternativeText = "Product packaging on a shelf."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 22,
            Name = "South product photo",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            AlternativeText = "  Product packaging   on a shelf.  "
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 23,
            Name = "Decorative duplicate",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart(),
            IsDecorative = true,
            AlternativeText = "Product packaging on a shelf."
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Select(issue => issue.Title).Should().Equal(
            "Filename-like alt text",
            "Duplicate alt text",
            "Duplicate alt text");
        summary.Issues.Select(issue => issue.ShapeId).Should().Equal(20u, 21u, 22u);
        summary.Issues[0].Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            20,
            "Filename-like alt text",
            "Hero photo uses filename-like alt text \"IMG_2048.JPG\".",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.FilenameLikeAltTextActionSummary,
                PresentationReviewWorkflowPlanner.AltTextCommandId,
                true)));
        summary.Issues[1].Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            21,
            "Duplicate alt text",
            "North product photo reuses alt text \"Product packaging on a shelf.\" on 2 non-decorative objects.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.DuplicateAltTextActionSummary,
                PresentationReviewWorkflowPlanner.AltTextCommandId,
                true)));
        pane.Rows.Select(row => row.Category).Should().Equal("Alt text", "Alt text", "Alt text");
        pane.Rows.Select(row => row.ActionLabel).Should().Equal("Open Alt Text", "Open Alt Text", "Open Alt Text");
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsChartsWithoutChartTitles()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro";
        slide.Shapes.Add(new SlideShape
        {
            Id = 11,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeText = "Quarterly sales by region."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 12,
            Name = "Margin chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape { Title = "   " },
            AlternativeText = "Margin trend by quarter."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 13,
            Name = "Revenue chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape { Title = "Revenue by region" },
            AlternativeText = "Revenue by region."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 14,
            Name = "Chart placeholder",
            Kind = SlideShapeKind.Chart,
            AlternativeText = "Chart placeholder."
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Select(issue => issue.Title).Should().Equal(
            "Chart title missing",
            "Chart title missing");
        summary.Issues.Select(issue => issue.ShapeId).Should().Equal(11u, 12u);
        summary.Issues[0].Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            11,
            "Chart title missing",
            "Sales chart does not have a chart title.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingChartTitleActionSummary,
                PresentationReviewWorkflowPlanner.ChartTitleCommandId,
                true)));
        pane.Rows[0].Should().Be(new PresentationAccessibilityCheckerRowPlan(
            0,
            PresentationAccessibilityIssueSeverity.Warning,
            "Chart",
            0,
            "Slide 1",
            11,
            "Sales chart",
            "Chart title missing",
            "Sales chart does not have a chart title.",
            true,
            "Add Chart Title",
            PresentationReviewWorkflowPlanner.ChartTitleCommandId,
            true,
            true));
    }

    [Fact]
    public void ChartTitleMutation_UsesAccessibleNameAndSupportsUndo()
    {
        var presentation = Presentation.CreateEmpty();
        var chart = new SlideShape
        {
            Id = 21,
            Name = "Sales chart",
            Kind = SlideShapeKind.Chart,
            Chart = new ChartShape(),
            AlternativeText = "Quarterly sales by region."
        };
        presentation.Slides[0].Shapes.Add(chart);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var plan = PresentationReviewWorkflowPlanner.BuildChartTitleMutationPlan(presentation, 0, chart.Id);
        plan.ShouldApply.Should().BeTrue();
        plan.Title.Should().Be("Quarterly sales by region");

        var applied = PresentationReviewWorkflowPlanner.TryApplyChartTitleMutation(editor, 0, chart.Id);
        applied.Should().Be(plan);
        chart.Chart!.Title.Should().Be("Quarterly sales by region");
        PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation).Issues
            .Should().NotContain(issue => issue.Title == "Chart title missing");

        editor.Undo();
        chart.Chart.Title.Should().BeNull();
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsVideosWithoutCaptions()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Demo";
        slide.Shapes.Add(new SlideShape
        {
            Id = 15,
            Name = "Product demo video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true },
            AlternativeText = "Product demo walkthrough."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 16,
            Name = "Narration audio",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = false },
            AlternativeText = "Narration track."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 17,
            Name = "Decorative motion background",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true },
            IsDecorative = true
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 18,
            Name = "Training video",
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo
            {
                IsVideo = true,
                CaptionTracks =
                {
                    new MediaCaptionTrackInfo
                    {
                        RelationshipId = "rIdCaption1",
                        Source = "ppt/media/training.vtt",
                        ContentType = "text/vtt",
                        Language = "en-US",
                        Label = "English captions"
                    }
                }
            },
            AlternativeText = "Training walkthrough."
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Should().ContainSingle().Which.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            15,
            "Video captions missing",
            "Product demo video does not expose closed captions or subtitles.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingVideoCaptionsActionSummary,
                PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId,
                true)));
        pane.Rows.Should().ContainSingle().Which.Should().Be(new PresentationAccessibilityCheckerRowPlan(
            0,
            PresentationAccessibilityIssueSeverity.Warning,
            "Media",
            0,
            "Slide 1",
            15,
            "Product demo video",
            "Video captions missing",
            "Product demo video does not expose closed captions or subtitles.",
            true,
            "Open Captions",
            PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId,
            true,
            true));
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsDuplicateSlideTitles()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Quarterly update";
        presentation.Slides.Add(new Slide { Title = "Launch plan" });
        presentation.Slides.Add(new Slide { Title = "  quarterly update  " });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            presentation,
            summary,
            selectedRowIndex: 1);

        var duplicateIssues = summary.Issues
            .Where(issue => issue.Title == "Duplicate slide title")
            .ToArray();
        duplicateIssues.Select(issue => issue.SlideIndex).Should().Equal(0, 2);
        duplicateIssues.Should().AllSatisfy(issue =>
        {
            issue.Severity.Should().Be(PresentationAccessibilityIssueSeverity.Warning);
            issue.ShapeId.Should().BeNull();
            issue.Detail.Should().Be("Slide title \"Quarterly update\" is reused by 2 slides.");
            issue.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.DuplicateSlideTitleActionSummary,
                PresentationReviewWorkflowPlanner.SetSlideTitleCommandId,
                false));
        });

        pane.Rows.Select(row => row.Category).Should().Equal("Slide title", "Slide title");
        pane.Rows.Select(row => row.ActionLabel).Should().Equal("Set Slide Title", "Set Slide Title");
        pane.Rows.Should().OnlyContain(row => row.ShapeId == null);
        pane.Rows.Select(row => row.ShouldSelectShape).Should().Equal(false, false);
        pane.SelectedRow.Should().BeSameAs(pane.Rows[1]);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsTextRunHyperlinksWithoutScreenTips()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Linked references";
        slide.Shapes.Add(new SlideShape
        {
            Id = 11,
            Name = "Reference text",
            TextBody = TextBody("Project notes", new Hyperlink { Url = "https://example.test/notes" })
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 12,
            Name = "Comparison table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = { FirstRow = true },
                ColumnWidthsEmu = { 100, 100 },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Deck", new Hyperlink { TargetSlideId = "256" }) },
                            new TableCell { TextBody = TextBody("Status") }
                        }
                    }
                }
            }
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 13,
            Name = "Documented link",
            TextBody = TextBody("Docs", new Hyperlink
            {
                Url = "https://example.test/docs",
                Tooltip = "Open project documentation"
            })
        });

        var plan = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        var linkIssues = plan.Issues
            .Where(issue => issue.Title == "Hyperlink ScreenTip missing")
            .ToArray();
        linkIssues.Select(issue => issue.ShapeId).Should().Equal(11u, 12u);
        linkIssues.Select(issue => issue.Detail).Should().Equal(
            "Text link in Reference text is missing hover/help text.",
            "Text link in Comparison table is missing hover/help text.");
        linkIssues.Should().AllSatisfy(issue =>
        {
            issue.Severity.Should().Be(PresentationAccessibilityIssueSeverity.Info);
            issue.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingHyperlinkScreenTipActionSummary,
                PresentationReviewWorkflowPlanner.InsertLinkCommandId,
                true));
        });
        plan.Issues.Should().NotContain(issue => issue.ShapeId == 13);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsUnclearTextRunHyperlinks()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Linked references";
        slide.Shapes.Add(new SlideShape
        {
            Id = 21,
            Name = "Vague link",
            TextBody = TextBody(" Click   Here ", new Hyperlink
            {
                Url = "https://example.test/notes",
                Tooltip = "Open project notes"
            })
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 22,
            Name = "Comparison table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = { FirstRow = true },
                ColumnWidthsEmu = { 100, 100 },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell
                            {
                                TextBody = TextBody("https://example.test/results", new Hyperlink
                                {
                                    Url = "https://example.test/results",
                                    Tooltip = "Open published results"
                                })
                            },
                            new TableCell { TextBody = TextBody("Status") }
                        }
                    }
                }
            },
            AlternativeText = "Comparison table with published results."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 23,
            Name = "Descriptive link",
            TextBody = TextBody("Project notes", new Hyperlink
            {
                Url = "https://example.test/notes",
                Tooltip = "Open project notes"
            })
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        var linkIssues = summary.Issues
            .Where(issue => issue.Title == "Unclear hyperlink text")
            .ToArray();
        linkIssues.Select(issue => issue.ShapeId).Should().Equal(21u, 22u);
        linkIssues.Select(issue => issue.Detail).Should().Equal(
            "Text link in Vague link uses non-descriptive display text \"Click Here\".",
            "Text link in Comparison table uses raw URL display text \"https://example.test/results\".");
        linkIssues.Should().AllSatisfy(issue =>
        {
            issue.Severity.Should().Be(PresentationAccessibilityIssueSeverity.Warning);
            issue.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.UnclearHyperlinkTextActionSummary,
                PresentationReviewWorkflowPlanner.InsertLinkCommandId,
                true));
        });
        pane.Rows
            .Where(row => row.Title == "Unclear hyperlink text")
            .Should()
            .AllSatisfy(row =>
            {
                row.Category.Should().Be("Hyperlink");
                row.ActionLabel.Should().Be("Edit Hyperlink");
                row.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.InsertLinkCommandId);
                row.ShouldSelectShape.Should().BeTrue();
            });
        summary.Issues.Should().NotContain(issue => issue.ShapeId == 23);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsLowContrastShapeAndTableText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Contrast checks";
        slide.Background = new ShapeFill.Solid(SrgbColor.White);
        slide.Shapes.Add(new SlideShape
        {
            Id = 31,
            Name = "Muted caption",
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x777777)),
            TextBody = TextBodyWithColor("Muted KPI", SrgbColor.FromRgb(0x777777))
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 32,
            Name = "Readable caption",
            Fill = new ShapeFill.Solid(SrgbColor.White),
            TextBody = TextBodyWithColor("Readable KPI", SrgbColor.Black)
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 33,
            Name = "Solid text fill",
            Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x202020)),
            TextBody = TextBodyWithFill(
                "Solid fill text",
                new ShapeFill.Solid(new ThemeAwareColor(SrgbColor.FromRgb(0x202020))))
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 34,
            Name = "KPI table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                StyleData = new TableStyleData
                {
                    WholeTbl = new TableStyleEntry
                    {
                        TextColor = new ThemeAwareColor(SrgbColor.White)
                    }
                },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell
                            {
                                Fill = new ShapeFill.Solid(SrgbColor.White),
                                TextBody = TextBody("Quarter")
                            }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        var contrastIssues = summary.Issues
            .Where(issue => issue.Title == "Low text contrast")
            .ToArray();
        contrastIssues.Should().HaveCount(3);
        contrastIssues.Select(issue => issue.ShapeId).Should().Equal(31u, 33u, 34u);
        contrastIssues.Select(issue => issue.Detail).Should().Equal(
            "Muted caption text \"Muted KPI\" has 1:1 contrast against its solid background; threshold is 4.5:1.",
            "Solid text fill text \"Solid fill text\" has 1:1 contrast against its solid background; threshold is 4.5:1.",
            "KPI table cell R1C1 text \"Quarter\" has 1:1 contrast against its solid background; threshold is 4.5:1.");
        contrastIssues.Should().AllSatisfy(issue =>
        {
            issue.Severity.Should().Be(PresentationAccessibilityIssueSeverity.Warning);
            issue.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.LowTextContrastActionSummary,
                null,
                true));
        });
        pane.Rows
            .Where(row => row.Title == "Low text contrast")
            .Should()
            .AllSatisfy(row =>
            {
                row.Category.Should().Be("Text contrast");
                row.ActionLabel.Should().Be("Select Object");
                row.CommandHint.Should().BeNull();
                row.ShouldNavigateToSlide.Should().BeTrue();
                row.ShouldSelectShape.Should().BeTrue();
            });
        summary.Issues.Should().NotContain(issue => issue.ShapeId == 32);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_SkipsLowContrastWhenColorsAreComplexAlphaOrInherited()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Skipped contrast checks";
        slide.Shapes.Add(new SlideShape
        {
            Id = 41,
            Name = "No explicit slide background",
            TextBody = TextBodyWithColor("Inherited background", SrgbColor.White)
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Gradient background",
            Fill = new ShapeFill.Gradient(
                new ThemeAwareColor(SrgbColor.White),
                new ThemeAwareColor(SrgbColor.Black)),
            TextBody = TextBodyWithColor("Gradient text", SrgbColor.White)
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 43,
            Name = "Theme foreground",
            Fill = new ShapeFill.Solid(SrgbColor.White),
            TextBody = TextBodyWithColor(
                "Theme text",
                new ThemeAwareColor(
                    SrgbColor.White,
                    new SchemeColorRef { Slot = ThemeColorSlot.Lt1, RoleName = "tx1" }))
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 44,
            Name = "Alpha foreground",
            Fill = new ShapeFill.Solid(SrgbColor.White),
            TextBody = TextBodyWithColor("Alpha text", new ThemeAwareColor(SrgbColor.White, alpha: 128))
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 45,
            Name = "Gradient text fill",
            Fill = new ShapeFill.Solid(SrgbColor.White),
            TextBody = TextBodyWithFill(
                "Complex text",
                new ShapeFill.Gradient(
                    new ThemeAwareColor(SrgbColor.White),
                    new ThemeAwareColor(SrgbColor.Black)))
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 46,
            Name = "Decorative box",
            Fill = new ShapeFill.Solid(SrgbColor.White)
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        summary.Issues.Should().NotContain(issue => issue.Title == "Low text contrast");
    }

    [Fact]
    public void SlideTitleMutationPlan_SuggestsPlaceholderTextAndAppliesThroughEditingSession()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = string.Empty;
        slide.Shapes.Add(new SlideShape
        {
            Id = 21,
            Placeholder = new Placeholder { Type = PlaceholderType.Title },
            Text = "  Quarterly launch plan  "
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        PresentationReviewWorkflowPlanner.BuildSuggestedSlideTitle(presentation, 0)
            .Should().Be("Quarterly launch plan");

        var plan = PresentationReviewWorkflowPlanner.TryApplySlideTitleMutation(editor, 0);

        plan.Should().Be(new PresentationSlideTitleMutationPlan(
            true,
            0,
            "Quarterly launch plan",
            "Quarterly launch plan",
            null));
        slide.Title.Should().Be("Quarterly launch plan");
        PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation)
            .Issues.Should().NotContain(issue => issue.Title == "Missing slide title");
    }

    [Fact]
    public void TableHeaderRowMutationPlan_AppliesThroughEditingSessionAndClearsAccessibilityIssue()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        var table = new SlideShape
        {
            Id = 44,
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
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody("Revenue") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("$42K") }
                        }
                    }
                }
            }
        };
        slide.Shapes.Add(table);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var issue = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation)
            .Issues.Single(issue => issue.Title == "Table header row missing");

        issue.Action.CommandId.Should().Be(PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId);
        var plan = PresentationReviewWorkflowPlanner.TryApplyTableHeaderRowMutation(
            editor,
            issue.SlideIndex,
            issue.ShapeId);

        plan.Should().Be(new PresentationTableHeaderRowMutationPlan(true, 0, 44, null));
        table.Table!.Flags.FirstRow.Should().BeTrue();
        PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation)
            .Issues.Should().NotContain(issue => issue.Title == "Table header row missing");
        editor.Undo();
        table.Table.Flags.FirstRow.Should().BeFalse();
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_DoesNotFlagDeclaredPopulatedTableHeaderRow()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 45,
            Name = "Revenue table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody("Revenue") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("$42K") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        summary.Issues.Should().NotContain(issue => issue.Title == "Table header row missing");
        summary.Issues.Should().NotContain(issue => issue.Title == "Blank table header cells");
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_MissingHeaderRowDoesNotCountCandidateHeaderBlanksAsBodyCells()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 46,
            Name = "Forecast table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell(),
                            new TableCell { TextBody = TextBody(" ") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell()
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        summary.Issues.Select(issue => issue.Title).Should().Equal(
            "Table header row missing",
            "Blank table body cells");
        summary.Issues.Should().NotContain(issue => issue.Title == "Blank table header cells");
        summary.Issues.Single(issue => issue.Title == "Blank table body cells")
            .Detail.Should().Be("Forecast table has 1 blank body cell.");
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
                PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId,
                true)));
        mergedCells.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            10,
            "Merged or split table cells",
            "Results table contains merged or split cells that can make table reading order ambiguous.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MergedTableCellsActionSummary,
                PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId,
                true)));
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsMergedTableCellsOncePerTable()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 17,
            Name = "Coverage table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region"), RowSpan = 2 },
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
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Should().ContainSingle().Which.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            17,
            "Merged or split table cells",
            "Coverage table contains merged or split cells that can make table reading order ambiguous.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MergedTableCellsActionSummary,
                PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId,
                true)));
        pane.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Table" &&
            row.ShapeId == 17 &&
            row.ShapeName == "Coverage table" &&
            row.ActionLabel == "Review Table Structure" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_DoesNotFlagNormalPopulatedTablesAsMerged()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 18,
            Name = "Revenue table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody("Revenue") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("$42K") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        summary.Issues.Should().BeEmpty();
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsBlankTableHeaderCells()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 14,
            Name = "Forecast table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody(" ") },
                            new TableCell()
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody("$42K") },
                            new TableCell { TextBody = TextBody("Green") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Should().ContainSingle().Which.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            14,
            "Blank table header cells",
            "Forecast table has 2 blank header cells.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.BlankTableHeaderCellsActionSummary,
                PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId,
                true)));
        summary.Issues.Should().NotContain(issue => issue.Title == "Table header row missing");
        pane.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Table" &&
            row.ShapeId == 14 &&
            row.ShapeName == "Forecast table" &&
            row.ActionLabel == "Review Table Structure" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_FlagsBlankTableBodyCells()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 15,
            Name = "Variance table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region") },
                            new TableCell { TextBody = TextBody("Plan") },
                            new TableCell { TextBody = TextBody("Actual") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North") },
                            new TableCell { TextBody = TextBody(" ") },
                            new TableCell()
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("South") },
                            new TableCell { TextBody = TextBody("$39K") },
                            new TableCell { TextBody = TextBody("$41K") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(presentation, summary);

        summary.Issues.Should().ContainSingle().Which.Should().Be(new PresentationAccessibilityIssueDescriptor(
            PresentationAccessibilityIssueSeverity.Warning,
            0,
            15,
            "Blank table body cells",
            "Variance table has 2 blank body cells.",
            new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.BlankTableBodyCellsActionSummary,
                PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId,
                true)));
        summary.Issues.Should().NotContain(issue => issue.Title == "Blank table header cells");
        pane.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
            row.Category == "Table" &&
            row.ShapeId == 15 &&
            row.ShapeName == "Variance table" &&
            row.ActionLabel == "Review Table Structure" &&
            row.CommandHint == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId &&
            row.ShouldNavigateToSlide &&
            row.ShouldSelectShape);
    }

    [Fact]
    public void BuildAccessibilitySummaryPlan_DoesNotDoubleReportBlankHeaderCellsAsBodyCells()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 16,
            Name = "Staffing table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Team") },
                            new TableCell(),
                            new TableCell { TextBody = TextBody("Notes") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Support") },
                            new TableCell { TextBody = TextBody("5") },
                            new TableCell { TextBody = TextBody("Covered") }
                        }
                    }
                }
            }
        });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);

        summary.Issues.Select(issue => issue.Title).Should().Equal("Blank table header cells");
        summary.Issues.Should().NotContain(issue => issue.Title == "Blank table body cells");
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
            "Set Slide Title",
            PresentationReviewWorkflowPlanner.SetSlideTitleCommandId,
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
            row.ShouldNavigateToSlide.Should().BeTrue();
            row.ShouldSelectShape.Should().BeTrue();
        });
        plan.Rows[0].ActionLabel.Should().Be("Set Header Row");
        plan.Rows[0].CommandHint.Should().Be(PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId);
        plan.Rows[1].ActionLabel.Should().Be("Review Table Structure");
        plan.Rows[1].CommandHint.Should().Be(PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);
        plan.SelectedRowIndex.Should().Be(1);
        plan.SelectedRow.Should().BeSameAs(plan.Rows[1]);
    }

    [Fact]
    public void BuildTableStructureReviewPlan_ListsBlankAndMergedTableDetails()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Quarterly review";
        slide.Shapes.Add(new SlideShape
        {
            Id = 19,
            Name = "Forecast table",
            Kind = SlideShapeKind.Table,
            Table = new TableShape
            {
                Flags = new TableStyleFlags { FirstRow = true },
                Rows =
                {
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("Region"), GridSpan = 2 },
                            new TableCell { HMerge = true },
                            new TableCell()
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { TextBody = TextBody("North"), RowSpan = 2 },
                            new TableCell(),
                            new TableCell { TextBody = TextBody("$42K") }
                        }
                    },
                    new TableRow
                    {
                        Cells =
                        {
                            new TableCell { VMerge = true },
                            new TableCell { TextBody = TextBody("$39K") },
                            new TableCell()
                        }
                    }
                }
            }
        });

        var plan = PresentationReviewWorkflowPlanner.BuildTableStructureReviewPlan(presentation, 0, 19);

        plan.Should().Match<PresentationTableStructureReviewPlan>(review =>
            review.CanReview &&
            review.SlideIndex == 0 &&
            review.ShapeId == 19 &&
            review.TableName == "Forecast table" &&
            review.RowCount == 3 &&
            review.ColumnCount == 3 &&
            review.ShouldNavigateToSlide &&
            review.ShouldSelectTable &&
            review.ValidationMessage == null);
        plan.BlankHeaderCells.Should().Equal(new[]
        {
            new PresentationTableStructureCellPlan(0, 2, "R1C3")
        });
        plan.BlankBodyCells.Should().Equal(new[]
        {
            new PresentationTableStructureCellPlan(1, 1, "R2C2"),
            new PresentationTableStructureCellPlan(2, 2, "R3C3")
        });
        plan.MergedOrSplitCells.Should().Equal(new[]
        {
            new PresentationTableStructureSpanPlan(0, 0, "R1C1", 2, 1, false, false, "R1C1 spans 2 columns."),
            new PresentationTableStructureSpanPlan(0, 1, "R1C2", 1, 1, true, false, "R1C2 continues a horizontal merge."),
            new PresentationTableStructureSpanPlan(1, 0, "R2C1", 1, 2, false, false, "R2C1 spans 2 rows."),
            new PresentationTableStructureSpanPlan(2, 0, "R3C1", 1, 1, false, true, "R3C1 continues a vertical merge.")
        });
        plan.Guidance.Should().Be(PresentationReviewWorkflowPlanner.TableStructureReviewGuidance);

        var display = PresentationReviewWorkflowPlanner.BuildTableStructureReviewDisplayPlan(plan);

        display.Should().Match<PresentationTableStructureReviewDisplayPlan>(review =>
            review.CanReview &&
            review.Heading == "Review Table Structure" &&
            review.Summary == "Forecast table: 3 rows, 3 columns. 1 blank header cell, 2 blank body cells, 4 merged or split cells." &&
            review.Guidance == PresentationReviewWorkflowPlanner.TableStructureReviewGuidance &&
            review.ValidationMessage == null);
        display.Details.Should().Equal(new[]
        {
            new PresentationTableStructureReviewDetailRowPlan(
                "Blank header cell",
                "R1C3 is blank.",
                "Add descriptive header text or remove the empty header cell."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Blank body cell",
                "R2C2 is blank.",
                "Confirm the blank data cell is intentional or add visible text."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Blank body cell",
                "R3C3 is blank.",
                "Confirm the blank data cell is intentional or add visible text."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R1C1 spans 2 columns.",
                "Verify the table still reads correctly in row and column order."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R1C2 continues a horizontal merge.",
                "Verify the table still reads correctly in row and column order."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R2C1 spans 2 rows.",
                "Verify the table still reads correctly in row and column order."),
            new PresentationTableStructureReviewDetailRowPlan(
                "Merged or split cell",
                "R3C1 continues a vertical merge.",
                "Verify the table still reads correctly in row and column order.")
        });
        display.Details[0].RenderedLine.Should().Be(
            "Blank header cell: R1C3 is blank. Add descriptive header text or remove the empty header cell.");
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
    public void BuildAccessibilityCheckerNavigationPlan_NormalizesRequestedRowsForHostAdapters()
    {
        var presentation = Presentation.CreateEmpty();
        var first = presentation.Slides[0];
        first.Title = "Intro";
        first.Shapes.Add(new SlideShape
        {
            Id = 42,
            Name = "Product image",
            Kind = SlideShapeKind.Picture,
            Picture = new ImagePart()
        });
        presentation.Slides.Add(new Slide { Title = string.Empty });

        var summary = PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(presentation);
        var pane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            presentation,
            summary,
            selectedRowIndex: 1);

        PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(pane, 99)
            .Should().Be(1);
        PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(pane, 99)
            .Should().Be(new PresentationAccessibilityCheckerNavigationPlan(
                true,
                1,
                1,
                null,
                false,
                null));
        PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(pane, 0)
            .Should().Be(new PresentationAccessibilityCheckerNavigationPlan(
                true,
                0,
                0,
                42,
                true,
                null));

        var clean = Presentation.CreateEmpty();
        clean.Slides[0].Title = "Intro";
        var cleanPane = PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerPanePlan(
            clean,
            PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(clean));

        PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(cleanPane, 0)
            .Should().Be(-1);
        PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(cleanPane, 0)
            .Should().Be(new PresentationAccessibilityCheckerNavigationPlan(
                false,
                -1,
                -1,
                null,
                false,
                "No accessibility issues found."));
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

    [Theory]
    [InlineData(null, null, false, "Alt text: missing")]
    [InlineData("Sales", null, false, "Alt text title: Sales")]
    [InlineData(null, "Quarterly sales by region.", false, "Alt text: Quarterly sales by region.")]
    [InlineData("Sales", "Quarterly sales by region.", false, "Alt text: Sales - Quarterly sales by region.")]
    [InlineData("Ignored", "Ignored", true, "Decorative object")]
    public void ReadingOrderAltTextDisplayText_CoversRendererReadyPermutations(
        string? title,
        string? description,
        bool isDecorative,
        string expected)
    {
        PresentationReviewWorkflowPlanner.BuildReadingOrderAltTextDisplayText(
                title,
                description,
                isDecorative)
            .Should().Be(expected);
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
        Action(nestedFirst, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
                "Move Later",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
        Action(nestedLast, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                true,
                PresentationWorkflowCapabilityStatus.Available,
                null));
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
        editor.SelectedShapeIds.Should().Equal(middle.Id);
        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        slide.Shapes.Select(shape => shape.Id).Should().Equal(1u, 2u, 3u);
        editor.SelectedShapeIds.Should().Equal(middle.Id);
        editor.CanRedo.Should().BeTrue();
        editor.Redo();
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u, 3u);
        editor.SelectedShapeIds.Should().Equal(middle.Id);
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
    public void TryApplyReadingOrderSelection_SelectsTopLevelAndNestedPaneItems()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Clear();
        slide.Shapes.Add(new SlideShape { Id = 1, Name = "Title" });
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Group",
            Kind = SlideShapeKind.Group,
            Children =
            {
                new SlideShape { Id = 3, Name = "Nested child" },
                new SlideShape { Id = 4, Name = "Nested sibling" }
            }
        });
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));

        var nested = PresentationReviewWorkflowPlanner.TryApplyReadingOrderSelection(editor, 4);
        var topLevel = PresentationReviewWorkflowPlanner.TryApplyReadingOrderSelection(editor, 1);
        var missing = PresentationReviewWorkflowPlanner.TryApplyReadingOrderSelection(editor, 99);

        nested.Should().Be(new PresentationReadingOrderSelectionPlan(
            PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
            true,
            0,
            4,
            3,
            null));
        topLevel.Should().Be(new PresentationReadingOrderSelectionPlan(
            PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
            true,
            0,
            1,
            0,
            null));
        editor.SelectedShapeIds.Should().Equal(1u);
        missing.Should().Be(new PresentationReadingOrderSelectionPlan(
            PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
            false,
            0,
            99,
            -1,
            PresentationReviewWorkflowPlanner.ReadingOrderItemNotFoundMessage));
        editor.SelectedShapeIds.Should().Equal(1u);
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
    public void BuildProofingPanePlan_ModelsIssueRowsSelectionAndCorrectionAction()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Teh caption"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution, selectedRowIndex: 1);

        plan.CanRun.Should().BeTrue();
        plan.IssueCount.Should().Be(2);
        plan.Rows.Select(row => row.Text).Should().Equal("eror", "Teh");
        plan.SelectedRowIndex.Should().Be(1);
        plan.SelectedRow.Should().BeSameAs(plan.Rows[1]);
        plan.Rows.Select(row => row.IsSelected).Should().Equal(false, true);
        plan.Rows.Should().OnlyContain(row =>
            row.SelectionAction == new PresentationReviewSurfaceActionPlan("Select", true));
        plan.Rows[0].CorrectionAction.IsEnabled.Should().BeFalse();
        plan.Rows[0].CorrectionAction.DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.ProofingMissingIssueMessage);
        plan.Rows[1].Should().Match<PresentationProofingIssueRowPlan>(row =>
            row.SourceName == "Caption" &&
            row.SlideDisplay == "Slide 1" &&
            row.SuggestedReplacement == "The" &&
            row.CorrectionAction.CommandId == PresentationReviewWorkflowPlanner.ProofingApplyCorrectionCommandId &&
            row.CorrectionAction.IsEnabled &&
            row.IgnoreAction.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId &&
            row.IgnoreAction.IsEnabled &&
            row.IgnoreAllAction.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId &&
            row.IgnoreAllAction.IsEnabled &&
            row.AddToDictionaryAction.CommandId == PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId &&
            row.AddToDictionaryAction.IsEnabled);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingApplyCorrectionCommandId)
            .IsEnabled.Should().BeTrue();
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId)
            .IsEnabled.Should().BeTrue();
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId)
            .IsEnabled.Should().BeTrue();
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId)
            .IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void BuildProofingPanePlan_IgnoreSuppressesOnlySelectedOccurrenceAndNormalizesSelection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Body eror"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var initial = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution, selectedRowIndex: 1);
        var ignoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssue(
            PresentationProofingIgnoreState.Empty,
            initial.SelectedRow);
        var filtered = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            execution,
            PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterIgnore(
                initial.SelectedRowIndex,
                PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution, ignoreState: ignoreState)),
            ignoreState);

        filtered.IssueCount.Should().Be(1);
        filtered.SelectedRowIndex.Should().Be(0);
        filtered.SelectedRow!.Scope.Kind.Should().Be(PresentationProofingScopeKind.SlideTitle);
        filtered.SelectedRow.Text.Should().Be("eror");
        filtered.Rows.Should().ContainSingle(row =>
            row.Scope.Kind == PresentationProofingScopeKind.SlideTitle &&
            row.Text == "eror");

        PresentationReviewWorkflowPlanner.AddProofingIgnoredIssue(ignoreState, initial.SelectedRow)
            .IgnoredIssues.Should().HaveCount(1);
    }

    [Fact]
    public void BuildProofingPanePlan_IgnoreAllSuppressesMatchingIssueTextAndMessageAcrossScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Title eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "Body EROR and teh"
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
                            new TableCell { TextBody = TextBody("Cell eror") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("Notes eror");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment eror",
            Replies =
            {
                new SlideCommentReply { Text = "Reply eror" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var initial = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        var ignoreState = PresentationReviewWorkflowPlanner.AddProofingIgnoredIssueGroup(
            PresentationProofingIgnoreState.Empty,
            initial.SelectedRow);
        var filtered = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution, ignoreState: ignoreState);

        execution.Issues.Where(issue =>
                string.Equals(issue.Text, "eror", StringComparison.OrdinalIgnoreCase) &&
                issue.Message == "Possible misspelling.")
            .Select(issue => issue.Scope.Kind)
            .Should().Equal(
                PresentationProofingScopeKind.SlideTitle,
                PresentationProofingScopeKind.ShapeText,
                PresentationProofingScopeKind.TableCellText,
                PresentationProofingScopeKind.SpeakerNotes,
                PresentationProofingScopeKind.Comment,
                PresentationProofingScopeKind.CommentReply);
        filtered.IssueCount.Should().Be(1);
        filtered.SelectedRow!.Text.Should().Be("teh");
        filtered.SelectedRow.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);
        filtered.SelectedRow.CorrectionAction.IsEnabled.Should().BeTrue();

        PresentationReviewWorkflowPlanner.AddProofingIgnoredIssueGroup(ignoreState, initial.SelectedRow)
            .IgnoredIssueGroups.Should().HaveCount(1);
    }

    [Fact]
    public void BuildProofingPanePlan_AddToDictionarySuppressesMatchingMisspellingAcrossScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Title eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "Body EROR and teh"
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
                            new TableCell { TextBody = TextBody("Cell eror") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("Notes eror");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment eror",
            Replies =
            {
                new SlideCommentReply { Text = "Reply eror" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var initial = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        var dictionaryState = PresentationReviewWorkflowPlanner.AddProofingDictionaryWord(
            PresentationProofingDictionaryState.Empty,
            initial.SelectedRow);
        var filtered = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            execution,
            dictionaryState: dictionaryState);

        execution.Issues.Where(issue =>
                string.Equals(issue.Text, "eror", StringComparison.OrdinalIgnoreCase) &&
                issue.Message == PresentationReviewWorkflowPlanner.ProofingPossibleMisspellingMessage)
            .Select(issue => issue.Scope.Kind)
            .Should().Equal(
                PresentationProofingScopeKind.SlideTitle,
                PresentationProofingScopeKind.ShapeText,
                PresentationProofingScopeKind.TableCellText,
                PresentationProofingScopeKind.SpeakerNotes,
                PresentationProofingScopeKind.Comment,
                PresentationProofingScopeKind.CommentReply);
        dictionaryState.NormalizedWords.Should().Equal("EROR");
        filtered.IssueCount.Should().Be(1);
        filtered.SelectedRow!.Text.Should().Be("teh");
        filtered.SelectedRow.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);

        PresentationReviewWorkflowPlanner.AddProofingDictionaryWord(dictionaryState, initial.SelectedRow)
            .NormalizedWords.Should().HaveCount(1);
    }

    [Fact]
    public void BuildProofingPanePlan_AddToDictionaryIsDisabledForGrammarAndPunctuationIssues()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "A apple. bad punctuation!!";

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        plan.Rows.Should().OnlyContain(row =>
            row.Message != PresentationReviewWorkflowPlanner.ProofingPossibleMisspellingMessage);
        plan.Rows.Should().OnlyContain(row =>
            !row.AddToDictionaryAction.IsEnabled);
        plan.SelectedRow!.AddToDictionaryAction.DisabledReason.Should()
            .Be(PresentationReviewWorkflowPlanner.ProofingCannotAddToDictionaryMessage);
        plan.Actions.Single(action => action.CommandId == PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId)
            .IsEnabled.Should().BeFalse();
        PresentationReviewWorkflowPlanner.AddProofingDictionaryWord(
                PresentationProofingDictionaryState.Empty,
                plan.SelectedRow)
            .NormalizedWords.Should().BeEmpty();
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsRepeatedWordsWithSingleWordCorrection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Revenue rose rose again"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        var row = plan.SelectedRow!;

        execution.Issues.Should().ContainSingle().Which.Should().Match<PresentationProofingIssueDescriptor>(issue =>
            issue.Scope.Kind == PresentationProofingScopeKind.ShapeText &&
            issue.Start == 8 &&
            issue.Length == 9 &&
            issue.Text == "rose rose" &&
            issue.Message == "Repeated word.");
        row.Text.Should().Be("rose rose");
        row.SuggestedReplacement.Should().Be("rose");
        row.CorrectionAction.IsEnabled.Should().BeTrue();

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            row.Scope,
            row.Start,
            row.Length,
            row.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            row.Scope,
            row.Start,
            row.Length,
            "rose",
            "Revenue rose again",
            null));
        slide.Shapes.Single(shape => shape.Id == 4).Text.Should().Be("Revenue rose again");
    }

    [Theory]
    [InlineData("alot", "a lot")]
    [InlineData("becuase", "because")]
    [InlineData("begining", "beginning")]
    [InlineData("definately", "definitely")]
    [InlineData("enviroment", "environment")]
    [InlineData("maintainance", "maintenance")]
    [InlineData("occassion", "occasion")]
    [InlineData("seperate", "separate")]
    [InlineData("thier", "their")]
    [InlineData("tommorow", "tomorrow")]
    [InlineData("untill", "until")]
    [InlineData("wich", "which")]
    public void BuiltInProofingLexicon_FindsAndCorrectsCommonMisspelling(
        string misspelling,
        string correction)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = $"Plan {misspelling}";

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var issue = execution.Issues.Should().ContainSingle().Subject;
        issue.Text.Should().Be(misspelling);

        var pane = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        pane.SelectedRow!.SuggestedReplacement.Should().Be(correction);

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            issue.Scope,
            issue.Start,
            issue.Length,
            correction);

        mutation.ShouldApply.Should().BeTrue();
        presentation.Slides[0].Title.Should().Be($"Plan {correction}");
    }

    [Theory]
    [InlineData("could of", "could have")]
    [InlineData("its a", "it's a")]
    [InlineData("their are", "there are")]
    [InlineData("your welcome", "you're welcome")]
    [InlineData("YOUR WELCOME", "YOU'RE WELCOME")]
    public void BuiltInProofingGrammar_FindsAndCorrectsUnambiguousPhrase(
        string phrase,
        string correction)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = $"Plan {phrase} today";

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var issue = execution.Issues.Should().ContainSingle().Subject;
        issue.Text.Should().Be(phrase);
        issue.Message.Should().Be(PresentationReviewWorkflowPlanner.ProofingGrammarConfusionMessage);

        var pane = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        pane.SelectedRow!.SuggestedReplacement.Should().Be(correction);

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            issue.Scope,
            issue.Start,
            issue.Length,
            pane.SelectedRow.SuggestedReplacement);

        mutation.ShouldApply.Should().BeTrue();
        presentation.Slides[0].Title.Should().Be($"Plan {correction} today");
    }

    [Fact]
    public void BuiltInProofingGrammar_UsesBoundariesAndSkipsAmbiguousFragments()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides[0].Title = "Your welcomes its asterisk their area could offer";

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().BeEmpty();
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsSentenceStartCapitalizationWithSingleLetterCorrection()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "intro starts. next sentence! already correct? no"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        var row = plan.SelectedRow!;

        execution.Issues.Should().HaveCount(4);
        execution.Issues.Select(issue => issue.Text).Should().Equal("i", "n", "a", "n");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == "Sentence should start with a capital letter.");
        row.Should().Match<PresentationProofingIssueRowPlan>(issue =>
            issue.Start == 0 &&
            issue.Length == 1 &&
            issue.Text == "i" &&
            issue.SuggestedReplacement == "I" &&
            issue.CorrectionAction.IsEnabled);

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            row.Scope,
            row.Start,
            row.Length,
            row.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            row.Scope,
            0,
            1,
            "I",
            "Intro starts. next sentence! already correct? no",
            null));
        slide.Shapes.Single(shape => shape.Id == 4).Text
            .Should().Be("Intro starts. next sentence! already correct? no");
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsPunctuationSpacingWithSharedCorrections()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Revenue grew , but margin fell.Next step wins"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(2);
        execution.Issues.Select(issue => issue.Text).Should().Equal(" ,", ".N");
        execution.Issues.Select(issue => issue.Message).Should().Equal(
            PresentationReviewWorkflowPlanner.ProofingWhitespaceBeforePunctuationMessage,
            PresentationReviewWorkflowPlanner.ProofingMissingSpaceAfterSentencePunctuationMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().Equal(",", ". N");
        plan.Rows[0].CorrectionAction.IsEnabled.Should().BeTrue();

        var removeWhitespace = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            plan.Rows[0].Scope,
            plan.Rows[0].Start,
            plan.Rows[0].Length,
            plan.Rows[0].SuggestedReplacement);
        var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation));
        var addSpace = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            refreshed.Rows.Single().Scope,
            refreshed.Rows.Single().Start,
            refreshed.Rows.Single().Length,
            refreshed.Rows.Single().SuggestedReplacement);

        removeWhitespace.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            plan.Rows[0].Scope,
            plan.Rows[0].Start,
            plan.Rows[0].Length,
            ",",
            "Revenue grew, but margin fell.Next step wins",
            null));
        addSpace.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            refreshed.Rows.Single().Scope,
            refreshed.Rows.Single().Start,
            refreshed.Rows.Single().Length,
            ". N",
            "Revenue grew, but margin fell. Next step wins",
            null));
        slide.Shapes.Single(shape => shape.Id == 4).Text
            .Should().Be("Revenue grew, but margin fell. Next step wins");
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsRepeatedTerminalPunctuationWithSharedCorrections()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Hello!! Why?? Wait?!?"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(3);
        execution.Issues.Select(issue => issue.Text).Should().Equal("!!", "??", "?!?");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == PresentationReviewWorkflowPlanner.ProofingRepeatedTerminalPunctuationMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().Equal("!", "?", "?");
        plan.SelectedRow!.CorrectionAction.IsEnabled.Should().BeTrue();

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            plan.SelectedRow.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            "!",
            "Hello! Why?? Wait?!?",
            null));
        slide.Shapes.Single(shape => shape.Id == 4).Text
            .Should().Be("Hello! Why?? Wait?!?");
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsArticleAgreementAcrossSharedProofingScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "A apple";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "We saw a honest mistake"
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
                            new TableCell { TextBody = TextBody("An banana") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("Bring an user guide");
        slide.Comments.Add(new SlideComment
        {
            Text = "Choose a orange",
            Replies =
            {
                new SlideCommentReply { Text = "Review an template" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(6);
        execution.Issues.Select(issue => issue.Scope.Kind).Should().Equal(
            PresentationProofingScopeKind.SlideTitle,
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.TableCellText,
            PresentationProofingScopeKind.SpeakerNotes,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        execution.Issues.Select(issue => issue.Text).Should().Equal(
            "A apple",
            "a honest",
            "An banana",
            "an user",
            "a orange",
            "an template");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == PresentationReviewWorkflowPlanner.ProofingArticleAgreementMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().Equal(
            "An apple",
            "an honest",
            "A banana",
            "a user",
            "an orange",
            "a template");
        plan.SelectedRow!.CorrectionAction.IsEnabled.Should().BeTrue();

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            plan.SelectedRow.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            plan.SelectedRow.Scope,
            0,
            7,
            "An apple",
            "An apple",
            null));
        slide.Title.Should().Be("An apple");
    }

    [Fact]
    public void BuildProofingExecutionPlan_ArticleAgreementAvoidsGuardedAndAmbiguousText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Correct a user guide and an honest note. Guard an MBA, a URL, an x, a 8, a 11th result, an user@example.com, a https://example.com/apple, a mailto:user@example.com, a university, a one-off result, and a euro plan."
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().NotContain(issue =>
            issue.Message == PresentationReviewWorkflowPlanner.ProofingArticleAgreementMessage);
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsSubjectVerbAgreementAcrossSharedProofingScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "The slides is ready";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "This chart are useful"
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
                            new TableCell { TextBody = TextBody("Reports has owners") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("The plan have milestones");
        slide.Comments.Add(new SlideComment
        {
            Text = "The tables was updated",
            Replies =
            {
                new SlideCommentReply { Text = "This slide were final" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(6);
        execution.Issues.Select(issue => issue.Scope.Kind).Should().Equal(
            PresentationProofingScopeKind.SlideTitle,
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.TableCellText,
            PresentationProofingScopeKind.SpeakerNotes,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        execution.Issues.Select(issue => issue.Text).Should().Equal(
            "The slides is",
            "This chart are",
            "Reports has",
            "The plan have",
            "The tables was",
            "This slide were");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == PresentationReviewWorkflowPlanner.ProofingSubjectVerbAgreementMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().Equal(
            "The slides are",
            "This chart is",
            "Reports have",
            "The plan has",
            "The tables were",
            "This slide was");
        plan.SelectedRow!.CorrectionAction.IsEnabled.Should().BeTrue();

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            plan.SelectedRow.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            plan.SelectedRow.Scope,
            0,
            13,
            "The slides are",
            "The slides are ready",
            null));
        slide.Title.Should().Be("The slides are ready");
    }

    [Fact]
    public void BuildProofingExecutionPlan_SubjectVerbAgreementAvoidsGuardedAndAmbiguousText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "This chart is correct. The slides are ready. They is ambiguous. Data is imported. " +
                "The series are related. The 2026 reports has numbers. The URL has docs. " +
                "\"The slides is quoted.\" `The chart are code` https://example.com/slides-is-ready " +
                "This unknown are skipped. These chart are skipped."
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().NotContain(issue =>
            issue.Message == PresentationReviewWorkflowPlanner.ProofingSubjectVerbAgreementMessage);
    }

    [Fact]
    public void BuildProofingExecutionPlan_RepeatedTerminalPunctuationScansAllProofingScopes()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Title!!";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Body",
            Text = "Body!!"
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
                            new TableCell { TextBody = TextBody("Cell!!") }
                        }
                    }
                }
            }
        });
        slide.Notes = TextBody("Notes!!");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment!!",
            Replies =
            {
                new SlideCommentReply { Text = "Reply!!" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(6);
        execution.Issues.Select(issue => issue.Scope.Kind).Should().Equal(
            PresentationProofingScopeKind.SlideTitle,
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.TableCellText,
            PresentationProofingScopeKind.SpeakerNotes,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        execution.Issues.Select(issue => issue.Text).Should().OnlyContain(text => text == "!!");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == PresentationReviewWorkflowPlanner.ProofingRepeatedTerminalPunctuationMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().OnlyContain(replacement => replacement == "!");
    }

    [Fact]
    public void BuildProofingExecutionPlan_RepeatedTerminalPunctuationAvoidsGuardedText()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Ellipsis ... Stays. Decimal 3.14 Stays. Version 1.2.3 Stays. Visit https://example.com/path??x=1 Stays. Also www.example.com/a??b Stays. Email user??@example.com Stays. Mail mailto:user??@example.com Stays. Intentional really?! Stays and no!? Stays."
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().NotContain(issue =>
            issue.Message == PresentationReviewWorkflowPlanner.ProofingRepeatedTerminalPunctuationMessage);
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsListPunctuationSpacingWithSharedCorrections()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "alpha,beta;gamma:delta"
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);
        var listRows = plan.Rows
            .Where(row => row.Message == PresentationReviewWorkflowPlanner.ProofingMissingSpaceAfterListPunctuationMessage)
            .ToArray();

        listRows.Should().HaveCount(3);
        listRows.Select(row => row.Text).Should().Equal(",", ";", ":");
        listRows.Select(row => row.SuggestedReplacement).Should().Equal(", ", "; ", ": ");

        while (true)
        {
            var refreshedExecution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
            var issueIndex = Array.FindIndex(
                refreshedExecution.Issues.ToArray(),
                issue => issue.Message == PresentationReviewWorkflowPlanner.ProofingMissingSpaceAfterListPunctuationMessage);
            if (issueIndex < 0)
            {
                break;
            }

            var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(refreshedExecution, issueIndex);
            var currentRow = refreshed.SelectedRow!;
            currentRow.CorrectionAction.IsEnabled.Should().BeTrue();
            PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
                    presentation,
                    currentRow.Scope,
                    currentRow.Start,
                    currentRow.Length,
                    currentRow.SuggestedReplacement)
                .Should().Match<PresentationProofingCorrectionMutationPlan>(mutation =>
                    mutation.ShouldApply &&
                    mutation.Length == 1);
        }

        slide.Shapes.Single(shape => shape.Id == 4).Text
            .Should().Be("alpha, beta; gamma: delta");
    }

    [Fact]
    public void BuildProofingPanePlan_FlagsExtraInteriorSpacesWithSharedCorrections()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Revenue  grew"
        });
        slide.Notes = TextBody("Speaker.\t\tNext");
        slide.Comments.Add(new SlideComment
        {
            Text = "Comment   text",
            Replies =
            {
                new SlideCommentReply { Text = "Reply \t text" }
            }
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);
        var plan = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(execution);

        execution.Issues.Should().HaveCount(4);
        execution.Issues.Select(issue => issue.Scope.Kind).Should().Equal(
            PresentationProofingScopeKind.ShapeText,
            PresentationProofingScopeKind.SpeakerNotes,
            PresentationProofingScopeKind.Comment,
            PresentationProofingScopeKind.CommentReply);
        execution.Issues.Select(issue => issue.Text).Should().Equal("  ", "\t\t", "   ", " \t ");
        execution.Issues.Select(issue => issue.Message).Should().OnlyContain(message =>
            message == PresentationReviewWorkflowPlanner.ProofingExtraSpacesMessage);
        plan.Rows.Select(row => row.SuggestedReplacement).Should().OnlyContain(replacement => replacement == " ");
        plan.SelectedRow!.CorrectionAction.IsEnabled.Should().BeTrue();

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            plan.SelectedRow.SuggestedReplacement);

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            plan.SelectedRow.Scope,
            plan.SelectedRow.Start,
            plan.SelectedRow.Length,
            " ",
            "Revenue grew",
            null));
        slide.Shapes.Single(shape => shape.Id == 4).Text.Should().Be("Revenue grew");
    }

    [Fact]
    public void BuildProofingExecutionPlan_ExtraSpacesPreservesLineStartsAndNewlineBoundaries()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "  Indented line\nNext  \n\tTabbed line\nLeft\t\tRight\nTrailing  "
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().ContainSingle().Which.Should().Match<PresentationProofingIssueDescriptor>(issue =>
            issue.Message == PresentationReviewWorkflowPlanner.ProofingExtraSpacesMessage &&
            issue.Text == "\t\t" &&
            issue.Scope.Text.Contains("\nLeft\t\tRight\n", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildProofingExecutionPlan_ListPunctuationSpacingAvoidsNumbersUrlsEmailsAndExistingSeparators()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Numbers 1,000 and 10:30 stay. Visit https://example.com/a,b;c:d and www.example.com/a,b. Email user@example.com,a and mailto:user@example.com:a. Already ok, done; next: value. Punctuation:,;: stays."
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().BeEmpty();
    }

    [Fact]
    public void BuildProofingExecutionPlan_SentenceStartCapitalizationAvoidsExistingCapsDecimalsUrlsAndEmails()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Already capped. 3.14 stays. Visit https://example.com/path.Next. www.example.com.Next works. Email user@example.com.Next now."
        });
        slide.Shapes.Add(new SlideShape
        {
            Id = 5,
            Name = "Link",
            Text = "https://example.com/path.Next stays capped. Email mailto:user@example.com.Next."
        });

        var execution = PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation);

        execution.Issues.Should().BeEmpty();
    }

    [Fact]
    public void BuildProofingPanePlan_AppliesCorrectionAndNormalizesSelectionAfterRefresh()
    {
        var presentation = Presentation.CreateEmpty();
        var slide = presentation.Slides[0];
        slide.Title = "Intro eror";
        slide.Shapes.Add(new SlideShape
        {
            Id = 4,
            Name = "Caption",
            Text = "Body eror"
        });
        var initial = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation),
            selectedRowIndex: 1);
        var selected = initial.SelectedRow!;

        var mutation = PresentationReviewWorkflowPlanner.TryApplyProofingCorrection(
            presentation,
            selected.Scope,
            selected.Start,
            selected.Length,
            selected.SuggestedReplacement);
        var refreshed = PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
            PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation),
            PresentationReviewWorkflowPlanner.NormalizeProofingSelectionAfterCorrection(
                initial.SelectedRowIndex,
                PresentationReviewWorkflowPlanner.BuildProofingPanePlan(
                    PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(presentation))));

        mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
            true,
            selected.Scope,
            selected.Start,
            selected.Length,
            "error",
            "Body error",
            null));
        refreshed.IssueCount.Should().Be(1);
        refreshed.SelectedRowIndex.Should().Be(0);
        refreshed.SelectedRow!.Text.Should().Be("eror");
        slide.Shapes.Single(shape => shape.Id == 4).Text.Should().Be("Body error");
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
        PresentationModel presentation,
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

    private static TextBody TextBody(string text, Hyperlink? hyperlink = null)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Hyperlink = hyperlink });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static TextBody TextBodyWithColor(string text, SrgbColor color)
        => TextBodyWithColor(text, new ThemeAwareColor(color));

    private static TextBody TextBodyWithColor(string text, ThemeAwareColor color)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Color = color });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static TextBody TextBodyWithFill(string text, ShapeFill textFill)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, TextFill = textFill });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static string TextBodyPlainText(TextBody textBody)
        => string.Join("\n", textBody.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));
}
