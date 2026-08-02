using System.IO;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void MainWindow_DelegatesSlidePaneProjectionToPresentationPlanner()
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"),
            "freep",
            "FreeP.App.Avalonia",
            "MainWindow.cs"));
        var slidePaneStart = source.IndexOf("private void RefreshSlidePane", StringComparison.Ordinal);
        var slidePaneEnd = source.IndexOf("private void RefreshNotesPane", slidePaneStart, StringComparison.Ordinal);
        slidePaneStart.Should().BeGreaterThanOrEqualTo(0);
        slidePaneEnd.Should().BeGreaterThan(slidePaneStart);
        var slidePaneSource = source[slidePaneStart..slidePaneEnd];

        source.Should().Contain("SlidePanePlanner.BuildSessionProjection(");
        source.Should().Contain("SlidePanePlanner.SetSelectedSlide(");
        source.Should().Contain("SlidePanePlanner.ToggleSection(");
        source.Should().Contain("SlidePanePlanner.BuildThumbnailVisualPlan(");
        source.Should().Contain("SlidePanePlanner.BuildSectionHeaderVisualPlan(entry)");
        source.Should().Contain("_slidePaneSessionState");
        source.Should().Contain("_slidePaneProjection");
        source.Should().Contain("_slidePaneRenderedSectionHeaderPlans.Add(plan)");
        source.Should().Contain("SlidePaneEntryKind.SectionHeader");
        source.Should().Contain("BuildSlidePaneSectionHeader(entry,");
        source.Should().Contain("Text              = plan.LabelText");
        source.Should().Contain("Text              = plan.DisclosureText");
        source.Should().Contain("Foreground        = BrushFromHex(plan.ForegroundHex)");
        source.Should().Contain("Background   = normalBackground");
        source.Should().Contain("AutomationProperties.SetName(item, plan.AccessibleName)");
        source.Should().Contain("PointerEntered += (_, _) => headerChrome.Background = hoverBackground");
        source.Should().Contain("ToolTip.SetTip(item, plan.ToolTipText)");
        source.Should().Contain("ToggleSlidePaneSection(plan.SectionId)");
        source.Should().Contain("ContextMenu = BuildSlidePaneSectionContextMenu(entry)");
        source.Should().Contain("Background  = BrushFromHex(SlidePanePlanner.DefaultPaneBackgroundHex)");
        source.Should().Contain("_slidePaneRenderedThumbnailPlans.Add(plan)");
        source.Should().Contain("Width        = plan.ThumbnailWidth");
        source.Should().Contain("Height       = plan.ThumbnailHeight");
        source.Should().Contain("IsHitTestVisible = false");
        source.Should().Contain("IsEnabled        = false");
        source.Should().Contain("FontSize            = plan.LabelFontSize");
        source.Should().Contain("Margin              = new Thickness(0, 0, 0, plan.LabelBottomMargin)");
        source.Should().Contain("BorderThickness = new Thickness(plan.ThumbnailBorderThickness)");
        source.Should().Contain("Margin      = new Thickness(plan.ItemMarginHorizontal, plan.ItemMarginVertical)");
        source.Should().Contain("Foreground          = BrushFromHex(plan.LabelForegroundHex)");
        source.Should().Contain("HorizontalAlignment = plan.CenterThumbnailContent");
        source.Should().Contain("Background      = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBackgroundHex : plan.ItemNormalBackgroundHex)");
        source.Should().Contain("BorderBrush     = BrushFromHex(plan.IsSelected ? plan.ItemSelectedBorderHex : plan.ItemNormalBorderHex)");
        source.Should().Contain("BorderThickness = new Thickness(plan.IsSelected ? plan.SelectedBorderThickness : plan.NormalBorderThickness)");
        source.Should().Contain("CornerRadius    = new CornerRadius(plan.ItemCornerRadius)");
        source.Should().Contain("Tag         = plan.SlideIndex");
        source.Should().Contain("IsSelected  = plan.IsSelected");
        source.Should().Contain("ToolTip.SetTip(item, plan.ToolTipText)");
        source.Should().Contain("ContextMenu = BuildSlidePaneContextMenu(plan.SlideIndex)");
        source.Should().Contain("SlidePanePlanner.BuildBottomNewSlideAffordance(");
        source.Should().Contain("Content                    = plan.Text");
        source.Should().Contain("IsVisible                  = plan.IsVisible");
        source.Should().Contain("IsEnabled                  = plan.Action.IsEnabled");
        source.Should().Contain("AutomationProperties.SetName(button, plan.AccessibleName)");
        source.Should().Contain("ToolTip.SetTip(button, plan.ToolTipText)");
        source.Should().Contain("button.Click += (_, _) => InsertSlideFromSlidePaneAffordance();");
        source.Should().Contain("slidePaneHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });");
        source.Should().Contain("slidePaneListHost.Children.Add(_slidePaneList);");
        source.Should().Contain("slidePaneHost.Children.Add(_slidePaneNewSlideButton);");
        source.Should().Contain("SlidePanePlanner.BuildContextActions(_presentation.Slides.Count, slideIndex)");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(Editor, action)");
        source.Should().Contain("SlideSectionPlanner.BuildSlideContextActions(");
        source.Should().Contain("SlideSectionPlanner.BuildSectionHeaderActions(");
        source.Should().Contain("SlideSectionPlanner.BuildExecutionPlan(action)");
        source.Should().Contain("var execution = SlideSectionPlanner.BuildExecutionPlan(action)");
        source.Should().Contain("SlideSectionPlanner.TryApplyAction(Editor, execution, promptedName)");
        source.Should().Contain("PointerPressed += OnSlidePaneItemPointerPressed");
        source.Should().Contain("Editor.SelectSlide(sourceSlideIndex);");
        source.IndexOf("Editor.SelectSlide(sourceSlideIndex);", StringComparison.Ordinal)
            .Should().BeGreaterThan(source.IndexOf("SlidePanePlanner.BeginDragSession(", StringComparison.Ordinal),
                "the clicked thumbnail must be selected as part of the same WPF-equivalent pointer-press route");
        source.Should().Contain("PointerMoved += OnSlidePaneItemPointerMoved");
        source.Should().Contain("PointerReleased += OnSlidePaneItemPointerReleased");
        source.Should().Contain("SlidePanePlanner.BeginDragSession(");
        source.Should().Contain("SlidePanePlanner.UpdateDragSession(");
        source.Should().Contain("SlidePanePlanner.CompleteDragSession(");
        source.Should().Contain("SlidePanePlanner.CancelDragSession(");
        source.Should().Contain("ShowSlidePaneInsertionIndicator(update.DropVisualPlan)");
        source.Should().Contain("SlidePanePlanner.DefaultSlideItemHeight");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorThickness");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorAccentHex");
        source.Should().Contain("_slidePaneInsertionIndicator.Background = BrushFromHex(plan.AccentColorHex)");
        source.Should().Contain("SlidePanePlanner.PlanMoveAction(");
        source.Should().Contain("private bool InsertSlideFromSlidePaneAffordance()");
        source.Should().Contain("SlidePanePlanner.TryApplyBottomNewSlideAffordance(Editor)");
        source.Should().Contain("SelectSlidePaneItem(Editor.CurrentSlideIndex)");
        source.Should().Contain("UpdateSlidePaneItemChrome()");
        source.Should().NotContain("SlidePaneAvaloniaSlideItemHeight");
        source.Should().NotContain("for (int i = 0; i < _presentation.Slides.Count; i++)");
        source.Should().NotContain("Text                = $\"{slideIdx + 1}\"");
        source.Should().NotContain("Width        = 148");
        source.Should().NotContain("Height       = 84");
        slidePaneSource.Should().NotContain("Color.FromRgb(0xF5, 0xF5, 0xF5)");
        slidePaneSource.Should().NotContain("Color.FromRgb(0xFF, 0xE0, 0xD6)");
        slidePaneSource.Should().NotContain("Color.FromRgb(0xEB, 0xEB, 0xEB)");
        source.Should().NotContain("Math.Abs(itemPosition.Y - _slidePaneDragStartPoint.Y) < 5");
        source.Should().NotContain("Math.Abs(itemPosition.Y - _slidePaneDragStartPoint.Y) < SlidePanePlanner.DefaultDragStartThreshold");
        source.Should().NotContain("SlidePanePlanner.HitTestInsertionPoint(");
        source.Should().NotContain("SlidePanePlanner.BuildDropVisualPlan(");
        source.Should().NotContain("_slidePaneCollapsedSectionIds");
        source.Should().NotContain("_slidePaneDragSession");
        source.Should().NotContain("new Thickness(0, indicatorY - 1, 0, 0)");
        source.Should().NotContain("_slidePaneList.SelectedIndex = Editor.CurrentSlideIndex");
        slidePaneSource.Should().NotContain("Editor.DuplicateCurrentSlide();");
        slidePaneSource.Should().NotContain("Editor.DeleteCurrentSlide();");
        slidePaneSource.Should().NotContain("Editor.MoveSlide(");
        slidePaneSource.Should().NotContain("Editor.AddSectionAtSlide(action.SlideIndex");
        slidePaneSource.Should().NotContain("Editor.RenameSection(action.SectionIndex");
        slidePaneSource.Should().NotContain("Editor.RemoveSection(action.SectionIndex)");
        slidePaneSource.Should().NotContain("Editor.RemoveAllSections()");
    }

}
