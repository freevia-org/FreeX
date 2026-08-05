using System.IO;
using System.Text;
using Free.Shared.Ribbon;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

public sealed class ReviewWorkflowAdapterTests
{
    [StaFact]
    public void WpfCommentPanePlan_ExposesResolvedThreadActionAuthority()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Resolved thread.",
                Idx = 1,
                IsResolved = true,
                ResolvedBy = "Reviewer",
            });

            var plan = window.ShowReviewCommentsPane();

            plan.Actions
                .Where(action => action.CommandId != PresentationReviewWorkflowPlanner.ReplyCommentCommandId)
                .Select(action => $"{action.CommandId}|{action.Label}|{action.IsEnabled}")
                .Should()
                .Equal(
                    "freep.review.comments.pane|Show Comments|True",
                    "freep.review.comments.add|New Comment|True",
                    "freep.review.comments.edit|Edit Comment|True",
                    "freep.review.comments.delete|Delete Comment|True",
                    "freep.review.comments.previous|Previous Comment|False",
                    "freep.review.comments.next|Next Comment|False",
                    "freep.review.comments.resolve|Resolve Comment|False",
                    "freep.review.comments.reopen|Reopen Comment|True");
        }
        finally
        {
            window.Close();
        }
    }

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
                IsResolved = true,
                ResolvedBy = "Reviewer",
                ResolvedDateTime = new DateTime(2026, 7, 2, 8, 15, 0, DateTimeKind.Utc),
            });

            var table = new SlideShape
            {
                Id = 426,
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
                                new TableCell(),
                                new TableCell()
                            }
                        }
                    }
                }
            };
            var shape = new SlideShape
            {
                Id = 427,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Packaging photo",
            };
            window.Editor.CurrentSlide.Shapes.Add(table);
            window.Editor.CurrentSlide.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            window.RefreshReviewWorkflowPlans();

            window.LastCommentPanePlan.Should().NotBeNull();
            window.LastCommentPanePlan!.TotalCommentCount.Should().Be(1);
            window.LastCommentPanePlan.OpenThreadCount.Should().Be(0);
            window.LastCommentPanePlan.ResolvedThreadCount.Should().Be(1);
            window.LastCommentPanePlan.TotalReplyCount.Should().Be(0);
            window.LastCommentPanePlan.TotalMentionCount.Should().Be(0);
            window.ReviewCommentPaneSummary.Should()
                .Be("1 thread: 0 open threads, 1 resolved thread, 0 replies, 0 mentions");
            window.LastCommentPanePlan.FilterSummaryLabel.Should().Be("Showing all threads");
            window.ReviewCommentPaneFilterStates.Should().Equal(
                "All|All|1|True|True",
                "Open|Open|0|False|False",
                "Resolved|Resolved|1|False|True",
                "Mentions|Mentions|0|False|False");
            window.LastCommentPanePlan.Comments.Single().Should().Match<PresentationCommentDescriptor>(comment =>
                comment.ThreadStatus == PresentationCommentThreadStatus.Resolved &&
                comment.ThreadStatusLabel == "Resolved" &&
                comment.ThreadStatusSummary == "Resolved by Reviewer" &&
                comment.ResolvedByDisplayName == "Reviewer" &&
                comment.InitialsBadgeText == "RV" &&
                comment.AuthorIdentityKey == "REVIEWER|RV" &&
                comment.IsSelected &&
                !comment.CanResolve &&
                comment.CanReopen &&
                !comment.CanReply);
            window.LastCommentPanePlan.SelectedComment.Should().BeSameAs(window.LastCommentPanePlan.Comments[0]);
            window.ReviewCommentSelectedCount.Should().Be(1);
            window.LastCommentPanePlan.Actions.Select(action => action.CommandId)
                .Should()
                .Contain(new[]
                {
                    PresentationReviewWorkflowPlanner.CommentsPaneCommandId,
                    PresentationReviewWorkflowPlanner.ReopenCommentCommandId
                });
            window.LastCommentPanePlan.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ReopenCommentCommandId)
                .IsEnabled.Should().BeTrue();
            window.LastAccessibilitySummaryPlan.Should().NotBeNull();
            var missingAltText = window.LastAccessibilitySummaryPlan!.Issues.Single(issue =>
                issue.ShapeId == shape.Id && issue.Title == "Alt text missing");
            missingAltText.Action.Should().Be(new PresentationAccessibilityIssueActionSummary(
                PresentationReviewWorkflowPlanner.MissingAltTextActionSummary,
                PresentationReviewWorkflowPlanner.AltTextCommandId,
                true));
            window.LastAccessibilitySummaryPlan.Issues.Should().Contain(issue =>
                issue.ShapeId == table.Id &&
                issue.Title == "Table header row missing" &&
                issue.Action.Summary == PresentationReviewWorkflowPlanner.MissingTableHeaderRowActionSummary);
            window.LastAltTextRequestPlan.Should().NotBeNull();
            window.LastAltTextRequestPlan!.Should().Be(new PresentationAltTextRequestPlan(
                true,
                shape.Id,
                "Product image",
                "Packaging photo",
                "Picture \"Product image\" (PNG image) on slide \"Slide 1\". Describe the important visual details and context.",
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
            window.LastReadingOrderPlan.Should().NotBeNull();
            window.LastReadingOrderPlan!.Items.Select(item => item.ShapeId).Should().Contain(shape.Id);
            window.LastReadingOrderPlan.SelectedItem.Should().NotBeNull();
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(shape.Id);
            window.LastReadingOrderPlan.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
                .Should().Be(new PresentationReviewWorkflowActionPlan(
                    PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
                    "Move Later",
                    PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                    false,
                    PresentationWorkflowCapabilityStatus.Available,
                    PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage));

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
            window.LastProofingRequestPlan!.Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);
            window.LastProofingExecutionPlan.Should().NotBeNull();
            window.LastProofingExecutionPlan!.Scopes.Select(scope => scope.Kind).Should().Equal(
                PresentationProofingScopeKind.SlideTitle,
                PresentationProofingScopeKind.Comment);
            window.LastProofingExecutionPlan.Scopes.Should().Contain(scope =>
                scope.Kind == PresentationProofingScopeKind.Comment &&
                scope.SlideIndex == 0 &&
                scope.CommentIndex == 0 &&
                scope.Text == "Use the shared plan.");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ProofingIgnoreActions_UseSharedPlannerState()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "Title eror";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 724,
                Name = "Body",
                Text = "Body eror"
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Comment eror",
                Idx = 1
            });

            var opened = window.ShowProofingPane();
            window.IsProofingPaneIgnoreEnabled.Should().BeTrue();
            window.IsProofingPaneIgnoreAllEnabled.Should().BeTrue();
            opened.Actions.Select(action => action.CommandId).Should().Contain(new[]
            {
                PresentationReviewWorkflowPlanner.ProofingIgnoreCommandId,
                PresentationReviewWorkflowPlanner.ProofingIgnoreAllCommandId
            });

            var selected = window.SelectProofingIssueRow(1);
            selected.SelectedRow!.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);
            var afterIgnore = window.IgnoreSelectedProofingIssue();
            afterIgnore.IssueCount.Should().Be(2);
            afterIgnore.Rows.Select(row => row.Scope.Kind).Should().Equal(
                PresentationProofingScopeKind.SlideTitle,
                PresentationProofingScopeKind.Comment);
            afterIgnore.SelectedRowIndex.Should().Be(1);

            var afterIgnoreAll = window.IgnoreAllSelectedProofingIssues();
            afterIgnoreAll.IssueCount.Should().Be(0);
            afterIgnoreAll.Message.Should().Be(PresentationReviewWorkflowPlanner.ProofingNoIssuesMessage);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ProofingAddToDictionary_UsesSharedSessionState()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "Title eror";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 724,
                Name = "Body",
                Text = "Body EROR and teh"
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Comment eror",
                Idx = 1
            });

            var opened = window.ShowProofingPane();
            window.IsProofingPaneAddToDictionaryEnabled.Should().BeTrue();
            opened.Actions.Select(action => action.CommandId).Should().Contain(
                PresentationReviewWorkflowPlanner.ProofingAddToDictionaryCommandId);

            var afterDictionary = window.AddSelectedProofingWordToDictionary();

            afterDictionary.IssueCount.Should().Be(1);
            afterDictionary.SelectedRow!.Text.Should().Be("teh");
            afterDictionary.SelectedRow.Scope.Kind.Should().Be(PresentationProofingScopeKind.ShapeText);
            window.IsProofingPaneAddToDictionaryEnabled.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ReviewCommentReply_RoutesThroughSharedMutationPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Needs a reply.",
                Idx = 1
            });
            window.SetSelectedReviewCommentIndexForTests(0);
            var timestamp = new DateTime(2026, 7, 4, 9, 15, 0, DateTimeKind.Utc);

            var mutation = window.ReplyToSelectedComment(
                "  Paired WPF reply evidence. ",
                timestamp,
                "FreeP User",
                null);

            mutation.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.ReplyComment,
                true,
                0,
                0,
                new SlideComment
                {
                    Author = "Reviewer",
                    Initials = "RV",
                    Text = "Needs a reply.",
                    Idx = 1,
                    Replies =
                    {
                        new SlideCommentReply
                        {
                            Author = "FreeP User",
                            Initials = "FU",
                            Text = "Paired WPF reply evidence.",
                            DateTime = timestamp
                        }
                    }
                },
                null));
            window.Editor.CurrentSlide.Comments.Should().ContainSingle();
            var repliedComment = window.Editor.CurrentSlide.Comments.Single();
            repliedComment.Replies.Should().ContainSingle().Which.Should().Match<SlideCommentReply>(reply =>
                reply.Author == "FreeP User" &&
                reply.Initials == "FU" &&
                reply.Text == "Paired WPF reply evidence." &&
                reply.DateTime == timestamp);
            window.LastCommentPanePlan.Should().NotBeNull();
            window.LastCommentPanePlan!.SelectedComment.Should().NotBeNull();
            window.LastCommentPanePlan.SelectedComment!.ThreadStatusSummary.Should().Be("Open - 1 reply");
            window.LastCommentPanePlan.SelectedComment.Replies.Single().Should().Match<PresentationCommentReplyDescriptor>(reply =>
                reply.TextPreview == "Paired WPF reply evidence." &&
                reply.AuthorDisplayName == "FreeP User" &&
                reply.InitialsBadgeText == "FU" &&
                reply.AuthorIdentityKey == "FREEP USER|FU");
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ReviewModernCommentReply_ReusesPowerPointAuthorIdentity()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Reviewer",
                Initials = "AR",
                Text = "Modern thread.",
                Idx = 1,
                UsesModernCommentSchema = true,
                ModernCommentId = "{11111111-1111-1111-1111-111111111111}",
                ModernAuthorId = "{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}",
                ModernAuthorUserId = "alice@example.com::powerpoint",
                ModernAuthorProviderId = "aad",
                Replies =
                {
                    new SlideCommentReply
                    {
                        Author = "Bob Reviewer",
                        Initials = "BR",
                        Text = "Taking a look.",
                        ModernAuthorId = "{22222222-2222-2222-2222-222222222222}",
                        ModernAuthorUserId = "bob@example.com::powerpoint",
                        ModernAuthorProviderId = "aad"
                    }
                }
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            var mutation = window.ReplyToSelectedComment(
                "  Confirmed after checking the deck. ",
                new DateTime(2026, 7, 4, 9, 20, 0, DateTimeKind.Utc),
                "bob reviewer",
                "br");

            mutation.ShouldApply.Should().BeTrue();
            var repliedComment = window.Editor.CurrentSlide.Comments.Single();
            repliedComment.Replies.Should().HaveCount(2);
            repliedComment.Replies[1].Should().Match<SlideCommentReply>(reply =>
                reply.Author == "bob reviewer" &&
                reply.Initials == "br" &&
                reply.Text == "Confirmed after checking the deck." &&
                reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
                reply.ModernAuthorUserId == "bob@example.com::powerpoint" &&
                reply.ModernAuthorProviderId == "aad");
            window.LastCommentPanePlan.Should().NotBeNull();
            window.LastCommentPanePlan!.SelectedComment!.Replies[1].Should().Match<PresentationCommentReplyDescriptor>(reply =>
                reply.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
                reply.ModernAuthorUserId == "bob@example.com::powerpoint" &&
                reply.ModernAuthorProviderId == "aad" &&
                reply.AuthorIdentityKey == "BOB REVIEWER|BR");
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerPane_RendersSharedPlanAndRoutesRows()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var firstSlide = window.Editor.CurrentSlide!;
            firstSlide.Title = "Intro";
            var shape = new SlideShape
            {
                Id = 808,
                Name = "Product image",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart()
            };
            var linkedText = new SlideShape
            {
                Id = 809,
                Name = "Reference text",
                TextBody = MakeLinkedTextBody("Click here", new Hyperlink
                {
                    Url = "https://example.test/notes",
                    Tooltip = "Open project notes"
                })
            };
            var chart = new SlideShape
            {
                Id = 810,
                Name = "Sales chart",
                Kind = SlideShapeKind.Chart,
                Chart = new ChartShape(),
                AlternativeText = "Quarterly sales by region."
            };
            firstSlide.Shapes.Add(shape);
            firstSlide.Shapes.Add(linkedText);
            firstSlide.Shapes.Add(chart);
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Title = string.Empty;
            window.Editor.SelectSlide(0);

            var opened = window.ShowAccessibilityCheckerPane();

            window.IsAccessibilityCheckerPaneVisible.Should().BeTrue();
            window.AccessibilityCheckerPaneRowCount.Should().Be(4);
            window.AccessibilityCheckerPaneSelectedRowCount.Should().Be(1);
            window.AccessibilityCheckerPaneHeading.Should().Be("Accessibility - 4 issues");
            opened.Rows.Select(row => row.Title).Should().Equal(
                "Alt text missing",
                "Unclear hyperlink text",
                "Chart title missing",
                "Missing slide title");
            opened.Rows[0].CommandHint.Should().Be(PresentationReviewWorkflowPlanner.AltTextCommandId);
            opened.Rows[1].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Category == "Hyperlink" &&
                row.ShapeId == linkedText.Id &&
                row.ActionLabel == "Edit Hyperlink" &&
                row.CommandHint == PresentationReviewWorkflowPlanner.InsertLinkCommandId &&
                row.ShouldNavigateToSlide &&
                row.ShouldSelectShape);
            opened.Rows[2].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Category == "Chart" &&
                row.ShapeId == chart.Id &&
                row.ActionLabel == "Add Chart Title" &&
                row.CommandHint == PresentationReviewWorkflowPlanner.ChartTitleCommandId &&
                row.ShouldNavigateToSlide &&
                row.ShouldSelectShape);
            opened.Rows[3].Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Category == "Slide title" &&
                row.ActionLabel == "Set Slide Title" &&
                row.CommandHint == PresentationReviewWorkflowPlanner.SetSlideTitleCommandId &&
                row.ShouldNavigateToSlide &&
                !row.ShouldSelectShape);

            var selectedChart = window.SelectAccessibilityCheckerRow(2);

            window.Editor.CurrentSlideIndex.Should().Be(0);
            window.Editor.SelectedShapeIds.Should().Equal(chart.Id);
            selectedChart.SelectedRow.Should().NotBeNull();
            selectedChart.SelectedRow!.Title.Should().Be("Chart title missing");

            var actionedChart = window.ApplyAccessibilityCheckerRowAction(2);
            chart.Chart!.Title.Should().Be("Quarterly sales by region");
            window.LastChartTitleMutationPlan.Should().Be(new PresentationChartTitleMutationPlan(
                true,
                0,
                chart.Id,
                "Quarterly sales by region",
                "Quarterly sales by region",
                null));
            actionedChart.Rows.Select(row => row.Title).Should().Equal(
                "Alt text missing",
                "Unclear hyperlink text",
                "Missing slide title");

            var selectedTitle = window.SelectAccessibilityCheckerRow(2);

            window.Editor.CurrentSlideIndex.Should().Be(1);
            window.Editor.SelectedShapeIds.Should().BeEmpty();
            selectedTitle.SelectedRow.Should().NotBeNull();
            selectedTitle.SelectedRow!.Title.Should().Be("Missing slide title");

            var invalidSelection = window.SelectAccessibilityCheckerRow(99);

            invalidSelection.SelectedRowIndex.Should().Be(2);
            invalidSelection.SelectedRow!.Title.Should().Be("Missing slide title");
            window.Editor.CurrentSlideIndex.Should().Be(1);
            window.Editor.SelectedShapeIds.Should().BeEmpty();

            var actionedTitle = window.ApplyAccessibilityCheckerRowAction(2);

            window.Editor.CurrentSlideIndex.Should().Be(1);
            window.Editor.CurrentSlide!.Title.Should().Be("Slide 2");
            window.LastSlideTitleMutationPlan.Should().Be(new PresentationSlideTitleMutationPlan(
                true,
                1,
                "Slide 2",
                "Slide 2",
                null));
            actionedTitle.Rows.Select(row => row.Title).Should().Equal(
                "Alt text missing",
                "Unclear hyperlink text");
            window.LastAccessibilitySummaryPlan!.Issues.Should().NotContain(issue =>
                issue.Title == "Missing slide title");
            window.IsDirty.Should().BeTrue();

            var actionedAltText = window.ApplyAccessibilityCheckerRowAction(0);

            window.Editor.CurrentSlideIndex.Should().Be(0);
            window.Editor.SelectedShapeIds.Should().Equal(shape.Id);
            window.IsAltTextPaneVisible.Should().BeTrue();
            window.AltTextPaneMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingAltTextDescriptionMessage);
            actionedAltText.SelectedRow!.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.AltTextCommandId);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerLowTextContrastRow_UsesSharedPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var shape = new SlideShape
            {
                Id = 812,
                Name = "Muted caption",
                Fill = new ShapeFill.Solid(SrgbColor.FromRgb(0x777777)),
                TextBody = MakeTextBodyWithColor("Muted KPI", SrgbColor.FromRgb(0x777777))
            };
            window.Editor.CurrentSlide!.Title = "Intro";
            window.Editor.CurrentSlide.Shapes.Add(shape);

            var opened = window.ShowAccessibilityCheckerPane();
            var actioned = window.ApplyAccessibilityCheckerRowAction(0);

            opened.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Title == "Low text contrast" &&
                row.Category == "Text contrast" &&
                row.ShapeId == shape.Id &&
                row.ShapeName == "Muted caption" &&
                row.ActionLabel == "Select Object" &&
                row.CommandHint == null &&
                row.Detail.Contains("threshold is 4.5:1.", StringComparison.Ordinal) &&
                row.ShouldNavigateToSlide &&
                row.ShouldSelectShape);
            actioned.SelectedRow!.Title.Should().Be("Low text contrast");
            window.LastAccessibilitySummaryPlan!.Issues.Should().ContainSingle(issue =>
                issue.ShapeId == shape.Id &&
                issue.Title == "Low text contrast" &&
                issue.Action.Summary == PresentationReviewWorkflowPlanner.LowTextContrastActionSummary);
            window.Editor.SelectedShapeIds.Should().Equal(shape.Id);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerLowQualityAltTextRow_UsesSharedPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var shape = new SlideShape
            {
                Id = 811,
                Name = "Hero product photo",
                Kind = SlideShapeKind.Picture,
                Picture = new ImagePart(),
                AlternativeTextTitle = "Hero product photo",
                AlternativeText = "IMG_2048.JPG"
            };
            window.Editor.CurrentSlide!.Title = "Intro";
            window.Editor.CurrentSlide.Shapes.Add(shape);

            var opened = window.ShowAccessibilityCheckerPane();
            var actioned = window.ApplyAccessibilityCheckerRowAction(0);

            window.IsAccessibilityCheckerPaneVisible.Should().BeTrue();
            window.AccessibilityCheckerPaneHeading.Should().Be("Accessibility - 1 issues");
            opened.Rows.Should().ContainSingle().Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                row.Title == "Filename-like alt text" &&
                row.Category == "Alt text" &&
                row.ShapeId == shape.Id &&
                row.ActionLabel == "Open Alt Text" &&
                row.CommandHint == PresentationReviewWorkflowPlanner.AltTextCommandId &&
                row.ShouldNavigateToSlide &&
                row.ShouldSelectShape);
            window.Editor.SelectedShapeIds.Should().Equal(shape.Id);
            window.IsAltTextPaneVisible.Should().BeTrue();
            window.LastAltTextRequestPlan.Should().NotBeNull();
            window.LastAltTextRequestPlan!.CurrentDescription.Should().Be("IMG_2048.JPG");
            actioned.SelectedRow!.Title.Should().Be("Filename-like alt text");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerTableHeaderAction_UsesSharedMutationAndRefreshesPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var table = new SlideShape
            {
                Id = 601,
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
                                new TableCell { TextBody = MakeTextBody("Region") },
                                new TableCell { TextBody = MakeTextBody("Revenue") }
                            }
                        },
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("North") },
                                new TableCell { TextBody = MakeTextBody("$42K") }
                            }
                        }
                    }
                }
            };
            window.Editor.CurrentSlide!.Shapes.Add(table);

            var opened = window.ShowAccessibilityCheckerPane();
            var tableRow = opened.Rows.Single(row => row.Title == "Table header row missing");

            tableRow.ActionLabel.Should().Be("Set Header Row");
            tableRow.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.SetTableHeaderRowCommandId);

            var actioned = window.ApplyAccessibilityCheckerRowAction(tableRow.RowIndex);

            window.LastTableHeaderRowMutationPlan.Should().Be(new PresentationTableHeaderRowMutationPlan(
                true,
                0,
                table.Id,
                null));
            table.Table!.Flags.FirstRow.Should().BeTrue();
            actioned.Rows.Should().NotContain(row => row.Title == "Table header row missing");
            window.LastAccessibilitySummaryPlan!.Issues.Should().NotContain(issue =>
                issue.Title == "Table header row missing");
            window.Editor.SelectedShapeIds.Should().Equal(table.Id);
            window.IsDirty.Should().BeTrue();

            window.Editor.Undo();
            table.Table.Flags.FirstRow.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerTableStructureAction_OpensSharedReviewPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var table = new SlideShape
            {
                Id = 602,
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
                                new TableCell { TextBody = MakeTextBody("Region"), GridSpan = 2 },
                                new TableCell { HMerge = true },
                                new TableCell()
                            }
                        },
                        new TableRow
                        {
                            Cells =
                            {
                                new TableCell { TextBody = MakeTextBody("North") },
                                new TableCell(),
                                new TableCell { TextBody = MakeTextBody("$42K") }
                            }
                        }
                    }
                }
            };
            window.Editor.CurrentSlide!.Shapes.Add(table);

            var opened = window.ShowAccessibilityCheckerPane();
            var tableRow = opened.Rows.Single(row => row.Title == "Blank table header cells");

            tableRow.ActionLabel.Should().Be("Review Table Structure");
            tableRow.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);

            var actioned = window.ApplyAccessibilityCheckerRowAction(tableRow.RowIndex);

            window.LastTableStructureReviewPlan.Should().NotBeNull();
            window.LastTableStructureReviewPlan!.Should().Match<PresentationTableStructureReviewPlan>(plan =>
                plan.CanReview &&
                plan.ShapeId == table.Id &&
                plan.TableName == "Forecast table" &&
                plan.RowCount == 2 &&
                plan.ColumnCount == 3 &&
                plan.ShouldNavigateToSlide &&
                plan.ShouldSelectTable);
            window.LastTableStructureReviewPlan.BlankHeaderCells.Should().Equal(new[]
            {
                new PresentationTableStructureCellPlan(0, 2, "R1C3")
            });
            window.LastTableStructureReviewPlan.BlankBodyCells.Should().Equal(new[]
            {
                new PresentationTableStructureCellPlan(1, 1, "R2C2")
            });
            window.LastTableStructureReviewPlan.MergedOrSplitCells.Select(cell => cell.CellReference)
                .Should().Equal("R1C1", "R1C2");
            window.LastTableStructureReviewDisplayPlan.Should().NotBeNull();
            window.LastTableStructureReviewDisplayPlan!.Summary.Should()
                .Be("Forecast table: 2 rows, 3 columns. 1 blank header cell, 1 blank body cell, 2 merged or split cells.");
            window.LastTableStructureReviewDisplayPlan.Details.Should().Equal(new[]
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
                    "Merged or split cell",
                    "R1C1 spans 2 columns.",
                    "Verify the table still reads correctly in row and column order."),
                new PresentationTableStructureReviewDetailRowPlan(
                    "Merged or split cell",
                    "R1C2 continues a horizontal merge.",
                    "Verify the table still reads correctly in row and column order.")
            });
            window.AccessibilityCheckerTableStructureReviewRenderedLines.Should().Equal(
                "Review Table Structure",
                "Forecast table: 2 rows, 3 columns. 1 blank header cell, 1 blank body cell, 2 merged or split cells.",
                PresentationReviewWorkflowPlanner.TableStructureReviewGuidance,
                "Blank header cell: R1C3 is blank. Add descriptive header text or remove the empty header cell.",
                "Blank body cell: R2C2 is blank. Confirm the blank data cell is intentional or add visible text.",
                "Merged or split cell: R1C1 spans 2 columns. Verify the table still reads correctly in row and column order.",
                "Merged or split cell: R1C2 continues a horizontal merge. Verify the table still reads correctly in row and column order.");
            actioned.SelectedRow!.CommandHint.Should().Be(PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);
            window.LastAccessibilitySummaryPlan!.Issues.Should().Contain(issue =>
                issue.Title == "Blank table header cells" &&
                issue.Action.CommandId == PresentationReviewWorkflowPlanner.ReviewTableStructureCommandId);
            window.Editor.SelectedShapeIds.Should().Equal(table.Id);
            table.Table!.Flags.FirstRow.Should().BeTrue();
            window.IsDirty.Should().BeFalse();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AccessibilityCheckerMediaCaptionTracks_UsesSharedPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "Media accessibility";
            var missingCaptions = new SlideShape
            {
                Id = 711,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true },
                AlternativeText = "Demo walkthrough."
            };
            var captioned = new SlideShape
            {
                Id = 712,
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
                            Label = "English captions",
                            Bytes = Encoding.UTF8.GetBytes(
                                "WEBVTT\r\n\r\n00:00.000 --> 00:01.000\r\nShared transcript cue\r\n")
                        }
                    }
                },
                AlternativeText = "Training walkthrough."
            };
            window.Editor.CurrentSlide.Shapes.Add(missingCaptions);
            window.Editor.CurrentSlide.Shapes.Add(captioned);

            var opened = window.ShowAccessibilityCheckerPane();

            opened.Rows.Should().ContainSingle(row => row.Title == "Video captions missing")
                .Which.Should().Match<PresentationAccessibilityCheckerRowPlan>(row =>
                    row.Category == "Media" &&
                    row.ShapeId == missingCaptions.Id &&
                    row.ShapeName == "Demo video" &&
                    row.ActionLabel == "Open Captions" &&
                    row.CommandHint == PresentationMediaTranscriptPlanner.CaptionAuthoringPaneOpenCommandId &&
                    row.ShouldNavigateToSlide &&
                    row.ShouldSelectShape);
            window.LastAccessibilitySummaryPlan!.Issues.Should().NotContain(issue =>
                issue.ShapeId == captioned.Id && issue.Title == "Video captions missing");
            window.LastMediaTranscriptPlan.Should().NotBeNull();
            window.LastMediaTranscriptPlan!.Tracks.Should().ContainSingle()
                .Which.Should().Match<PresentationMediaTranscriptTrackDescriptor>(track =>
                    track.ShapeId == captioned.Id &&
                    track.ShapeName == "Training video" &&
                    track.Label == "English captions" &&
                    track.Language == "en-US" &&
                    track.Source == "ppt/media/training.vtt" &&
                    track.Status == PresentationMediaTranscriptTrackStatus.Available &&
                    track.CueCount == 1 &&
                    track.Cues[0].Text == "Shared transcript cue");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_MediaCaptionPane_CreateReplaceDelete_UsesSharedPlanner()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var mediaShape = new SlideShape
            {
                Id = 722,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true },
                AlternativeText = "Demo walkthrough."
            };
            window.Editor.CurrentSlide!.Shapes.Add(mediaShape);
            window.Editor.Select(mediaShape.Id);

            var opened = window.ShowMediaCaptionPane();

            opened.ShapeId.Should().Be(mediaShape.Id);
            window.IsMediaCaptionPaneVisible.Should().BeTrue();
            window.MediaCaptionPaneHeading.Should().Be("Media Captions - Demo video");
            window.MediaCaptionPaneTrackCount.Should().Be(0);
            window.IsMediaCaptionCreateEnabled.Should().BeFalse();

            window.SetMediaCaptionPaneInput(
                "English captions",
                "en-US",
                "ppt/media/demo-captions.vtt",
                "WEBVTT\r\n\r\n00:00:00.000 --> 00:00:01.000\r\nInitial cue\r\n");
            window.IsMediaCaptionCreateEnabled.Should().BeTrue();

            var create = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Create);

            create.Succeeded.Should().BeTrue();
            create.TrackIndex.Should().Be(0);
            mediaShape.Media!.CaptionTracks.Should().ContainSingle()
                .Which.Label.Should().Be("English captions");
            window.LastMediaCaptionAuthoringMutationPlan!.Intent.Should().Be(PresentationMediaCaptionAuthoringIntentKind.Create);
            window.MediaCaptionPaneTrackCount.Should().Be(1);
            window.IsMediaCaptionReplaceEnabled.Should().BeTrue();
            window.IsMediaCaptionDeleteEnabled.Should().BeTrue();
            window.IsDirty.Should().BeTrue();
            window.LastMediaTranscriptPlan!.Tracks.Should().ContainSingle()
                .Which.Cues.Single().Text.Should().Be("Initial cue");

            window.SetMediaCaptionPaneInput(
                "English captions",
                "en-US",
                "ppt/media/demo-captions.vtt",
                "WEBVTT\r\n\r\n00:00:01.000 --> 00:00:02.000\r\nUpdated cue\r\n",
                selectedTrackIndex: 0);
            var replace = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Replace);

            replace.Succeeded.Should().BeTrue();
            window.LastMediaTranscriptPlan!.Tracks.Should().ContainSingle()
                .Which.Cues.Single().Text.Should().Be("Updated cue");

            var delete = window.ApplyMediaCaptionPane(PresentationMediaCaptionAuthoringIntentKind.Delete);

            delete.Succeeded.Should().BeTrue();
            mediaShape.Media.CaptionTracks.Should().BeEmpty();
            window.MediaCaptionPaneTrackCount.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_MediaVolumePane_AppliesSelectedMediaVolume()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var mediaShape = new SlideShape
            {
                Id = 723,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true, VolumePercent = 80 }
            };
            window.Editor.CurrentSlide!.Shapes.Add(mediaShape);
            window.Editor.Select(mediaShape.Id);

            window.ShowMediaCaptionPane();
            window.MediaVolumePercent.Should().Be(80);

            window.SetMediaVolumePaneInput(25);
            window.MediaVolumePercent.Should().Be(25);
            window.ApplyMediaVolumePane().Should().BeTrue();

            mediaShape.Media!.VolumePercent.Should().Be(25);
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_MediaPlaybackPane_AppliesSelectedPlaybackOptions()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var mediaShape = new SlideShape
            {
                Id = 724,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true }
            };
            window.Editor.CurrentSlide!.Shapes.Add(mediaShape);
            window.Editor.Select(mediaShape.Id);

            window.ShowMediaCaptionPane();
            window.MediaPlaybackStartMode.Should().Be(MediaPlaybackStartMode.InClickSequence);
            window.MediaLoop.Should().BeFalse();

            window.SetMediaPlaybackPaneInput(MediaPlaybackStartMode.Automatically, true);
            window.MediaPlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
            window.MediaLoop.Should().BeTrue();
            window.ApplyMediaPlaybackPane().Should().BeTrue();

            mediaShape.Media!.PlaybackStartMode.Should().Be(MediaPlaybackStartMode.Automatically);
            mediaShape.Media.Loop.Should().BeTrue();
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_MediaTimingPane_AppliesTrimAndFade()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var mediaShape = new SlideShape
            {
                Id = 727,
                Name = "Demo video",
                Kind = SlideShapeKind.Media,
                Media = new MediaInfo { IsVideo = true }
            };
            window.Editor.CurrentSlide!.Shapes.Add(mediaShape);
            window.Editor.Select(mediaShape.Id);

            window.SetMediaTimingPaneInput(125, 250, 500, 750);
            window.MediaTrimStartMilliseconds.Should().Be(125);
            window.ApplyMediaTimingPane().Should().BeTrue();

            mediaShape.Media!.TrimStartMilliseconds.Should().Be(125);
            mediaShape.Media.TrimEndMilliseconds.Should().Be(250);
            mediaShape.Media.FadeInMilliseconds.Should().Be(500);
            mediaShape.Media.FadeOutMilliseconds.Should().Be(750);
            window.IsDirty.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ApplyProofingCorrection_UsesSharedMutationAndRefreshesPlans()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "The slides is ready";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 904,
                Name = "Caption",
                Text = "Caption  text"
            });
            window.RefreshReviewWorkflowPlans();
            var scope = window.LastProofingExecutionPlan!.Scopes.Single(s =>
                s.Kind == PresentationProofingScopeKind.SlideTitle);
            var start = scope.Text.IndexOf("The slides is", StringComparison.Ordinal);

            var pane = window.ShowProofingPane();

            window.IsProofingPaneVisible.Should().BeTrue();
            window.ProofingPaneIssueRowCount.Should().Be(2);
            window.ProofingPaneSelectedIssueCount.Should().Be(1);
            window.IsProofingPaneCorrectionEnabled.Should().BeTrue();
            window.ProofingPaneHeading.Should().Be("Spelling - 2 issues");
            pane.SelectedRow!.SuggestedReplacement.Should().Be("The slides are");

            var mutation = window.ApplyProofingCorrection(scope, start, "The slides is".Length, "The slides are");
            var selectedCaption = window.SelectProofingIssueRow(0);
            var paneMutation = window.ApplySelectedProofingCorrection();

            mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
                true,
                scope,
                start,
                "The slides is".Length,
                "The slides are",
                "The slides are ready",
                null));
            paneMutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
                true,
                selectedCaption.SelectedRow!.Scope,
                selectedCaption.SelectedRow.Start,
                selectedCaption.SelectedRow.Length,
                " ",
                "Caption text",
                null));
            window.Editor.CurrentSlide.Title.Should().Be("The slides are ready");
            window.Editor.CurrentSlide.Shapes.Single(shape => shape.Id == 904).Text.Should().Be("Caption text");
            window.LastProofingRequestPlan.Should().NotBeNull();
            window.LastProofingExecutionPlan.Should().NotBeNull();
            window.LastProofingExecutionPlan!.Scopes.Single(s =>
                    s.Kind == PresentationProofingScopeKind.SlideTitle)
                .Text.Should().Be("The slides are ready");
            window.LastProofingPanePlan.Should().NotBeNull();
            window.LastProofingPanePlan!.IssueCount.Should().Be(0);
            window.IsProofingPaneCorrectionEnabled.Should().BeFalse();
            window.ProofingPaneMessage.Should().Be(PresentationReviewWorkflowPlanner.ProofingNoIssuesMessage);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_AddAndEditComment_ApplySharedPlanAndRefreshPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var timestamp = new DateTime(2026, 7, 2, 16, 0, 0, DateTimeKind.Utc);

            var add = window.AddComment(
                "  Add execution evidence. ",
                timestamp,
                "  FreeP User ",
                null,
                120,
                240);

            add.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.AddComment,
                true,
                0,
                null,
                new SlideComment
                {
                    Author = "FreeP User",
                    Initials = "FU",
                    Text = "Add execution evidence.",
                    DateTime = timestamp,
                    Xemu = 120,
                    Yemu = 240,
                    Idx = 1
                },
                null));
            window.Editor.CurrentSlide!.Comments.Should().ContainSingle();
            window.LastCommentPanePlan!.SelectedCommentIndex.Should().Be(0);
            window.LastCommentPanePlan.SelectedComment!.TextPreview.Should().Be("Add execution evidence.");

            var edit = window.EditSelectedComment(
                "  Edited execution evidence. ",
                "Reviewer",
                "RV");

            edit.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.EditComment,
                true,
                0,
                0,
                new SlideComment
                {
                    Author = "Reviewer",
                    Initials = "RV",
                    Text = "Edited execution evidence.",
                    DateTime = timestamp,
                    Xemu = 120,
                    Yemu = 240,
                    Idx = 1
                },
                null));
            window.Editor.CurrentSlide.Comments.Single().Text.Should().Be("Edited execution evidence.");
            window.LastCommentPanePlan!.SelectedComment!.TextPreview.Should().Be("Edited execution evidence.");
            window.ReviewCommentSelectedCount.Should().Be(1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ResolveAndReopenComment_ApplySharedPlanAndRefreshPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Close this thread.",
                Idx = 1
            });
            window.SetSelectedReviewCommentIndexForTests(0);
            window.LastCommentPanePlan!.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
                .IsEnabled.Should().BeTrue();

            var resolvedAt = new DateTime(2026, 7, 2, 13, 0, 0, DateTimeKind.Utc);
            var resolve = window.ResolveSelectedComment(resolvedAt, "  FreeP User ");

            resolve.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.ResolveComment,
                true,
                0,
                0,
                new SlideComment
                {
                    Author = "Reviewer",
                    Initials = "RV",
                    Text = "Close this thread.",
                    Idx = 1,
                    IsResolved = true,
                    ResolvedDateTime = resolvedAt,
                    ResolvedBy = "FreeP User"
                },
                null));
            var comment = window.Editor.CurrentSlide.Comments[0];
            comment.IsResolved.Should().BeTrue();
            comment.ResolvedDateTime.Should().Be(resolvedAt);
            comment.ResolvedBy.Should().Be("FreeP User");
            window.LastCommentPanePlan!.Comments.Single().CanReopen.Should().BeTrue();
            window.LastCommentPanePlan.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ResolveCommentCommandId)
                .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.CommentAlreadyResolvedMessage);

            var reopen = window.ReopenSelectedComment();

            reopen.Intent.Should().Be(PresentationReviewWorkflowIntentKind.ReopenComment);
            reopen.ShouldApply.Should().BeTrue();
            var reopened = window.Editor.CurrentSlide.Comments[0];
            reopened.IsResolved.Should().BeFalse();
            reopened.ResolvedDateTime.Should().BeNull();
            reopened.ResolvedBy.Should().BeEmpty();
            window.LastCommentPanePlan!.Comments.Single().CanResolve.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ReplyToComment_AppliesSharedPlanAndRefreshesPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Needs follow-up.",
                Idx = 1
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            var timestamp = new DateTime(2026, 7, 2, 15, 0, 0, DateTimeKind.Utc);
            var reply = window.ReplyToSelectedComment(
                "  @Reviewer fixed in the deck. ",
                timestamp,
                "  FreeP User ",
                null);

            reply.Should().BeEquivalentTo(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.ReplyComment,
                true,
                0,
                0,
                new SlideComment
                {
                    Author = "Reviewer",
                    Initials = "RV",
                    Text = "Needs follow-up.",
                    Idx = 1,
                    Replies =
                    {
                        new SlideCommentReply
                        {
                            Author = "FreeP User",
                            Initials = "FU",
                            Text = "@Reviewer fixed in the deck.",
                            DateTime = timestamp
                        }
                    }
                },
                null));
            window.Editor.CurrentSlide.Comments[0].Replies.Should().ContainSingle();
            window.LastCommentPanePlan!.Comments.Single().Should().Match<PresentationCommentDescriptor>(comment =>
                comment.ReplyCount == 1 &&
                comment.MentionCount == 1 &&
                comment.ReplySummary == "1 reply" &&
                comment.MentionSummary == "1 mention" &&
                comment.ThreadStatusSummary == "Open - 1 reply" &&
                comment.CanReply);
            window.LastCommentPanePlan.Comments.Single().Replies.Single().TextPreview
                .Should().Be("@Reviewer fixed in the deck.");
            window.LastCommentPanePlan.Comments.Single().Replies.Single().Should().Match<PresentationCommentReplyDescriptor>(reply =>
                reply.AuthorDisplayName == "FreeP User" &&
                reply.InitialsBadgeText == "FU" &&
                reply.AuthorIdentityKey == "FREEP USER|FU" &&
                reply.ReplyLabel == "Reply 1" &&
                reply.MentionSummary == "1 mention" &&
                reply.MentionDetailSummary == "Mentions: @Reviewer");
            window.LastCommentPanePlan.Comments.Single().Replies.Single().Mentions
                .Should()
                .ContainSingle()
                .Which.Should().Be(new PresentationCommentMentionDescriptor(0, 0, 9, "Reviewer", "REVIEWER"));
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_VisibleMentionActions_UseSharedPickerAndRefreshPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Writer",
                Initials = "AW",
                Text = "Please ask @No",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Nora Reviewer",
                Initials = "NR",
                Text = "Available for review.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(0);
            window.ReviewCommentPaneRenderedMentionActions.Should().Contain(
                "comment-mention:edit:@Nora.Reviewer:True");

            var invokedEdit = window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:edit");

            invokedEdit.Should().BeTrue();
            window.LastCommentMentionInsertionPlan.Should().NotBeNull();
            window.LastCommentMentionInsertionPlan!.Candidate!.DisplayName.Should().Be("Nora Reviewer");
            window.LastCommentMentionInsertionPlan!.UpdatedText.Should().Be("Please ask @Nora.Reviewer ");
            window.Editor.CurrentSlide.Comments[0].Text.Should().Be("Please ask @Nora.Reviewer");
            window.LastCommentPanePlan!.SelectedComment!.MentionCount.Should().Be(1);
            window.LastCommentPanePlan.SelectedComment.MentionDetailSummary.Should().Be("Mentions: @Nora.Reviewer");
            window.ReviewCommentPaneRenderedMentionLines.Should().Contain("Mentions: @Nora.Reviewer");
            window.IsDirty.Should().BeTrue();

            var invokedReply = window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:reply");

            invokedReply.Should().BeTrue();
            window.Editor.CurrentSlide.Comments[0].Replies.Should().ContainSingle();
            window.Editor.CurrentSlide.Comments[0].Replies[0].Text.Should().Be("@Alice.Writer");
            window.LastCommentPanePlan!.SelectedComment!.Replies.Single().MentionDetailSummary.Should()
                .Be("Mentions: @Alice.Writer");
            window.ReviewCommentPaneRenderedMentionLines.Should().Contain("Mentions: @Nora.Reviewer");
            window.ReviewCommentPaneRenderedMentionLines.Should().Contain("Mentions: @Alice.Writer");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_MentionPicker_AllowsChoosingNonDefaultCandidate()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Writer",
                Initials = "AW",
                Text = "Please ask @",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Nora Reviewer",
                Initials = "NR",
                Text = "Available for review.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            window.InvokeReviewCommentPaneMentionActionForTests("comment-mention:edit", "@Nora.Reviewer")
                .Should().BeTrue();
            window.LastCommentMentionInsertionPlan.Should().NotBeNull();
            window.LastCommentMentionInsertionPlan!.Candidate!.DisplayName.Should().Be("Nora Reviewer");
            window.Editor.CurrentSlide.Comments[0].Text.Should().Be("Please ask @Nora.Reviewer");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_ReplyToModernComment_ReusesPowerPointAuthorIdentity()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Alice Reviewer",
                Initials = "AR",
                Text = "Needs a second reviewer.",
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
                        Text = "Taking a look.",
                        ModernAuthorId = "{22222222-2222-2222-2222-222222222222}",
                        ModernAuthorUserId = "bob@example.com::powerpoint",
                        ModernAuthorProviderId = "aad"
                    }
                }
            });
            window.SetSelectedReviewCommentIndexForTests(0);

            var reply = window.ReplyToSelectedComment(
                "  Confirmed after checking the deck. ",
                new DateTime(2026, 7, 4, 9, 0, 0, DateTimeKind.Utc),
                "bob reviewer",
                "br");

            reply.ShouldApply.Should().BeTrue();
            var comment = window.Editor.CurrentSlide.Comments.Single();
            comment.Replies.Should().HaveCount(2);
            comment.Replies[1].Should().Match<SlideCommentReply>(value =>
                value.Author == "bob reviewer" &&
                value.Initials == "br" &&
                value.Text == "Confirmed after checking the deck." &&
                value.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
                value.ModernAuthorUserId == "bob@example.com::powerpoint" &&
                value.ModernAuthorProviderId == "aad");
            window.LastCommentPanePlan!.SelectedComment!.Replies[1].Should().Match<PresentationCommentReplyDescriptor>(value =>
                value.ModernAuthorId == "{22222222-2222-2222-2222-222222222222}" &&
                value.ModernAuthorUserId == "bob@example.com::powerpoint" &&
                value.ModernAuthorProviderId == "aad");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_DeleteComment_AppliesSharedPlanAndNormalizesSelection()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Keep this thread.",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Delete this thread.",
                Idx = 2
            });
            window.SetSelectedReviewCommentIndexForTests(1);
            window.LastCommentPanePlan!.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
                .IsEnabled.Should().BeTrue();

            var delete = window.DeleteSelectedComment();

            delete.Should().Be(new PresentationCommentMutationPlan(
                PresentationReviewWorkflowIntentKind.DeleteComment,
                true,
                0,
                1,
                null,
                null));
            window.Editor.CurrentSlide.Comments.Should().ContainSingle().Which.Text.Should().Be("Keep this thread.");
            window.LastCommentPanePlan!.SelectedCommentIndex.Should().Be(0);
            window.LastCommentPanePlan.SelectedComment!.TextPreview.Should().Be("Keep this thread.");
            window.ReviewCommentSelectedCount.Should().Be(1);

            window.SetSelectedReviewCommentIndexForTests(99);
            window.LastCommentPanePlan!.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.DeleteCommentCommandId)
                .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);

            var invalid = window.DeleteSelectedComment();

            invalid.ShouldApply.Should().BeFalse();
            invalid.ValidationMessage.Should().Be(PresentationReviewWorkflowPlanner.MissingCommentMessage);
            window.Editor.CurrentSlide.Comments.Should().ContainSingle();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_NextPreviousComment_NavigateThroughSharedPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "First thread.",
                Idx = 1
            });
            window.Editor.CurrentSlide.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Second thread.",
                Idx = 2
            });
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.CurrentSlide!.Comments.Add(new SlideComment
            {
                Author = "Reviewer",
                Initials = "RV",
                Text = "Third thread.",
                Idx = 1
            });
            window.Editor.SelectSlide(0);
            window.SetSelectedReviewCommentIndexForTests(0);
            var dirtyBeforeNavigation = window.IsDirty;

            var sameSlideNext = window.NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment);
            var slideAfterSameSlideNext = window.Editor.CurrentSlideIndex;
            var selectedAfterSameSlideNext = window.LastCommentPanePlan!.SelectedComment!.TextPreview;
            var crossSlideNext = window.NavigateReviewComment(PresentationReviewWorkflowIntentKind.NextComment);
            var slideAfterCrossSlideNext = window.Editor.CurrentSlideIndex;
            var selectedAfterCrossSlideNext = window.LastCommentPanePlan!.SelectedComment!.TextPreview;
            var previousAcrossEmptySlide = window.NavigateReviewComment(PresentationReviewWorkflowIntentKind.PreviousComment);
            var slideAfterPrevious = window.Editor.CurrentSlideIndex;
            var selectedAfterPrevious = window.LastCommentPanePlan!.SelectedComment!.TextPreview;

            sameSlideNext.ShouldNavigate.Should().BeTrue();
            sameSlideNext.TargetSlideIndex.Should().Be(0);
            sameSlideNext.TargetCommentIndex.Should().Be(1);
            slideAfterSameSlideNext.Should().Be(0);
            selectedAfterSameSlideNext.Should().Be("Second thread.");
            crossSlideNext.ShouldNavigate.Should().BeTrue();
            crossSlideNext.TargetSlideIndex.Should().Be(2);
            crossSlideNext.TargetCommentIndex.Should().Be(0);
            slideAfterCrossSlideNext.Should().Be(2);
            selectedAfterCrossSlideNext.Should().Be("Third thread.");
            previousAcrossEmptySlide.ShouldNavigate.Should().BeTrue();
            previousAcrossEmptySlide.TargetSlideIndex.Should().Be(0);
            previousAcrossEmptySlide.TargetCommentIndex.Should().Be(1);
            slideAfterPrevious.Should().Be(0);
            selectedAfterPrevious.Should().Be("Second thread.");
            window.IsDirty.Should().Be(dirtyBeforeNavigation, "comment navigation should not change document dirty state");
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
                "Picture \"Product image\" (PNG image) on slide \"Slide 1\". Describe the important visual details and context.");
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
    public void MainWindow_ReadingOrderCommand_ShowsSharedPlanBackedPane()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var chart = new SlideShape
            {
                Id = 501,
                Name = "Sales chart",
                Kind = SlideShapeKind.Chart,
                Chart = new ChartShape(),
                AlternativeTextTitle = "Regional sales",
                AlternativeText = "Quarterly sales by region.",
            };
            var group = new SlideShape
            {
                Id = 502,
                Name = "Grouped layout",
                Kind = SlideShapeKind.Group,
                Children =
                {
                    new SlideShape
                    {
                        Id = 503,
                        Name = "Decorative flourish",
                        Kind = SlideShapeKind.Picture,
                        Picture = new ImagePart(),
                        IsDecorative = true,
                    },
                    new SlideShape
                    {
                        Id = 504,
                        Name = "Grouped label",
                        Kind = SlideShapeKind.AutoShape,
                        Text = "Grouped label",
                    }
                }
            };
            window.Editor.CurrentSlide!.Shapes.Clear();
            window.Editor.CurrentSlide!.Shapes.Add(chart);
            window.Editor.CurrentSlide.Shapes.Add(group);
            window.Editor.Select(chart.Id);

            var registry = FreePRibbonCommands.Build(
                new RibbonStateStore(),
                window.Editor,
                onReviewReadingOrder: () => window.ShowReadingOrderPane());
            registry.TryGet(PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, out var command)
                .Should().BeTrue();

            command!.Execute(RibbonCommandContext.Empty);

            window.IsReadingOrderPaneVisible.Should().BeTrue();
            window.ReadingOrderPaneItemCount.Should().Be(4);
            window.ReadingOrderPaneHeading.Should().Be("Reading Order - slide 1 (4 shapes)");
            window.ReadingOrderPaneMessage.Should().Be("Selected: Sales chart");
            window.LastReadingOrderPlan.Should().NotBeNull();
            window.LastReadingOrderPlan!.SelectedItem.Should().NotBeNull();
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(chart.Id);
            window.LastReadingOrderPlan.Items.Single(item => item.ShapeId == 503).Should().Match<PresentationReadingOrderItemPlan>(item =>
                item.NestingDepth == 1 &&
                item.IsDecorative &&
                item.AccessibilitySummary == "Decorative");
            window.IsReadingOrderMoveEarlierEnabled.Should().BeFalse();
            window.IsReadingOrderMoveLaterEnabled.Should().BeTrue();
            window.ReadingOrderMoveEarlierDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
            window.ReadingOrderMoveLaterDisabledReason.Should().BeNull();
            window.LastReadingOrderPlan.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
                .Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);

            var mutation = window.ApplyReadingOrderMoveLater();

            mutation.Should().Be(new PresentationReadingOrderMutationPlan(
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                true,
                0,
                chart.Id,
                0,
                1,
                null));
            window.Editor.CurrentSlide.Shapes.Select(shape => shape.Id).Should().Equal(502u, 501u);
            window.LastReadingOrderPlan!.Items.Select(item => item.ShapeId).Should().Equal(502u, 503u, 504u, 501u);
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(chart.Id);
            window.IsReadingOrderMoveEarlierEnabled.Should().BeTrue();
            window.IsReadingOrderMoveLaterEnabled.Should().BeFalse();
            window.ReadingOrderMoveLaterDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage);

            window.Editor.Select(503);
            window.ShowReadingOrderPane();
            window.IsReadingOrderMoveEarlierEnabled.Should().BeFalse();
            window.ReadingOrderMoveEarlierDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
            window.IsReadingOrderMoveLaterEnabled.Should().BeTrue();
            window.LastReadingOrderPlan!.Actions.Single(action =>
                    action.CommandId == PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
                .Status.Should().Be(PresentationWorkflowCapabilityStatus.Available);

            var nestedMutation = window.ApplyReadingOrderMoveLater();

            nestedMutation.Should().Be(new PresentationReadingOrderMutationPlan(
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                true,
                0,
                503,
                0,
                1,
                null));
            group.Children.Select(shape => shape.Id).Should().Equal(504u, 503u);
            window.LastReadingOrderPlan!.Items.Select(item => item.ShapeId).Should().Equal(502u, 504u, 503u, 501u);
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(503);
            window.IsReadingOrderMoveLaterEnabled.Should().BeFalse();
            window.ReadingOrderMoveLaterDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage);

            var selection = window.ApplyReadingOrderSelectItem(504);

            selection.Should().Be(new PresentationReadingOrderSelectionPlan(
                PresentationReviewWorkflowIntentKind.SelectReadingOrderItem,
                true,
                0,
                504,
                1,
                null));
            window.Editor.SelectedShapeIds.Should().Equal(504u);
            window.LastReadingOrderPlan!.SelectedItem.Should().NotBeNull();
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(504);
            window.ReadingOrderPaneMessage.Should().Be("Selected: Grouped label");
            window.IsReadingOrderMoveEarlierEnabled.Should().BeFalse();
            window.ReadingOrderMoveEarlierDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage);
            window.IsReadingOrderMoveLaterEnabled.Should().BeTrue();
            window.ReadingOrderMoveLaterDisabledReason.Should()
                .BeNull();

            window.Editor.Undo();
            group.Children.Select(shape => shape.Id).Should().Equal(503u, 504u);
            window.Editor.CurrentSlide.Shapes.Select(shape => shape.Id).Should().Equal(502u, 501u);
            window.Editor.SelectedShapeIds.Should().Equal(504u);
            window.Editor.Redo();
            window.Editor.CurrentSlide.Shapes.Select(shape => shape.Id).Should().Equal(502u, 501u);
            group.Children.Select(shape => shape.Id).Should().Equal(504u, 503u);
            window.Editor.SelectedShapeIds.Should().Equal(504u);
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
    public void MainWindow_SmartArtTextPane_RendersSharedOutlineAndRoutesKeyboard()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.ShowSmartArtTextPane();
            window.IsSmartArtTextPaneVisible.Should().BeTrue();
            window.SmartArtTextPaneMessage.Should().Be("Select a SmartArt graphic to edit its text outline.");

            var shape = MakeSmartArtShape();
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var outline = window.ShowSmartArtTextPane();
            outline.Select(row => row.Text).Should().Equal("Plan", "Build");
            window.SmartArtTextPaneActionButtonCount.Should().Be(8);
            window.SmartArtTextPaneEnabledActionButtonCount.Should().Be(8);
            window.SmartArtTextPaneRenderedRows.Should().Equal(
                "n1|0|False|Plan",
                "n2|0|False|Build");
            window.SmartArtTextPaneSelectedRowCount.Should().Be(1);

            window.SetSmartArtTextPaneRowText(0, "Discover");
            var apply = window.ApplySmartArtTextPane();

            apply.Applied.Should().BeTrue();
            shape.SmartArt!.Data!.Nodes[0].Text.Should().Be("Discover");
            window.LastSmartArtDataPartRewriteResult!.Applied.Should().BeTrue();
            window.LastSmartArtDrawingCacheRegenerationResult!.Applied.Should().BeTrue();
            shape.SmartArt.FallbackShapes.Should().NotBeEmpty();
            window.IsDirty.Should().BeTrue();

            window.Editor.Undo();
            shape.SmartArt.Data!.Nodes[0].Text.Should().Be("Plan");
            window.SmartArtTextPaneRenderedRows.Should().Contain("n1|0|False|Plan");
            window.Editor.Redo();
            shape.SmartArt.Data.Nodes[0].Text.Should().Be("Discover");
            window.SmartArtTextPaneRenderedRows.Should().Contain("n1|0|False|Discover");

            var addSibling = window.ApplySmartArtTextPaneKeyboardRouteForTests(
                SmartArtTextPaneShortcutKey.Enter,
                SmartArtTextPaneShortcutModifiers.None,
                "n1");
            addSibling!.Applied.Should().BeTrue();
            window.LastSmartArtTextPaneKeyboardRoute!.RouteId.Should().Be("smartart.text-pane.enter.add-sibling-after");
            window.SmartArtTextPaneRowCount.Should().Be(3);

            var addChild = window.ApplySmartArtTextPaneKeyboardRouteForTests(
                SmartArtTextPaneShortcutKey.Enter,
                SmartArtTextPaneShortcutModifiers.Control,
                "n2");
            addChild!.Applied.Should().BeTrue();
            window.SmartArtTextPaneRenderedRows.Should().Contain(row => row.Contains("|1|False|New node", StringComparison.Ordinal));

            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.MoveUp, "n2")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.MoveDown, "n2")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.Promote, "freep-smartart-node-4")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.Demote, "freep-smartart-node-3")!.Applied.Should().BeTrue();
            window.ApplySmartArtTextPaneEditForTests(SmartArtNodeEditKind.Remove, "freep-smartart-node-3")!.Applied.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SmartArtTextPane_TogglesAssistantThroughUndoablePackageRefresh()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var shape = MakeSmartArtShape();
            shape.SmartArt!.Data!.Family = SmartArtFamily.Hierarchy;
            shape.SmartArt.Data.LayoutUniqueId =
                "urn:microsoft.com/office/officeart/2005/8/layout/orgChart";
            var root = shape.SmartArt.Data.Nodes[0];
            var child = shape.SmartArt.Data.Nodes[1];
            shape.SmartArt.Data.Nodes.RemoveAt(1);
            child.Level = 1;
            root.Children.Add(child);
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            window.ShowSmartArtTextPane();

            var result = window.ToggleSmartArtTextPaneAssistantForTests("n2");

            result.Should().NotBeNull();
            result!.Applied.Should().BeTrue();
            shape.SmartArt.Data!.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();
            window.SmartArtTextPaneRenderedRows.Should().Contain(row => row.Contains("|1|True|Build", StringComparison.Ordinal));
            window.Editor.Undo();
            shape.SmartArt.Data.Nodes[0].Children.Single().IsAssistant.Should().BeFalse();
            window.Editor.Redo();
            shape.SmartArt.Data.Nodes[0].Children.Single().IsAssistant.Should().BeTrue();

            var addAssistant = window.ApplySmartArtTextPaneEditForTests(
                SmartArtNodeEditKind.AddAssistant,
                "n1");
            addAssistant!.Applied.Should().BeTrue();
            shape.SmartArt.Data.Nodes[0].Children.Should().ContainSingle(child =>
                child.IsAssistant && child.Text == "Assistant");
            window.Editor.Undo();
            shape.SmartArt.Data.Nodes[0].Children.Should().ContainSingle(child =>
                child.ModelId == "n2" && child.IsAssistant);
            window.Editor.Redo();
            shape.SmartArt.Data.Nodes[0].Children.Should().Contain(child =>
                child.Text == "Assistant" && child.IsAssistant);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SmartArtColorPreset_UsesNativePartAndUndoBus()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var shape = MakeSmartArtShape();
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);
            var before = shape.SmartArt!.Parts["ppt/diagrams/colors1.xml"].Bytes.ToArray();

            var result = window.ApplySmartArtColorPresetForTests(SmartArtColorPreset.Grayscale);

            result.Applied.Should().BeTrue();
            shape.SmartArt.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().NotEqual(before);
            shape.SmartArt.Colors!.Palette.Should().HaveCount(2);
            window.Editor.Undo();
            shape.SmartArt.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().Equal(before);
            window.Editor.Redo();
            shape.SmartArt.Parts["ppt/diagrams/colors1.xml"].Bytes.Should().NotEqual(before);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SmartArtColorPreset_CreatesMissingPartAndUndoRestoresIt()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var shape = MakeSmartArtShape();
            var smartArt = shape.SmartArt!;
            smartArt.Parts.Remove("ppt/diagrams/colors1.xml");
            smartArt.DiagramRelIds.Remove("cs");
            window.Editor.CurrentSlide!.Shapes.Add(shape);
            window.Editor.Select(shape.Id);

            var result = window.ApplySmartArtColorPresetForTests(SmartArtColorPreset.SingleAccent);

            result.Applied.Should().BeTrue();
            result.PartPath.Should().NotBeNull();
            smartArt.Parts.Should().ContainKey(result.PartPath!);
            smartArt.DiagramRelIds.Should().ContainKey("cs");

            window.Editor.Undo();
            smartArt.Parts.Should().NotContainKey(result.PartPath!);
            smartArt.DiagramRelIds.Should().NotContainKey("cs");

            window.Editor.Redo();
            smartArt.Parts.Should().ContainKey(result.PartPath!);
            smartArt.DiagramRelIds.Should().ContainKey("cs");
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
    public void MainWindow_VideoExportRequest_RecordsSharedFramePackage()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.InsertSlide();

            var package = window.RefreshVideoFramePackage(new PresentationVideoExportRequest(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [2, 1, 2]),
                PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 8));
            var plan = package.Plan.ExportPlan;

            window.LastVideoFramePackage.Should().BeSameAs(package);
            window.LastVideoExportPlan.Should().NotBeNull();
            window.LastVideoExportPlan!.CommandId.Should().Be(plan.CommandId);
            window.LastVideoExportHandoffPlan.Should().NotBeNull();
            window.LastVideoExportHandoffPlan!.PackagePlan.Should().BeSameAs(package.Plan);
            window.LastVideoExportHandoffPlan.Status.Should()
                .Be(PresentationVideoExportHandoffStatus.HostEncoderReady);
            window.LastVideoExportHandoffPlan.StatusText.Should()
                .Be("WPF Windows video export host: host MP4 encoder ready");
            plan.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
            plan.SlideRange.SlideNumbers.Should().Equal(1, 2);
            plan.Quality.Quality.Should().Be(PresentationVideoQualityKind.Standard);
            plan.Quality.WidthPx.Should().Be(852);
            plan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(16));
            plan.Storyboard.SlideRange.SlideNumbers.Should().Equal(1, 2);
            plan.Storyboard.Segments.Select(segment => segment.SlideNumber).Should().Equal(1, 2);
            plan.Storyboard.Segments.Should().OnlyContain(segment =>
                segment.Duration == TimeSpan.FromSeconds(8) &&
                segment.TimingSource == PresentationVideoTimingSource.DefaultDuration);
            plan.Storyboard.OutputWidthPx.Should().Be(852);
            plan.Storyboard.OutputHeightPx.Should().Be(480);
            plan.Storyboard.FrameRateHint.Should().Be(24);
            plan.Storyboard.TotalDuration.Should().Be(plan.EstimatedDuration);
            plan.IsImplemented.Should().BeTrue();
            plan.CanExecute.Should().BeTrue();
            plan.DisabledReason.Should().BeNull();
            package.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.EncoderDeferred);
            package.Plan.DeferredCapabilities.Should().Contain(PresentationVideoFramePackageExecutor.Mp4EncoderDeferred);
            package.Frames.Select(frame => frame.FileName)
                .Should()
                .Equal("frames/slide-01-frame-0001.png", "frames/slide-02-frame-0002.png");
            package.Frames.Should().OnlyContain(frame => frame.WidthPx == 852 && frame.HeightPx == 480);
            package.Bytes.Length.Should().BeGreaterThan(100);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_PrintBackstageRequest_RecordsSharedPlanWithoutPackageExecution()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges,
            nativePrintCapability: WpfNativePrintCapability.Unavailable("Test printer handoff deferred."));
        try
        {
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.SelectSlide(1);

            var plan = window.RefreshPrintBackstagePlan(new PresentationPrintRequest(
                PresentationPrintLayoutKind.NotesPages,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CurrentSlide,
                    CurrentSlideNumber: 2)));

            window.LastPrintBackstagePlan.Should().BeSameAs(plan);
            window.LastPrintOutputPackage.Should().BeNull("Backstage Print planning must not render a printable package");
            plan.SelectedLayout.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
            plan.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CurrentSlide);
            plan.RangeChoices.Single(choice => choice.Kind == PresentationSlideRangeKind.CurrentSlide)
                .DisplayName.Should().Be("Current Slide (Slide 2)");
            plan.PageCount.Should().Be(1);
            plan.PreviewPlan.Pages.Should().ContainSingle()
                .Which.Should().Match<PresentationPrintPreviewPage>(page =>
                    page.PageIndex == 0 &&
                    page.PageNumber == 1 &&
                    page.Kind == PresentationPrintPreviewPageKind.NotesPage &&
                    page.SlideNumbers.SequenceEqual(new[] { 2 }) &&
                    page.ThumbnailLabel == "Slide 2 notes" &&
                    page.Detail == "Notes page for slide 2");
            plan.NativePrinterDialogDeferred.Should().BeTrue();
            plan.NativePrintHandoff.Status.Should().Be(PresentationNativePrintHandoffStatus.HostPrinterUnavailableDeferredByHost);
            plan.NativePrintHandoff.IsPackageReady.Should().BeTrue();
            plan.NativePrintHandoff.RequiresHostHandoff.Should().BeTrue();
            plan.NativePrintHandoff.CanOpenNativePrintDialog.Should().BeFalse();
            plan.NativePrintHandoff.Reason.Should().Contain("Test printer handoff deferred");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_PrintBackstageRequest_ParsesCustomRangeThroughWpfAdapter()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges,
            nativePrintCapability: WpfNativePrintCapability.Unavailable("Test printer handoff deferred."));
        try
        {
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            var plan = window.RefreshPrintBackstagePlan(new PresentationPrintRequest(
                PresentationPrintLayoutKind.FullPageSlides,
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.CustomRange,
                    CustomRangeText: "2,4")));

            plan.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
            plan.SelectedRange.Request!.CustomRangeText.Should().Be("2,4");
            plan.SelectedRange.DisplayName.Should().Be("Slides 2, 4");
            plan.SelectedRange.IsAvailable.Should().BeTrue();
            plan.PageCount.Should().Be(2);
            plan.CanBuildPackage.Should().BeTrue();
            window.LastPrintOutputPackage.Should().BeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_BackstageCustomRangeInput_RebuildsPrintPlanAndCarriesRangeToPrintAction()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges,
            nativePrintCapability: WpfNativePrintCapability.Unavailable("Test printer handoff deferred."));
        try
        {
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();
            window.Editor.InsertSlide();

            window.ShowBackstageForTests();
            window.ActivateBackstageEntryForTests("Print").Should().BeTrue();
            window.ApplyBackstagePrintCustomRangeForTests("2,4").Should().BeTrue();

            window.LastFilePrintBackstagePlanForTests.Should().NotBeNull();
            window.LastFilePrintBackstagePlanForTests!.SelectedRange.Kind.Should().Be(PresentationSlideRangeKind.CustomRange);
            window.LastFilePrintBackstagePlanForTests.SelectedRange.Request!.CustomRangeText.Should().Be("2,4");
            window.LastFilePrintBackstagePlanForTests.PageCount.Should().Be(2);
            window.LastFilePrintBackstagePlanForTests.CanBuildPackage.Should().BeTrue();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_NotesPagePdfRequest_RecordsSharedRenderPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "Opening";
            window.Editor.CurrentSlide.Notes = MakeTextBody("Opening note");
            window.Editor.InsertSlide();

            var plan = window.RefreshNotesPagePdfRenderPlan(new PresentationSlideRangeRequest(
                PresentationSlideRangeKind.CurrentSlide,
                CurrentSlideNumber: 1));

            window.LastNotesPagePdfRenderPlan.Should().BeSameAs(plan);
            plan.PrintPlan.CommandId.Should().Be(PresentationExportPlanner.PrintCommandId);
            plan.PrintPlan.Layout.Layout.Should().Be(PresentationPrintLayoutKind.NotesPages);
            plan.PrintPlan.SlideRange.SlideNumbers.Should().Equal(1);
            plan.PreviewPlans.Should().ContainSingle(preview =>
                preview.SlideNumber == 1 &&
                preview.NoteLines.Count == 1 &&
                preview.NoteLines[0] == "Opening note");
            plan.Pages.Should().ContainSingle();
            plan.Pages[0].Ops.OfType<Free.Shared.Pdf.PdfText>().Select(text => text.Text)
                .Should()
                .Contain(["Opening", "Opening note"]);
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
        var readingOrderInvoked = false;
        var proofingInvoked = false;
        var addInvoked = false;
        var editInvoked = false;
        var replyInvoked = false;
        var deleteInvoked = false;
        var previousInvoked = false;
        var nextInvoked = false;

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onReviewAccessibility: () => invoked = true,
            onReviewAltText: () => altTextInvoked = true,
            onReviewReadingOrder: () => readingOrderInvoked = true,
            onReviewProofing: () => proofingInvoked = true,
            onAddComment: () => addInvoked = true,
            onEditComment: () => editInvoked = true,
            onReplyComment: () => replyInvoked = true,
            onDeleteComment: () => deleteInvoked = true,
            onPreviousComment: () => previousInvoked = true,
            onNextComment: () => nextInvoked = true);

        registry.TryGet(PresentationReviewWorkflowPlanner.AccessibilityCommandId, out var command)
            .Should()
            .BeTrue("WPF should expose the shared review accessibility intent through its command registry");

        command!.Execute(RibbonCommandContext.Empty);
        invoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.AltTextCommandId, out var altTextCommand).Should().BeTrue();
        altTextCommand!.Execute(RibbonCommandContext.Empty);
        altTextInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.ReadingOrderPaneCommandId, out var readingOrderCommand)
            .Should().BeTrue();
        readingOrderCommand!.Execute(RibbonCommandContext.Empty);
        readingOrderInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.CommentsPaneCommandId, out _).Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.AddCommentCommandId, out var addCommand).Should().BeTrue();
        addCommand!.Execute(RibbonCommandContext.Empty);
        addInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.EditCommentCommandId, out var editCommand).Should().BeTrue();
        editCommand!.Execute(RibbonCommandContext.Empty);
        editInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.ReplyCommentCommandId, out var replyCommand).Should().BeTrue();
        replyCommand!.Execute(RibbonCommandContext.Empty);
        replyInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.DeleteCommentCommandId, out var deleteCommand).Should().BeTrue();
        deleteCommand!.Execute(RibbonCommandContext.Empty);
        deleteInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.PreviousCommentCommandId, out var previousCommand).Should().BeTrue();
        previousCommand!.Execute(RibbonCommandContext.Empty);
        previousInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.NextCommentCommandId, out var nextCommand).Should().BeTrue();
        nextCommand!.Execute(RibbonCommandContext.Empty);
        nextInvoked.Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.ReopenCommentCommandId, out _).Should().BeTrue();
        registry.TryGet(PresentationReviewWorkflowPlanner.ProofingCommandId, out var proofingCommand).Should().BeTrue();
        proofingCommand!.Execute(RibbonCommandContext.Empty);
        proofingInvoked.Should().BeTrue();
    }

    private static TextBody MakeTextBody(params string[] paragraphs)
    {
        var body = new TextBody();
        foreach (var text in paragraphs)
        {
            var paragraph = new Paragraph();
            paragraph.Runs.Add(new Run { Text = text });
            body.Paragraphs.Add(paragraph);
        }

        return body;
    }

    private static TextBody MakeTextBodyWithColor(string text, SrgbColor color)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Color = new ThemeAwareColor(color) });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static TextBody MakeLinkedTextBody(string text, Hyperlink hyperlink)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Hyperlink = hyperlink });
        body.Paragraphs.Add(paragraph);
        return body;
    }

    private static SlideShape MakeSmartArtShape()
    {
        var data = new SmartArtData
        {
            Family = SmartArtFamily.List,
            LayoutUniqueId = "urn:microsoft.com/office/officeart/2005/8/layout/verticalBoxList",
            IsLiveLayoutSupported = true
        };
        data.Nodes.Add(new SmartArtNode { ModelId = "n1", Text = "Plan", Level = 0 });
        data.Nodes.Add(new SmartArtNode { ModelId = "n2", Text = "Build", Level = 0 });

        var smartArt = new SmartArtShape
        {
            Data = data,
            DrawingPartPath = "ppt/diagrams/drawing1.xml"
        };
        smartArt.Parts["ppt/diagrams/data1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/data1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramData+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:dataModel xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/drawing1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/drawing1.xml",
            ContentType = "application/vnd.ms-office.drawingml.diagramDrawing+xml",
            Bytes = Encoding.UTF8.GetBytes("<dsp:drawing xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" />")
        };
        smartArt.Parts["ppt/diagrams/colors1.xml"] = new DiagramPart
        {
            PartPath = "ppt/diagrams/colors1.xml",
            ContentType = "application/vnd.openxmlformats-officedocument.drawingml.diagramColors+xml",
            Bytes = Encoding.UTF8.GetBytes("<dgm:colorsDef xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><dgm:styleLbl name=\"node0\"><dgm:fillClrLst><a:schemeClr val=\"accent1\"/><a:schemeClr val=\"accent2\"/></dgm:fillClrLst></dgm:styleLbl></dgm:colorsDef>")
        };

        return new SlideShape
        {
            Id = 970,
            Name = "Roadmap SmartArt",
            Kind = SlideShapeKind.SmartArt,
            OffsetXEmu = 914_400,
            OffsetYEmu = 457_200,
            ExtentCxEmu = 4_572_000,
            ExtentCyEmu = 2_743_200,
            SmartArt = smartArt
        };
    }

    [Fact]
    public void MainWindow_Source_UsesPlannerForCommentPaneAndReviewState()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "MainWindow.cs"));

        source.Should().Contain("PresentationReviewWorkflowSession");
        source.Should().Contain("_reviewWorkflowSession.RefreshReviewWorkflowPlans();");
        source.Should().Contain("_reviewWorkflowSession.ApplySelectedShapeAlternativeText(");
        source.Should().Contain("_reviewWorkflowSession.ApplyProofingCorrection(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAccessibilitySummaryPlan(_presentation)");
        source.Should().Contain("PresentationReviewWorkflowPlanner.NormalizeAccessibilityCheckerRowSelection(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildAccessibilityCheckerNavigationPlan(");
        source.Should().Contain("_reviewWorkflowSession.RefreshAltTextPlans(");
        source.Should().Contain("_reviewWorkflowSession.ApplyReadingOrderMove(");
        source.Should().Contain("_reviewWorkflowSession.SelectReadingOrderItem(");
        source.Should().Contain("_reviewWorkflowSession.RefreshReadingOrderPlan();");
        source.Should().Contain("_reviewWorkflowSession.RefreshProofingRequestPlan();");
        source.Should().Contain("PresentationMediaTranscriptPlanner.BuildCaptionAuthoringPanePlan(");
        source.Should().Contain("PresentationMediaTranscriptPlanner.BuildCaptionAuthoringMutationPlan(");
        source.Should().Contain("Editor.ApplyMediaCaptionAuthoring(");
        source.Should().Contain("RenderCommentPane(PresentationCommentPanePlan plan)");
        source.Should().Contain("cm.AuthorDisplayName");
        source.Should().Contain("cm.InitialsBadgeText");
        source.Should().Contain("cm.ThreadStatusLabel");
        source.Should().Contain("reply.AuthorDisplayName");
        source.Should().Contain("onLayoutPicker:     () => OpenLayoutPicker()");
        source.Should().Contain("PresentationDesignCommandPlanner.BuildLayoutPickerPlan(");
        source.Should().Contain("PresentationDesignCommandPlanner.TryApplyLayoutChoice(");
        source.Should().Contain("ShowLayoutPicker(LastLayoutPickerPlan);");
        source.Should().Contain("BuildLayoutChoiceLabel(choice)");

        var selectionPaneSource = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Host",
            "SelectionPane.cs"));
        selectionPaneSource.Should().Contain("_editor.SetShapeName(");
        selectionPaneSource.Should().Contain("Key.Enter");
        selectionPaneSource.Should().Contain("rename.LostFocus");
        selectionPaneSource.Should().Contain("_editor.MoveSelectedShapeInReadingOrder(");
        selectionPaneSource.Should().Contain("item.CanMoveUp");
        selectionPaneSource.Should().Contain("item.CanMoveDown");
        source.Should().Contain("BuildLayoutChoiceTile(choice)");
        source.Should().Contain("BuildLayoutThumbnail(choice)");
        source.Should().NotContain("Modern resolved-thread state is not modeled yet.\";");
    }

}
