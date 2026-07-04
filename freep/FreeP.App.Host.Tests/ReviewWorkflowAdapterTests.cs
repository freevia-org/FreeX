using System.IO;
using System.Text;
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
                row.CommandHint == null &&
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

            var selectedTitle = window.SelectAccessibilityCheckerRow(3);

            window.Editor.CurrentSlideIndex.Should().Be(1);
            window.Editor.SelectedShapeIds.Should().BeEmpty();
            selectedTitle.SelectedRow.Should().NotBeNull();
            selectedTitle.SelectedRow!.Title.Should().Be("Missing slide title");

            var actionedTitle = window.ApplyAccessibilityCheckerRowAction(3);

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
                "Unclear hyperlink text",
                "Chart title missing");
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
                    row.ActionLabel == "Select Media" &&
                    row.CommandHint == null &&
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
    public void MainWindow_ApplyProofingCorrection_UsesSharedMutationAndRefreshesPlans()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.CurrentSlide!.Title = "intro deck";
            window.Editor.CurrentSlide.Shapes.Add(new SlideShape
            {
                Id = 904,
                Name = "Caption",
                Text = "caption text"
            });
            window.RefreshReviewWorkflowPlans();
            var scope = window.LastProofingExecutionPlan!.Scopes.Single(s =>
                s.Kind == PresentationProofingScopeKind.SlideTitle);
            var start = scope.Text.IndexOf('i');

            var pane = window.ShowProofingPane();

            window.IsProofingPaneVisible.Should().BeTrue();
            window.ProofingPaneIssueRowCount.Should().Be(2);
            window.ProofingPaneSelectedIssueCount.Should().Be(1);
            window.IsProofingPaneCorrectionEnabled.Should().BeTrue();
            window.ProofingPaneHeading.Should().Be("Spelling - 2 issues");
            pane.SelectedRow!.SuggestedReplacement.Should().Be("I");

            var mutation = window.ApplyProofingCorrection(scope, start, 1, "I");
            var selectedCaption = window.SelectProofingIssueRow(0);
            var paneMutation = window.ApplySelectedProofingCorrection();

            mutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
                true,
                scope,
                start,
                1,
                "I",
                "Intro deck",
                null));
            paneMutation.Should().Be(new PresentationProofingCorrectionMutationPlan(
                true,
                selectedCaption.SelectedRow!.Scope,
                selectedCaption.SelectedRow.Start,
                selectedCaption.SelectedRow.Length,
                "C",
                "Caption text",
                null));
            window.Editor.CurrentSlide.Title.Should().Be("Intro deck");
            window.Editor.CurrentSlide.Shapes.Single(shape => shape.Id == 904).Text.Should().Be("Caption text");
            window.LastProofingRequestPlan.Should().NotBeNull();
            window.LastProofingExecutionPlan.Should().NotBeNull();
            window.LastProofingExecutionPlan!.Scopes.Single(s =>
                    s.Kind == PresentationProofingScopeKind.SlideTitle)
                .Text.Should().Be("Intro deck");
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
            window.IsReadingOrderMoveLaterEnabled.Should().BeTrue();
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
            window.LastVideoExportPlan.Should().BeSameAs(plan);
            window.LastVideoExportHandoffPlan.Should().NotBeNull();
            window.LastVideoExportHandoffPlan!.PackagePlan.Should().BeSameAs(package.Plan);
            window.LastVideoExportHandoffPlan.Status.Should()
                .Be(PresentationVideoExportHandoffStatus.EncoderInputPackageReadyHostDeferred);
            window.LastVideoExportHandoffPlan.StatusText.Should()
                .Be("WPF video export host: MP4 encoder deferred; frame package ready");
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
            plan.CanExecute.Should().BeFalse();
            plan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);
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
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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
            plan.NativePrintHandoff.Reason.Should().Contain("Native printer handoff adapter is not wired");
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

    private static TextBody MakeLinkedTextBody(string text, Hyperlink hyperlink)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text, Hyperlink = hyperlink });
        body.Paragraphs.Add(paragraph);
        return body;
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
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildProofingExecutionPlan(_presentation)");
        source.Should().Contain("PresentationReviewWorkflowPlanner.BuildProofingRequestPlan(_presentation)");
        source.Should().Contain("LastCommentPanePlan = plan;");
        source.Should().Contain("cm.AuthorDisplayName");
        source.Should().Contain("cm.InitialsBadgeText");
        source.Should().Contain("cm.ThreadStatusLabel");
        source.Should().Contain("reply.AuthorDisplayName");
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
