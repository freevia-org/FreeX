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
    public void DefaultThumbnailMetrics_UseSixteenByNineSlideAspect()
    {
        SlidePanePlanner.DefaultThumbnailWidth.Should().Be(150.0);
        SlidePanePlanner.DefaultThumbnailHeight.Should().BeApproximately(84.375, 0.0001);
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
