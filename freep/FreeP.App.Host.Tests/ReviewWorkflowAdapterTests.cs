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
            window.ReadingOrderPaneItemCount.Should().Be(3);
            window.ReadingOrderPaneHeading.Should().Be("Reading Order - slide 1 (3 shapes)");
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
            window.LastReadingOrderPlan!.Items.Select(item => item.ShapeId).Should().Equal(502u, 503u, 501u);
            window.LastReadingOrderPlan.SelectedItem!.ShapeId.Should().Be(chart.Id);
            window.IsReadingOrderMoveEarlierEnabled.Should().BeTrue();
            window.IsReadingOrderMoveLaterEnabled.Should().BeFalse();
            window.ReadingOrderMoveLaterDisabledReason.Should()
                .Be(PresentationReviewWorkflowPlanner.ReadingOrderAlreadyLatestMessage);
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
    public void MainWindow_VideoExportRequest_RecordsSharedDeferredPlan()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.InsertSlide();

            var plan = window.RefreshVideoExportPlan(new PresentationVideoExportRequest(
                new PresentationSlideRangeRequest(
                    PresentationSlideRangeKind.SelectedSlides,
                    SelectedSlideNumbers: [2, 1, 2]),
                PresentationVideoQualityKind.Standard,
                SecondsPerSlide: 8));

            window.LastVideoExportPlan.Should().BeSameAs(plan);
            plan.CommandId.Should().Be(PresentationExportPlanner.VideoExportCommandId);
            plan.SlideRange.SlideNumbers.Should().Equal(1, 2);
            plan.Quality.Quality.Should().Be(PresentationVideoQualityKind.Standard);
            plan.Quality.WidthPx.Should().Be(852);
            plan.EstimatedDuration.Should().Be(TimeSpan.FromSeconds(16));
            plan.CanExecute.Should().BeFalse();
            plan.DisabledReason.Should().Be(PresentationExportPlanner.VideoExportDeferredMessage);
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

        var registry = FreePRibbonCommands.Build(
            new RibbonStateStore(),
            editor,
            onReviewAccessibility: () => invoked = true,
            onReviewAltText: () => altTextInvoked = true,
            onReviewReadingOrder: () => readingOrderInvoked = true);

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
        registry.TryGet(PresentationReviewWorkflowPlanner.ProofingCommandId, out _).Should().BeTrue();
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
