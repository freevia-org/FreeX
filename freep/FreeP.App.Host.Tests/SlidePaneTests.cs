using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Tests for <see cref="SlidePane"/> (Wave 3B).
/// All tests run on an STA thread because SlidePane is a WPF control.
/// </summary>
public sealed class SlidePaneTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static int CountThumbnailItems(SlidePane pane) => pane.SlidePaneSlideItemCount;

    private static ListBoxItem GetItem(SlidePane pane, int index) =>
        pane.NativeListForTests.Items[index].Should().BeOfType<ListBoxItem>().Subject;

    private static Border GetChrome(SlidePane pane, int index) =>
        GetItem(pane, index).Content.Should().BeOfType<Border>().Subject;

    private static Button GetNewSlideButton(SlidePane pane) => pane.NewSlideButtonForTests;

    private static Color BrushColor(Brush brush) =>
        brush.Should().BeOfType<SolidColorBrush>().Subject.Color;

    private static Color ColorFromHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex)!;

    private static (SlidePane pane, EditingSession editor) MakePaneWithSlides(int count)
    {
        var presentation = Presentation.CreateEmpty();
        // CreateEmpty() adds 1 slide; add (count-1) more if needed.
        for (int i = 1; i < count; i++)
        {
            var s = new Slide { Title = $"Slide {i + 1}" };
            presentation.Slides.Add(s);
        }
        return MakePane(presentation);
    }

    private static (SlidePane pane, EditingSession editor) MakePane(Presentation presentation)
    {
        var endpoint = new PaneEndpoint();
        var workarea = new PresentationWorkareaSession(endpoint, presentation);
        var pane = new SlidePane(workarea);
        endpoint.Pane = pane;
        workarea.Initialize();
        return (pane, workarea.Editor);
    }

    // ── Construction ──────────────────────────────────────────────────────────────

    [StaFact]
    public void SlidePane_Constructs_WithOneSlide()
    {
        var (pane, _) = MakePaneWithSlides(1);
        pane.Should().NotBeNull();
        CountThumbnailItems(pane).Should().Be(1);
    }

    [StaFact]
    public void SlidePane_Constructs_WithThreeSlides_ShowsThreeItems()
    {
        var (pane, _) = MakePaneWithSlides(3);
        CountThumbnailItems(pane).Should().Be(3);
    }

    [StaFact]
    public void SlidePane_HasNewSlideButtonAtBottom()
    {
        var (pane, _) = MakePaneWithSlides(2);
        var button = GetNewSlideButton(pane);

        button.Content.Should().Be(SlidePanePlanner.NewSlideButtonText);
        button.ToolTip.Should().Be("Insert a new slide after the current slide");
        button.IsEnabled.Should().BeTrue();
        AutomationProperties.GetName(button).Should().Be("New Slide");
    }

    [StaFact]
    public void SlidePane_NewSlideButton_UsesSharedBottomAffordanceAction()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.SelectSlide(0);
        var button = GetNewSlideButton(pane);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        editor.Presentation.Slides.Should().HaveCount(3);
        editor.CurrentSlideIndex.Should().Be(1);
        CountThumbnailItems(pane).Should().Be(3);
    }

    // ── Selection ─────────────────────────────────────────────────────────────────

    [StaFact]
    public void ClickingItem_SelectsSlide()
    {
        var (pane, editor) = MakePaneWithSlides(3);
        // Editor starts on slide 0. Simulate clicking slide 2 by raising the event directly.
        editor.SelectSlide(2);
        editor.CurrentSlideIndex.Should().Be(2);
    }

    [StaFact]
    public void SlidePane_Item_Tag_ReflectsSlideIndex()
    {
        var (pane, _) = MakePaneWithSlides(3);
        for (int i = 0; i < 3; i++)
        {
            var item = GetItem(pane, i);
            item.Tag.Should().Be(i, $"item {i} should carry tag {i}");
        }
    }

    [StaFact]
    public void SlidePane_HighlightedItem_IsCurrentSlideIndex()
    {
        var (pane, editor) = MakePaneWithSlides(3);
        editor.SelectSlide(1);

        // After select, item at index 1 should have the accent border thickness (2).
        var selected = GetChrome(pane, 1);
        selected.BorderThickness.Left.Should().Be(2,
            "selected item must have the accent 2-px border");

        var other = GetChrome(pane, 0);
        other.BorderThickness.Left.Should().Be(1,
            "non-selected item must have the normal 1-px border");
    }

    [StaFact]
    public void NativeExtendedSelectionFeedsSharedBatchCommands()
    {
        var (pane, editor) = MakePaneWithSlides(3);
        var list = pane.NativeListForTests;

        list.SelectedItems.Add(GetItem(pane, 0));
        list.SelectedItems.Add(GetItem(pane, 2));

        editor.CurrentSlideIndex.Should().Be(2);
        GetChrome(pane, 0).BorderThickness.Left
            .Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);
        GetChrome(pane, 2).BorderThickness.Left
            .Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);

        pane.TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind.DuplicateCurrentSlide)
            .Should().BeTrue();

        editor.Presentation.Slides.Should().HaveCount(5);
        pane.SlidePaneSlideItemCount.Should().Be(5);
    }

    [StaFact]
    public void SlidePane_ThumbnailItems_ExposeLiveSharedAutomationNames()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.SetSlideTitle(0, "Opening");
        editor.SetSlideTitle(1, "Agenda");

        pane.SlidePaneThumbnailAutomationNamesForTests.Should().Equal(
            "Slide 1: Opening, 1 object",
            "Slide 2: Agenda, 1 object");

        editor.SelectSlide(1);
        pane.SlidePaneThumbnailAutomationNamesForTests.Should().Equal(
            "Slide 1: Opening, 1 object",
            "Slide 2: Agenda, 1 object");

        editor.MoveSlide(1, 0);
        pane.SlidePaneThumbnailAutomationNamesForTests.Should().Equal(
            "Slide 1: Agenda, 1 object",
            "Slide 2: Opening, 1 object");
    }

    [StaFact]
    public void SlidePane_SectionHeaders_ExposeLiveSharedAutomationNames()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide1", Title = "Opening" });
        presentation.Slides.Add(new Slide { Id = "slide2", Title = "Agenda" });
        var section = new PresentationSection { Id = "intro", Name = "Intro" };
        section.SlideIds.Add("slide1");
        section.SlideIds.Add("slide2");
        presentation.Sections.Add(section);

        var (pane, _) = MakePane(presentation);

        pane.SlidePaneSectionHeaderAutomationNamesForTests.Should()
            .ContainSingle().Which.Should().Be("Section Intro  (2), expanded");
        pane.ToggleSectionForTests(0).Should().BeTrue();
        pane.SlidePaneSectionHeaderAutomationNamesForTests.Should()
            .ContainSingle().Which.Should().Be("Section Intro  (2), collapsed");
    }

    // ── Structural changes ────────────────────────────────────────────────────────

    [StaFact]
    public void SlidePane_ThumbnailChrome_UsesSharedVisualPlanTokens()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.SelectSlide(1);

        var selected = GetChrome(pane, 1);
        BrushColor(selected.Background).Should().Be(ColorFromHex(SlidePanePlanner.DefaultItemSelectedBackgroundHex));
        BrushColor(selected.BorderBrush).Should().Be(ColorFromHex(SlidePanePlanner.DefaultItemSelectedBorderHex));
        selected.BorderThickness.Left.Should().Be(SlidePanePlanner.DefaultSelectedBorderThickness);
        selected.CornerRadius.TopLeft.Should().Be(SlidePanePlanner.DefaultItemCornerRadius);

        var normal = GetChrome(pane, 0);
        BrushColor(normal.Background).Should().Be(ColorFromHex(SlidePanePlanner.DefaultItemNormalBackgroundHex));
        BrushColor(normal.BorderBrush).Should().Be(ColorFromHex(SlidePanePlanner.DefaultItemNormalBorderHex));
        normal.BorderThickness.Left.Should().Be(SlidePanePlanner.DefaultNormalBorderThickness);

        var panel = selected.Child.Should().BeOfType<StackPanel>().Subject;
        panel.HorizontalAlignment.Should().Be(
            SlidePanePlanner.DefaultCenterThumbnailContent
                ? HorizontalAlignment.Center
                : HorizontalAlignment.Stretch);
        var label = panel.Children[0].Should().BeOfType<TextBlock>().Subject;
        BrushColor(label.Foreground).Should().Be(ColorFromHex(SlidePanePlanner.DefaultLabelForegroundHex));
        label.FontSize.Should().Be(SlidePanePlanner.DefaultLabelFontSize);
        label.Margin.Bottom.Should().Be(SlidePanePlanner.DefaultLabelBottomMargin);

        var thumbnailBorder = panel.Children[1].Should().BeOfType<Border>().Subject;
        thumbnailBorder.BorderThickness.Left.Should().Be(SlidePanePlanner.DefaultThumbnailBorderThickness);
    }

    [StaFact]
    public void AfterInsertSlide_PaneShowsNPlusOneItems()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.InsertSlide();
        CountThumbnailItems(pane).Should().Be(3);
    }

    [StaFact]
    public void AfterDuplicateCurrentSlide_PaneShowsNPlusOneItems()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.DuplicateCurrentSlide();
        CountThumbnailItems(pane).Should().Be(3);
    }

    [StaFact]
    public void AfterDeleteCurrentSlide_PaneShowsNMinusOneItems()
    {
        var (pane, editor) = MakePaneWithSlides(3);
        editor.DeleteCurrentSlide();
        CountThumbnailItems(pane).Should().Be(2);
    }

    // ── Reorder ───────────────────────────────────────────────────────────────────

    [StaFact]
    public void MoveSlide_Reorders_AndPaneReflectsNewOrder()
    {
        // Start with 3 slides: 0, 1, 2.
        var (pane, editor) = MakePaneWithSlides(3);

        // Give each slide a distinct title so we can verify reorder.
        editor.Presentation.Slides[0].Title = "Alpha";
        editor.Presentation.Slides[1].Title = "Beta";
        editor.Presentation.Slides[2].Title = "Gamma";

        // Move slide 0 (Alpha) to position 2.
        // MoveSlide(from, to): removes item at `from`, then inserts at `to` of the shortened list.
        // Remove Alpha → [Beta(0), Gamma(1)], then Insert at 2 → [Beta(0), Gamma(1), Alpha(2)].
        editor.MoveSlide(0, 2);

        // After move, pane still has 3 items.
        CountThumbnailItems(pane).Should().Be(3);

        // Tags should be re-assigned 0..2 in the new order.
        for (int i = 0; i < 3; i++)
            GetItem(pane, i).Tag.Should().Be(i);

        // Actual model order after MoveSlide(0,2): Beta(0), Gamma(1), Alpha(2).
        editor.Presentation.Slides[0].Title.Should().Be("Beta");
        editor.Presentation.Slides[2].Title.Should().Be("Alpha");
    }

    [StaFact]
    public void SlidePane_KeyboardDelete_UsesSharedPlannerEnablement()
    {
        var (pane, editor) = MakePaneWithSlides(1);

        pane.TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind.DeleteCurrentSlide)
            .Should().BeFalse("the shared planner blocks deleting the final slide");
        editor.Presentation.Slides.Should().ContainSingle();

        editor.InsertSlide();
        editor.SelectSlide(1);
        pane.TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind.DeleteCurrentSlide)
            .Should().BeTrue("keyboard delete should route through the same slide-pane action plan as the menu");

        editor.Presentation.Slides.Should().ContainSingle();
        editor.CurrentSlideIndex.Should().Be(0);
    }

    [StaFact]
    public void SlidePane_KeyboardMoveLater_UsesSharedInsertionPlan()
    {
        var (pane, editor) = MakePaneWithSlides(3);
        editor.Presentation.Slides[0].Title = "Alpha";
        editor.Presentation.Slides[1].Title = "Beta";
        editor.Presentation.Slides[2].Title = "Gamma";
        editor.SelectSlide(1);

        pane.TryApplySlidePaneKeyboardAction(SlidePaneKeyboardIntentKind.MoveCurrentSlideLater)
            .Should().BeTrue();

        editor.Presentation.Slides.Select(slide => slide.Title)
            .Should().Equal("Alpha", "Gamma", "Beta");
        editor.CurrentSlideIndex.Should().Be(2);
        CountThumbnailItems(pane).Should().Be(3);
    }

    // ── Undo ─────────────────────────────────────────────────────────────────────

    [StaFact]
    public void SectionHeader_ToggleCollapsesMemberSlidesAndKeepsContextMenu()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide1", Title = "Slide 1" });
        presentation.Slides.Add(new Slide { Id = "slide2", Title = "Slide 2" });
        presentation.Slides.Add(new Slide { Id = "slide3", Title = "Slide 3" });
        var intro = new PresentationSection { Id = "intro-section", Name = "Intro" };
        intro.SlideIds.Add("slide1");
        var body = new PresentationSection { Id = "body-section", Name = "Body" };
        body.SlideIds.Add("slide2");
        body.SlideIds.Add("slide3");
        presentation.Sections.Add(intro);
        presentation.Sections.Add(body);

        var (pane, editor) = MakePane(presentation);

        pane.SlidePaneSectionHeaderCount.Should().Be(2);
        pane.SlidePaneSlideItemCount.Should().Be(3);
        GetItem(pane, 2).ContextMenu.Should().NotBeNull("section header context actions must stay available");

        pane.ToggleSectionForTests(1).Should().BeTrue();

        pane.SlidePaneSectionHeaderCount.Should().Be(2);
        pane.SlidePaneSlideItemCount.Should().Be(1);

        pane.ToggleSectionForTests(1).Should().BeTrue();
        pane.SlidePaneSlideItemCount.Should().Be(3);
    }

    [StaFact]
    public void SectionContextActions_RouteThroughSharedExecutionPlanner()
    {
        var (pane, editor) = MakePaneWithSlides(3);

        pane.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.AddSection,
                slideIndex: 1,
                promptedName: "  Agenda  ")
            .Should().BeTrue();

        editor.Presentation.Sections.Should().ContainSingle();
        editor.Presentation.Sections[0].Name.Should().Be("Agenda");
        pane.SlidePaneSectionHeaderCount.Should().Be(1);

        pane.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.RenameSection,
                slideIndex: 1,
                sectionIndex: 0,
                promptedName: "  Renamed Agenda  ")
            .Should().BeTrue();

        editor.Presentation.Sections[0].Name.Should().Be("Renamed Agenda");

        pane.TryApplySlideSectionActionForTests(
                SlideSectionActionKind.RemoveSection,
                slideIndex: 1,
                sectionIndex: 0)
            .Should().BeTrue();

        editor.Presentation.Sections.Should().BeEmpty();
        editor.Presentation.Slides.Should().HaveCount(3);
        pane.SlidePaneSectionHeaderCount.Should().Be(0);
    }

    [StaFact]
    public void SectionHeader_ChromeUsesSharedVisualPlanTokens()
    {
        var presentation = new Presentation();
        presentation.Slides.Add(new Slide { Id = "slide1", Title = "Slide 1" });
        presentation.Slides.Add(new Slide { Id = "slide2", Title = "Slide 2" });
        var section = new PresentationSection { Id = "intro-section", Name = "Intro" };
        section.SlideIds.AddRange(new[] { "slide1", "slide2" });
        presentation.Sections.Add(section);

        var (pane, _) = MakePane(presentation);
        var headerItem = GetItem(pane, 0);
        var header = GetChrome(pane, 0);
        var plan = SlidePanePlanner.BuildSectionHeaderVisualPlan(
            SlidePanePlanner.BuildEntries(presentation.Slides, presentation.Sections)
                .Single(entry => entry.Kind == SlidePaneEntryKind.SectionHeader));

        BrushColor(header.Background).Should().Be(ColorFromHex(plan.BackgroundHex));
        header.MinHeight.Should().Be(plan.HeaderHeight);
        header.Padding.Left.Should().Be(plan.HorizontalPadding);
        header.Padding.Top.Should().Be(plan.VerticalPadding);
        headerItem.Margin.Top.Should().Be(plan.TopMargin);
        headerItem.Margin.Bottom.Should().Be(plan.BottomMargin);
        header.CornerRadius.TopLeft.Should().Be(plan.CornerRadius);
        headerItem.ToolTip.Should().Be(plan.ToolTipText);
        AutomationProperties.GetName(headerItem).Should().Be(plan.AccessibleName);

        var row = header.Child.Should().BeOfType<DockPanel>().Subject;
        var disclosure = row.Children[0].Should().BeOfType<TextBlock>().Subject;
        disclosure.Text.Should().Be(plan.DisclosureText);
        disclosure.Width.Should().Be(plan.DisclosureWidth);
        BrushColor(disclosure.Foreground).Should().Be(ColorFromHex(plan.ForegroundHex));

        var label = row.Children[1].Should().BeOfType<TextBlock>().Subject;
        label.Text.Should().Be(plan.LabelText);
        BrushColor(label.Foreground).Should().Be(ColorFromHex(plan.ForegroundHex));
    }

    [StaFact]
    public void Undo_AfterInsert_PaneRestoresOriginalCount()
    {
        var (pane, editor) = MakePaneWithSlides(2);
        editor.InsertSlide();
        editor.Undo();
        CountThumbnailItems(pane).Should().Be(2);
    }

    // ── MainWindow seam ───────────────────────────────────────────────────────────

    [StaFact]
    public void MainWindow_SlidePaneHost_HasSlidePaneChild()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.SlidePaneHost.Child.Should().BeOfType<SlidePane>(
                "MainWindow must set SlidePaneHost.Child to a SlidePane at construction");
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class PaneEndpoint : IPresentationWorkareaEndpoint
    {
        public SlidePane? Pane { get; set; }

        public bool IsPaneVisible(PresentationWorkareaPane pane) => false;

        public void Apply(
            PresentationWorkareaOperation operation,
            PresentationWorkareaContext context)
        {
            switch (operation)
            {
                case PresentationWorkareaOperation.RefreshSlidePane:
                    Pane?.RefreshProjection();
                    break;
                case PresentationWorkareaOperation.SyncSlidePaneSelection:
                    Pane?.SyncNativeSelection();
                    break;
                case PresentationWorkareaOperation.RefreshSlidePaneChrome:
                    Pane?.RefreshItemChrome();
                    break;
            }
        }

        public void ExecuteNativeCommand(PresentationWorkareaNativeCommand command)
        {
        }
    }
}
