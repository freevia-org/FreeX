using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlidePanePlannerTests
{
    [Fact]
    public void BuildEntries_WithoutSections_ReturnsSlideRowsOnly()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" }
        };

        var entries = SlidePanePlanner.BuildEntries(slides, Array.Empty<PresentationSection>());

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"));
    }

    [Fact]
    public void BuildEntries_InsertsSectionHeadersBeforeFirstMemberSlide()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" }
        };
        var intro = new PresentationSection { Id = "intro-section", Name = "Intro" };
        intro.SlideIds.Add("rId2");
        var body = new PresentationSection { Id = "body-section", Name = "Body" };
        body.SlideIds.Add("rId3");
        body.SlideIds.Add("rId4");

        var entries = SlidePanePlanner.BuildEntries(slides, new[] { intro, body });

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 0, "Intro  (1)", 1, 0, "intro-section"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 1, "Body  (2)", 2, 1, "body-section"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 2, "3"));
    }

    [Fact]
    public void BuildEntries_UsesEarliestKnownMemberAndIgnoresUnknownSlideIds()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" }
        };
        var section = new PresentationSection { Id = "mixed-section", Name = "Mixed" };
        section.SlideIds.Add("missing");
        section.SlideIds.Add("rId4");
        section.SlideIds.Add("rId3");

        var entries = SlidePanePlanner.BuildEntries(slides, new[] { section });

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 1, "Mixed  (2)", 2, 0, "mixed-section"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 2, "3"));
    }

    [Fact]
    public void BuildEntries_CarriesSectionIndexForHeaderActions()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
        };
        var section = new PresentationSection { Id = "intro-section", Name = "Intro" };
        section.SlideIds.Add("rId2");

        var entries = SlidePanePlanner.BuildEntries(slides, new[] { section });

        entries[0].Kind.Should().Be(SlidePaneEntryKind.SectionHeader);
        entries[0].SectionIndex.Should().Be(0);
        entries[0].SectionId.Should().Be("intro-section");
    }

    [Fact]
    public void BuildEntries_CollapsedSection_OmitsMemberSlidesAndMarksHeaderCollapsed()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" },
            new Slide { Id = "rId5" }
        };
        var intro = new PresentationSection { Id = "intro-section", Name = "Intro" };
        intro.SlideIds.Add("rId2");
        var body = new PresentationSection { Id = "body-section", Name = "Body" };
        body.SlideIds.Add("rId3");
        body.SlideIds.Add("rId4");

        var entries = SlidePanePlanner.BuildEntries(
            slides,
            new[] { intro, body },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "body-section" });

        entries.Should().Equal(
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 0, "Intro  (1)", 1, 0, "intro-section"),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1"),
            new SlidePaneEntry(SlidePaneEntryKind.SectionHeader, 1, "Body  (2)", 2, 1, "body-section", true),
            new SlidePaneEntry(SlidePaneEntryKind.Slide, 3, "4"));
    }

    [Fact]
    public void BuildEntries_CollapsedSection_KeepsHeaderSlideCountAndIdentity()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" }
        };
        var section = new PresentationSection { Id = "section-1", Name = "Deck" };
        section.SlideIds.Add("rId2");
        section.SlideIds.Add("rId3");

        var entries = SlidePanePlanner.BuildEntries(
            slides,
            new[] { section },
            new HashSet<string> { "section-1" });

        entries.Should().ContainSingle();
        var header = entries[0];
        header.Kind.Should().Be(SlidePaneEntryKind.SectionHeader);
        header.SectionId.Should().Be("section-1");
        header.SectionSlideCount.Should().Be(2);
        header.IsSectionCollapsed.Should().BeTrue();
    }

    [Fact]
    public void BuildSessionProjection_ProjectsCollapseSelectionDragAndPaneEntries()
    {
        var slides = new[]
        {
            new Slide { Id = "rId2" },
            new Slide { Id = "rId3" },
            new Slide { Id = "rId4" }
        };
        var section = new PresentationSection { Id = "body", Name = "Body" };
        section.SlideIds.Add("rId3");
        section.SlideIds.Add("rId4");
        var drag = SlidePanePlanner.BeginDragSession(0, 12);
        var state = new SlidePaneSessionState(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BODY" },
            SelectedSlideIndex: 2,
            drag);

        var projection = SlidePanePlanner.BuildSessionProjection(
            slides,
            new[] { section },
            state);

        projection.SelectedSlideIndex.Should().Be(2);
        projection.DragSession.Should().BeSameAs(drag);
        projection.Entries.Select(entry => entry.Kind).Should().Equal(
            SlidePaneEntryKind.Slide,
            SlidePaneEntryKind.SectionHeader);
        projection.PaneEntries.Should().BeSameAs(projection.Entries);
        projection.PaneItemIsSlide.Should().Equal(true, false);

        var expanded = SlidePanePlanner.ToggleSection(state, "body");
        expanded.CollapsedSectionIds.Should().BeEmpty();
        expanded.SelectedSlideIndex.Should().Be(2);
        expanded.DragSession.Should().BeSameAs(drag);
    }

    [Fact]
    public void DefaultThumbnailMetrics_UseSixteenByNineSlideAspect()
    {
        SlidePanePlanner.DefaultThumbnailWidth.Should().Be(150.0);
        SlidePanePlanner.DefaultThumbnailHeight.Should().BeApproximately(84.375, 0.0001);
        SlidePanePlanner.DefaultSlideItemHeight.Should().BeApproximately(128.375, 0.0001);
    }

    [Fact]
    public void BuildThumbnailVisualPlan_ProjectsSharedMetricsSelectionAndMetadata()
    {
        var slide = new Slide { Id = "slide2" };
        slide.Title = "Quarterly Plan";
        slide.Shapes.Add(new SlideShape { Id = 99, Name = "Chart 1" });
        var entry = new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2");

        var plan = SlidePanePlanner.BuildThumbnailVisualPlan(entry, slide, currentSlideIndex: 1);

        plan.SlideIndex.Should().Be(1);
        plan.LabelText.Should().Be("2");
        plan.TitleText.Should().Be("Quarterly Plan");
        plan.ShapeCount.Should().Be(2);
        plan.IsSelected.Should().BeTrue();
        plan.ThumbnailWidth.Should().Be(SlidePanePlanner.DefaultThumbnailWidth);
        plan.ThumbnailHeight.Should().Be(SlidePanePlanner.DefaultThumbnailHeight);
        plan.ItemPadding.Should().Be(SlidePanePlanner.DefaultItemPadding);
        plan.LabelHeight.Should().Be(SlidePanePlanner.DefaultLabelHeight);
        plan.ItemHeight.Should().Be(SlidePanePlanner.DefaultSlideItemHeight);
        plan.ItemCornerRadius.Should().Be(SlidePanePlanner.DefaultItemCornerRadius);
        plan.NormalBorderThickness.Should().Be(SlidePanePlanner.DefaultNormalBorderThickness);
        plan.SelectedBorderThickness.Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);
        plan.PaneBackgroundHex.Should().Be(SlidePanePlanner.DefaultPaneBackgroundHex);
        plan.ItemNormalBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemNormalBackgroundHex);
        plan.ItemSelectedBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemSelectedBackgroundHex);
        plan.ItemHoverBackgroundHex.Should().Be(SlidePanePlanner.DefaultItemHoverBackgroundHex);
        plan.ItemNormalBorderHex.Should().Be(SlidePanePlanner.DefaultItemNormalBorderHex);
        plan.ItemSelectedBorderHex.Should().Be(SlidePanePlanner.DefaultItemSelectedBorderHex);
        plan.ThumbnailBorderHex.Should().Be(SlidePanePlanner.DefaultThumbnailBorderHex);
        plan.LabelForegroundHex.Should().Be(SlidePanePlanner.DefaultLabelForegroundHex);
        plan.CenterThumbnailContent.Should().Be(SlidePanePlanner.DefaultCenterThumbnailContent);
        plan.AccessibleName.Should().Be("Slide 2: Quarterly Plan, 2 objects");
        plan.ToolTipText.Should().Be(plan.AccessibleName);
    }

    [Fact]
    public void BuildThumbnailVisualPlan_BlankTitleUsesUntitledFallback()
    {
        var slide = new Slide { Id = "slide1" };
        var entry = new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1");

        var plan = SlidePanePlanner.BuildThumbnailVisualPlan(entry, slide, currentSlideIndex: 2);

        plan.IsSelected.Should().BeFalse();
        plan.TitleText.Should().Be("Untitled slide");
        plan.ShapeCount.Should().Be(0);
        plan.AccessibleName.Should().Be("Slide 1: Untitled slide, 0 objects");
    }

    [Fact]
    public void BuildThumbnailVisualPlan_RefreshesAccessibleNameFromEntryAndSlide()
    {
        var slide = new Slide { Id = "slide1", Title = "Opening" };
        var firstEntry = new SlidePaneEntry(SlidePaneEntryKind.Slide, 0, "1");

        SlidePanePlanner.BuildThumbnailVisualPlan(firstEntry, slide, currentSlideIndex: 0)
            .AccessibleName.Should().Be("Slide 1: Opening, 1 object");

        slide.Title = "Updated opening";
        var secondEntry = new SlidePaneEntry(SlidePaneEntryKind.Slide, 1, "2");

        SlidePanePlanner.BuildThumbnailVisualPlan(secondEntry, slide, currentSlideIndex: 0)
            .AccessibleName.Should().Be("Slide 2: Updated opening, 1 object");
    }

    [Fact]
    public void BuildSectionHeaderVisualPlan_ProjectsSharedChromeAndState()
    {
        var entry = new SlidePaneEntry(
            SlidePaneEntryKind.SectionHeader,
            SlideIndex: 2,
            Text: "Body  (3)",
            SectionSlideCount: 3,
            SectionIndex: 1,
            SectionId: "body-section",
            IsSectionCollapsed: true);

        var plan = SlidePanePlanner.BuildSectionHeaderVisualPlan(entry);

        plan.SlideIndex.Should().Be(2);
        plan.SectionIndex.Should().Be(1);
        plan.SectionId.Should().Be("body-section");
        plan.LabelText.Should().Be("Body  (3)");
        plan.SlideCount.Should().Be(3);
        plan.IsCollapsed.Should().BeTrue();
        plan.HeaderHeight.Should().Be(SlidePanePlanner.DefaultSectionHeaderHeight);
        plan.DisclosureWidth.Should().Be(SlidePanePlanner.DefaultSectionHeaderDisclosureWidth);
        plan.FontSize.Should().Be(SlidePanePlanner.DefaultSectionHeaderFontSize);
        plan.HorizontalPadding.Should().Be(SlidePanePlanner.DefaultSectionHeaderHorizontalPadding);
        plan.VerticalPadding.Should().Be(SlidePanePlanner.DefaultSectionHeaderVerticalPadding);
        plan.TopMargin.Should().Be(SlidePanePlanner.DefaultSectionHeaderTopMargin);
        plan.BottomMargin.Should().Be(SlidePanePlanner.DefaultSectionHeaderBottomMargin);
        plan.CornerRadius.Should().Be(SlidePanePlanner.DefaultSectionHeaderCornerRadius);
        plan.DisclosureText.Should().Be(SlidePanePlanner.DefaultSectionHeaderCollapsedDisclosureText);
        plan.BackgroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderBackgroundHex);
        plan.HoverBackgroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderHoverBackgroundHex);
        plan.ForegroundHex.Should().Be(SlidePanePlanner.DefaultSectionHeaderForegroundHex);
        plan.AccessibleName.Should().Be("Section Body  (3), collapsed");
        plan.ToolTipText.Should().Be("Expand section");
    }

    [Fact]
    public void BuildSectionHeaderVisualPlan_ExpandedHeaderUsesCollapseHint()
    {
        var entry = new SlidePaneEntry(
            SlidePaneEntryKind.SectionHeader,
            SlideIndex: 0,
            Text: "Intro  (1)",
            SectionSlideCount: 1,
            SectionIndex: 0,
            SectionId: "intro-section");

        var plan = SlidePanePlanner.BuildSectionHeaderVisualPlan(entry);

        plan.DisclosureText.Should().Be(SlidePanePlanner.DefaultSectionHeaderExpandedDisclosureText);
        plan.AccessibleName.Should().Be("Section Intro  (1), expanded");
        plan.ToolTipText.Should().Be("Collapse section");
    }

    [Fact]
    public void HitTestInsertionPoint_SkipsNonSlideRows()
    {
        var layout = new[] { false, true, true, false };

        SlidePanePlanner.HitTestInsertionPoint(layout, y: 10, slideItemHeight: 100).Should().Be(0);
        SlidePanePlanner.HitTestInsertionPoint(layout, y: 90, slideItemHeight: 100).Should().Be(1);
        SlidePanePlanner.HitTestInsertionPoint(layout, y: 190, slideItemHeight: 100).Should().Be(2);
    }

    [Fact]
    public void ComputeInsertionIndicatorOffset_AccumulatesRowsBeforeTargetSlide()
    {
        var layout = new[] { false, true, true, false };

        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 0, slideItemHeight: 100).Should().Be(0);
        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 1, slideItemHeight: 100).Should().Be(130);
        SlidePanePlanner.ComputeInsertionIndicatorOffset(layout, 2, slideItemHeight: 100).Should().Be(230);
    }

    [Fact]
    public void BuildDropVisualPlan_ProjectsSharedIndicatorVisualState()
    {
        var layout = new[] { false, true, true, false, true };

        var plan = SlidePanePlanner.BuildDropVisualPlan(
            layout,
            sourceSlideIndex: 0,
            targetSlideIndex: 2,
            slideItemHeight: 100);

        plan.SourceSlideIndex.Should().Be(0);
        plan.TargetSlideIndex.Should().Be(2);
        plan.IsTargetValid.Should().BeTrue();
        plan.IsMoveEnabled.Should().BeTrue();
        plan.IsVisible.Should().BeTrue();
        plan.IndicatorOffset.Should().Be(230);
        plan.IndicatorTopMargin.Should().Be(229);
        plan.IndicatorThickness.Should().Be(SlidePanePlanner.DefaultDropIndicatorThickness);
        plan.HorizontalInset.Should().Be(SlidePanePlanner.DefaultDropIndicatorHorizontalInset);
        plan.AccentColorHex.Should().Be(SlidePanePlanner.DefaultDropIndicatorAccentHex);
        plan.AutomationDescription.Should().Be("Move slide 1 to position 3");
    }

    [Theory]
    [InlineData(0, -1, false, false, false)]
    [InlineData(0, 0, true, false, true)]
    [InlineData(0, 1, true, false, true)]
    [InlineData(0, 2, true, true, true)]
    [InlineData(5, 1, true, false, false)]
    public void BuildDropVisualPlan_SeparatesTargetValidityFromMoveEnablement(
        int sourceSlideIndex,
        int targetSlideIndex,
        bool expectedTargetValid,
        bool expectedMoveEnabled,
        bool expectedVisible)
    {
        var layout = new[] { true, true, true };

        var plan = SlidePanePlanner.BuildDropVisualPlan(
            layout,
            sourceSlideIndex,
            targetSlideIndex,
            slideItemHeight: 100);

        plan.IsTargetValid.Should().Be(expectedTargetValid);
        plan.IsMoveEnabled.Should().Be(expectedMoveEnabled);
        plan.IsVisible.Should().Be(expectedVisible);
    }

    [Fact]
    public void DragSession_PlansThresholdTargetCompletionAndCancel()
    {
        var layout = new[] { true, true, true };
        var session = SlidePanePlanner.BeginDragSession(
            sourceSlideIndex: 0,
            startPointerY: 10);

        var belowThreshold = SlidePanePlanner.UpdateDragSession(
            session,
            layout,
            pointerYWithinItem: 12,
            pointerYWithinPane: 240,
            slideItemHeight: 100);

        belowThreshold.State.IsTracking.Should().BeTrue();
        belowThreshold.State.IsDragging.Should().BeFalse();
        belowThreshold.ShouldCapturePointer.Should().BeFalse();

        var active = SlidePanePlanner.UpdateDragSession(
            belowThreshold.State,
            layout,
            pointerYWithinItem: 20,
            pointerYWithinPane: 260,
            slideItemHeight: 100);

        active.State.IsDragging.Should().BeTrue();
        active.State.TargetSlideIndex.Should().Be(3);
        active.ShouldCapturePointer.Should().BeTrue();
        active.DropVisualPlan.IsMoveEnabled.Should().BeTrue();
        active.DropVisualPlan.IndicatorOffset.Should().Be(300);

        var completion = SlidePanePlanner.CompleteDragSession(active.State, slideCount: 3);

        completion.ShouldReleaseCapture.Should().BeTrue();
        completion.State.Should().Be(SlidePaneDragSessionState.None);
        completion.Action.Should().Be(new SlidePaneActionPlan(
            SlidePaneActionKind.MoveSlide,
            "Move Slide",
            0,
            3,
            true));

        SlidePanePlanner.CancelDragSession(active.State).Should().Be(SlidePaneDragSessionState.None);
    }

    [Fact]
    public void BuildContextActions_ForValidSlide_ReturnsSharedMenuOrderAndTargets()
    {
        var actions = SlidePanePlanner.BuildContextActions(slideCount: 3, slideIndex: 1);

        actions.Should().Equal(
            new SlidePaneActionPlan(SlidePaneActionKind.InsertAfterSlide, "New Slide", 1, 2, true),
            new SlidePaneActionPlan(SlidePaneActionKind.DuplicateSlide, "Duplicate Slide", 1, 2, true),
            new SlidePaneActionPlan(SlidePaneActionKind.DeleteSlide, "Delete Slide", 1, 1, true));
    }

    [Fact]
    public void BuildContextActions_DisablesInvalidAndSingleSlideDeletes()
    {
        var singleSlideActions = SlidePanePlanner.BuildContextActions(slideCount: 1, slideIndex: 0);
        singleSlideActions.Single(a => a.Kind == SlidePaneActionKind.DeleteSlide).IsEnabled.Should().BeFalse();

        var invalidActions = SlidePanePlanner.BuildContextActions(slideCount: 2, slideIndex: 5);
        invalidActions.Should().OnlyContain(a => !a.IsEnabled);
    }

    [Fact]
    public void BuildHiddenSlideAction_ReflectsHideAndShowState()
    {
        var slides = new[] { new Slide(), new Slide { IsHidden = true } };

        var hide = SlidePanePlanner.BuildHiddenSlideAction(slides, 0);
        hide.Kind.Should().Be(SlidePaneActionKind.ToggleHiddenSlide);
        hide.Text.Should().Be(SlidePanePlanner.HideSlideMenuText);
        hide.IsEnabled.Should().BeTrue();
        hide.IsChecked.Should().BeFalse();

        var show = SlidePanePlanner.BuildHiddenSlideAction(slides, 1);
        show.Text.Should().Be(SlidePanePlanner.ShowSlideMenuText);
        show.IsEnabled.Should().BeTrue();
        show.IsChecked.Should().BeTrue();
    }

    [Theory]
    [InlineData(FreePContextMenuCommand.NewSlide, SlidePaneActionKind.InsertAfterSlide)]
    [InlineData(FreePContextMenuCommand.DuplicateSlide, SlidePaneActionKind.DuplicateSlide)]
    [InlineData(FreePContextMenuCommand.DeleteSlide, SlidePaneActionKind.DeleteSlide)]
    [InlineData(FreePContextMenuCommand.ToggleHiddenSlide, SlidePaneActionKind.ToggleHiddenSlide)]
    public void BuildContextCommandRoute_MapsSlideCommands(
        FreePContextMenuCommand command,
        SlidePaneActionKind expectedKind)
    {
        var slides = new[] { new Slide(), new Slide() };

        var route = SlidePanePlanner.BuildContextCommandRoute(
            command,
            slides,
            Array.Empty<PresentationSection>(),
            slideIndex: 0,
            sectionIndex: -1);

        route.SlideAction!.Kind.Should().Be(expectedKind);
        route.SectionExecution.Should().BeNull();
        route.IsEnabled.Should().BeTrue();
    }

    [Theory]
    [InlineData(FreePContextMenuCommand.AddSection, SlideSectionActionKind.AddSection, true)]
    [InlineData(FreePContextMenuCommand.RenameSection, SlideSectionActionKind.RenameSection, true)]
    [InlineData(FreePContextMenuCommand.RemoveSection, SlideSectionActionKind.RemoveSection, false)]
    [InlineData(FreePContextMenuCommand.RemoveAllSections, SlideSectionActionKind.RemoveAllSections, false)]
    public void BuildContextCommandRoute_MapsSectionCommandsAndPromptState(
        FreePContextMenuCommand command,
        SlideSectionActionKind expectedKind,
        bool requiresPrompt)
    {
        var slides = new[] { new Slide { Id = "slide-1" } };
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add("slide-1");

        var route = SlidePanePlanner.BuildContextCommandRoute(
            command,
            slides,
            new[] { section },
            slideIndex: 0,
            sectionIndex: 0);

        route.SlideAction.Should().BeNull();
        route.SectionExecution!.Kind.Should().Be(expectedKind);
        route.SectionExecution.RequiresNamePrompt.Should().Be(requiresPrompt);
        route.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void TryApplyAction_ToggleHidden_UsesSharedSelectionAndCommandRouting()
    {
        var editor = CreateEditingSession(2);
        var action = SlidePanePlanner.BuildHiddenSlideAction(editor.Presentation.Slides, 1);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();
        editor.CurrentSlideIndex.Should().Be(1);
        editor.CurrentSlide!.IsHidden.Should().BeTrue();
    }

    [Fact]
    public void BuildBottomNewSlideAffordance_ProjectsVisibleSharedInsertAction()
    {
        var plan = SlidePanePlanner.BuildBottomNewSlideAffordance(
            slideCount: 3,
            currentSlideIndex: 1);

        plan.Text.Should().Be(SlidePanePlanner.NewSlideButtonText);
        plan.ToolTipText.Should().Be("Insert a new slide after the current slide");
        plan.AccessibleName.Should().Be("New Slide");
        plan.IsVisible.Should().BeTrue();
        plan.Action.Should().Be(new SlidePaneActionPlan(
            SlidePaneActionKind.InsertAfterSlide,
            SlidePanePlanner.NewSlideButtonText,
            1,
            2,
            true));
    }

    [Fact]
    public void TryApplyBottomNewSlideAffordance_UsesSharedActionRouting()
    {
        var editor = CreateEditingSession(2);
        editor.SelectSlide(0);

        SlidePanePlanner.TryApplyBottomNewSlideAffordance(editor).Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(3);
        editor.CurrentSlideIndex.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, 1, false)]
    [InlineData(0, 2, true)]
    [InlineData(2, 0, true)]
    [InlineData(2, 3, false)]
    [InlineData(2, 4, false)]
    public void PlanMoveAction_EnablesOnlyRealInsertionMoves(
        int sourceSlideIndex,
        int targetInsertionIndex,
        bool expectedEnabled)
    {
        var action = SlidePanePlanner.PlanMoveAction(3, sourceSlideIndex, targetInsertionIndex);

        action.Kind.Should().Be(SlidePaneActionKind.MoveSlide);
        action.SourceSlideIndex.Should().Be(sourceSlideIndex);
        action.TargetSlideIndex.Should().Be(targetInsertionIndex);
        action.IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public void BuildKeyboardAction_MapsSlidePaneKeysToSharedActions()
    {
        SlidePanePlanner.BuildKeyboardAction(
                slideCount: 3,
                currentSlideIndex: 1,
                SlidePaneKeyboardIntentKind.InsertAfterCurrentSlide)
            .Should().Be(new SlidePaneActionPlan(
                SlidePaneActionKind.InsertAfterSlide,
                SlidePanePlanner.NewSlideMenuText,
                1,
                2,
                true));

        SlidePanePlanner.BuildKeyboardAction(
                slideCount: 3,
                currentSlideIndex: 1,
                SlidePaneKeyboardIntentKind.DuplicateCurrentSlide)
            .Should().Be(new SlidePaneActionPlan(
                SlidePaneActionKind.DuplicateSlide,
                SlidePanePlanner.DuplicateSlideMenuText,
                1,
                2,
                true));

        SlidePanePlanner.BuildKeyboardAction(
                slideCount: 3,
                currentSlideIndex: 1,
                SlidePaneKeyboardIntentKind.DeleteCurrentSlide)
            .Should().Be(new SlidePaneActionPlan(
                SlidePaneActionKind.DeleteSlide,
                SlidePanePlanner.DeleteSlideMenuText,
                1,
                1,
                true));
    }

    [Theory]
    [InlineData(0, SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier, -1, false)]
    [InlineData(1, SlidePaneKeyboardIntentKind.MoveCurrentSlideEarlier, 0, true)]
    [InlineData(1, SlidePaneKeyboardIntentKind.MoveCurrentSlideLater, 3, true)]
    [InlineData(2, SlidePaneKeyboardIntentKind.MoveCurrentSlideLater, 4, false)]
    public void BuildKeyboardAction_MapsMoveIntentsToInsertionTargets(
        int currentSlideIndex,
        SlidePaneKeyboardIntentKind intent,
        int expectedTargetIndex,
        bool expectedEnabled)
    {
        var action = SlidePanePlanner.BuildKeyboardAction(3, currentSlideIndex, intent);

        action.Kind.Should().Be(SlidePaneActionKind.MoveSlide);
        action.SourceSlideIndex.Should().Be(currentSlideIndex);
        action.TargetSlideIndex.Should().Be(expectedTargetIndex);
        action.IsEnabled.Should().Be(expectedEnabled);
    }

    [Fact]
    public void TryApplyAction_Duplicate_UsesSharedSelectionAndCommandRouting()
    {
        var editor = CreateEditingSession(2);
        var action = SlidePanePlanner.BuildContextActions(2, 0)
            .Single(a => a.Kind == SlidePaneActionKind.DuplicateSlide);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(3);
        editor.CurrentSlideIndex.Should().Be(1);
    }

    [Fact]
    public void TryApplyAction_Move_UsesSharedSelectionAndCommandRouting()
    {
        var editor = CreateEditingSession(3);
        var action = SlidePanePlanner.PlanMoveAction(3, sourceSlideIndex: 0, targetInsertionIndex: 3);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("Slide 2", "Slide 3", "Slide 1");
        editor.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void TryApplyAction_DisabledAction_DoesNotMutateSlides()
    {
        var editor = CreateEditingSession(1);
        var action = SlidePanePlanner.BuildContextActions(1, 0)
            .Single(a => a.Kind == SlidePaneActionKind.DeleteSlide);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeFalse();

        editor.Presentation.Slides.Should().HaveCount(1);
        editor.CurrentSlideIndex.Should().Be(0);
    }

    [Fact]
    public void TryApplyAction_KeyboardMoveLater_UsesSharedInsertionTarget()
    {
        var editor = CreateEditingSession(3);
        editor.SelectSlide(1);
        var action = SlidePanePlanner.BuildKeyboardAction(
            editor.Presentation.Slides.Count,
            editor.CurrentSlideIndex,
            SlidePaneKeyboardIntentKind.MoveCurrentSlideLater);

        SlidePanePlanner.TryApplyAction(editor, action).Should().BeTrue();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("Slide 1", "Slide 3", "Slide 2");
        editor.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void SectionPlanner_NormalizesBlankNamesToUntitledSection()
    {
        SlideSectionPlanner.NormalizeSectionName("  \t ").Should().Be(SlideSectionPlanner.DefaultSectionName);
        SlideSectionPlanner.NormalizeSectionName("  Q1\r\nPlan  ").Should().Be("Q1 Plan");
    }

    [Fact]
    public void SectionPlanner_BuildsAddAndHeaderActions()
    {
        var editor = CreateEditingSession(2);
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add("slide1");
        editor.Presentation.Sections.Add(section);

        SlideSectionPlanner.BuildSlideContextActions(
                editor.Presentation.Slides,
                editor.Presentation.Sections,
                slideIndex: 1)
            .Should().Equal(new SlideSectionActionPlan(
                SlideSectionActionKind.AddSection,
                SlideSectionPlanner.AddSectionMenuText,
                1,
                -1,
                true,
                SlideSectionPlanner.DefaultSectionName));

        SlideSectionPlanner.BuildSectionHeaderActions(editor.Presentation.Sections, 0, 0)
            .Select(action => action.Kind)
            .Should().Equal(
                SlideSectionActionKind.RenameSection,
                SlideSectionActionKind.RemoveSection,
                SlideSectionActionKind.RemoveAllSections);
    }

    [Fact]
    public void SectionPlanner_BuildsSharedExecutionPlanForPromptedActions()
    {
        var editor = CreateEditingSession(2);
        var addAction = SlideSectionPlanner.BuildSlideContextActions(
                editor.Presentation.Slides,
                editor.Presentation.Sections,
                slideIndex: 1)
            .Single();

        var execution = SlideSectionPlanner.BuildExecutionPlan(addAction);

        execution.Kind.Should().Be(SlideSectionActionKind.AddSection);
        execution.IsEnabled.Should().BeTrue();
        execution.RequiresNamePrompt.Should().BeTrue();
        execution.PromptTitle.Should().Be(SlideSectionPlanner.AddSectionMenuText);
        execution.PromptLabel.Should().Be("Section name:");
        execution.PromptAcceptText.Should().Be("OK");
        execution.PromptCancelText.Should().Be("Cancel");
        execution.SuggestedName.Should().Be(SlideSectionPlanner.DefaultSectionName);
        execution.SlideIndex.Should().Be(1);
    }

    [Fact]
    public void SectionPlanner_TryApplyAction_UsesSharedExecutionForAddRenameAndRemove()
    {
        var editor = CreateEditingSession(3);

        var addExecution = SlideSectionPlanner.BuildExecutionPlan(
            SlideSectionPlanner.BuildSlideContextActions(
                    editor.Presentation.Slides,
                    editor.Presentation.Sections,
                    slideIndex: 1)
                .Single());

        SlideSectionPlanner.TryApplyAction(editor, addExecution)
            .Should().BeFalse("prompted actions require the host-provided name");
        SlideSectionPlanner.TryApplyAction(editor, addExecution, "  Part Two  ").Should().BeTrue();
        editor.Presentation.Sections.Should().ContainSingle();
        editor.Presentation.Sections[0].Name.Should().Be("Part Two");

        var renameExecution = SlideSectionPlanner.BuildExecutionPlan(
            SlideSectionPlanner.BuildSectionHeaderActions(editor.Presentation.Sections, 0, 1)
                .Single(action => action.Kind == SlideSectionActionKind.RenameSection));

        renameExecution.RequiresNamePrompt.Should().BeTrue();
        renameExecution.PromptTitle.Should().Be(SlideSectionPlanner.RenameSectionMenuText);
        SlideSectionPlanner.TryApplyAction(editor, renameExecution, "  Renamed  ").Should().BeTrue();
        editor.Presentation.Sections[0].Name.Should().Be("Renamed");

        var removeExecution = SlideSectionPlanner.BuildExecutionPlan(
            SlideSectionPlanner.BuildSectionHeaderActions(editor.Presentation.Sections, 0, 1)
                .Single(action => action.Kind == SlideSectionActionKind.RemoveSection));

        removeExecution.RequiresNamePrompt.Should().BeFalse();
        SlideSectionPlanner.TryApplyAction(editor, removeExecution).Should().BeTrue();
        editor.Presentation.Sections.Should().BeEmpty();
    }

    [Fact]
    public void AddSectionAtSlide_SplitsExistingSectionAndUsesSlideIds()
    {
        var editor = CreateEditingSession(3);
        var section = new PresentationSection { Name = "Deck" };
        section.SlideIds.AddRange(new[] { "slide1", "slide2", "slide3" });
        editor.Presentation.Sections.Add(section);

        editor.AddSectionAtSlide(1, "  Part Two  ").Should().BeTrue();

        editor.Presentation.Sections.Select(s => s.Name).Should().Equal("Deck", "Part Two");
        editor.Presentation.Sections[0].SlideIds.Should().Equal("slide1");
        editor.Presentation.Sections[1].SlideIds.Should().Equal("slide2", "slide3");
    }

    [Fact]
    public void RenameSection_BlankName_UsesUntitledSectionAndIsUndoable()
    {
        var editor = CreateEditingSession(2);
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.Add("slide1");
        editor.Presentation.Sections.Add(section);

        editor.RenameSection(0, "   ").Should().BeTrue();

        editor.Presentation.Sections[0].Name.Should().Be(SlideSectionPlanner.DefaultSectionName);

        editor.Undo();
        editor.Presentation.Sections[0].Name.Should().Be("Intro");
    }

    [Fact]
    public void RemoveSection_KeepsSlidesAndMergesIntoPreviousSection()
    {
        var editor = CreateEditingSession(4);
        var intro = new PresentationSection { Name = "Intro" };
        intro.SlideIds.AddRange(new[] { "slide1", "slide2" });
        var body = new PresentationSection { Name = "Body" };
        body.SlideIds.AddRange(new[] { "slide3", "slide4" });
        editor.Presentation.Sections.Add(intro);
        editor.Presentation.Sections.Add(body);

        editor.RemoveSection(1).Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(4);
        editor.Presentation.Sections.Should().ContainSingle();
        editor.Presentation.Sections[0].Name.Should().Be("Intro");
        editor.Presentation.Sections[0].SlideIds.Should().Equal("slide1", "slide2", "slide3", "slide4");
    }

    [Fact]
    public void RemoveFirstSection_KeepsSlidesAndLeavesThemUnsectioned()
    {
        var editor = CreateEditingSession(3);
        var intro = new PresentationSection { Name = "Intro" };
        intro.SlideIds.Add("slide1");
        var body = new PresentationSection { Name = "Body" };
        body.SlideIds.AddRange(new[] { "slide2", "slide3" });
        editor.Presentation.Sections.Add(intro);
        editor.Presentation.Sections.Add(body);

        editor.RemoveSection(0).Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(3);
        editor.Presentation.Sections.Should().ContainSingle();
        editor.Presentation.Sections[0].Name.Should().Be("Body");
        editor.Presentation.Sections[0].SlideIds.Should().Equal("slide2", "slide3");
    }

    [Fact]
    public void RenameAndRemoveSection_UseHeaderOriginalIndexAfterPruningStaleSections()
    {
        var editor = CreateEditingSession(2);
        var stale = new PresentationSection { Name = "Stale" };
        stale.SlideIds.Add("missing-slide");
        var live = new PresentationSection { Name = "Live" };
        live.SlideIds.AddRange(new[] { "slide1", "slide2" });
        editor.Presentation.Sections.Add(stale);
        editor.Presentation.Sections.Add(live);

        var header = SlidePanePlanner.BuildEntries(
                editor.Presentation.Slides,
                editor.Presentation.Sections)
            .Single(entry => entry.Kind == SlidePaneEntryKind.SectionHeader);
        header.SectionIndex.Should().Be(1);

        editor.RenameSection(header.SectionIndex, "Renamed").Should().BeTrue();
        editor.Presentation.Sections.Should().ContainSingle();
        editor.Presentation.Sections[0].Name.Should().Be("Renamed");

        var removeEditor = CreateEditingSession(2);
        var removeStale = new PresentationSection { Name = "Stale" };
        removeStale.SlideIds.Add("missing-slide");
        var removeLive = new PresentationSection { Name = "Live" };
        removeLive.SlideIds.AddRange(new[] { "slide1", "slide2" });
        removeEditor.Presentation.Sections.Add(removeStale);
        removeEditor.Presentation.Sections.Add(removeLive);

        removeEditor.RemoveSection(header.SectionIndex).Should().BeTrue();
        removeEditor.Presentation.Sections.Should().BeEmpty();
        removeEditor.Presentation.Slides.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveAllSections_ClearsOnlySectionMetadata()
    {
        var editor = CreateEditingSession(2);
        var section = new PresentationSection { Name = "Intro" };
        section.SlideIds.AddRange(new[] { "slide1", "slide2" });
        editor.Presentation.Sections.Add(section);

        editor.RemoveAllSections().Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(2);
        editor.Presentation.Sections.Should().BeEmpty();
    }

    private static EditingSession CreateEditingSession(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        for (var i = 0; i < slideCount; i++)
        {
            presentation.Slides.Add(new Slide
            {
                Id = $"slide{i + 1}",
                Title = $"Slide {i + 1}",
            });
        }

        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
