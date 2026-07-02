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
            false,
            PresentationCommentThreadStatus.Open));

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
    public void BuildReadingOrderPlan_EnablesTopLevelMovesOnlyWhenDirectionIsAvailable()
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
                new SlideShape { Id = 4, Name = "Nested child" }
            }
        });

        var first = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [1]);
        var middle = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [2]);
        var last = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [3]);
        var nested = PresentationReviewWorkflowPlanner.BuildReadingOrderPlan(slide, 0, [4]);

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
        Action(nested, PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId)
            .Should().Be(new PresentationReviewWorkflowActionPlan(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                false,
                PresentationWorkflowCapabilityStatus.Deferred,
                PresentationReviewWorkflowPlanner.NestedReadingOrderReorderDeferredMessage));
        Action(nested, PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId)
            .DisabledReason.Should().Be(PresentationReviewWorkflowPlanner.NestedReadingOrderReorderDeferredMessage);

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
                new SlideShape { Id = 4, Name = "Nested child" }
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
        editor.Select(4);
        var nested = PresentationReviewWorkflowPlanner.TryApplyReadingOrderMove(
            editor,
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater);

        earlier.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
            true,
            0,
            middle.Id,
            1,
            0,
            null));
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u, 3u);
        group.Children.Select(shape => shape.Id).Should().Equal(4u);
        boundary.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
            false,
            0,
            middle.Id,
            -1,
            -1,
            PresentationReviewWorkflowPlanner.ReadingOrderAlreadyEarliestMessage));
        nested.Should().Be(new PresentationReadingOrderMutationPlan(
            PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
            false,
            0,
            4,
            -1,
            -1,
            PresentationReviewWorkflowPlanner.NestedReadingOrderReorderDeferredMessage));
        slide.Shapes.Select(shape => shape.Id).Should().Equal(2u, 1u, 3u);
        group.Children.Select(shape => shape.Id).Should().Equal(4u);
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
            PresentationWorkflowCapabilityStatus.RequiresHost,
            2,
            1,
            1,
            PresentationReviewWorkflowPlanner.ProofingRequiresHostMessage));
    }

    private static TextBody TextBody(string text)
    {
        var body = new TextBody();
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run { Text = text });
        body.Paragraphs.Add(paragraph);
        return body;
    }
}
