using System.IO;

namespace FreeP.App.Host.Tests;

public sealed class SlidePanePolicySourceGuardTests
{
    [Fact]
    public void SlidePane_DelegatesSectionProjectionAndDragMathToPresentationPlanner()
    {
        var source = ReadHostSource("SlidePane.cs");

        source.Should().Contain("SlidePanePlanner.BuildSessionProjection(");
        source.Should().Contain("SlidePanePlanner.SetSelectedSlide(");
        source.Should().Contain("SlidePanePlanner.ToggleSection(");
        source.Should().Contain("SlidePanePlanner.BuildThumbnailVisualPlan(");
        source.Should().Contain("SlidePanePlanner.BuildSectionHeaderVisualPlan(entry)");
        source.Should().Contain("_sessionState");
        source.Should().Contain("_sessionProjection");
        source.Should().Contain("ToggleSection(plan.SectionId)");
        source.Should().Contain("SlidePanePlanner.BeginDragSession(");
        source.Should().Contain("SlidePanePlanner.UpdateDragSession(");
        source.Should().Contain("SlidePanePlanner.CompleteDragSession(");
        source.Should().Contain("SlidePanePlanner.CancelDragSession(");
        source.Should().Contain("ShowInsertIndicator(update.DropVisualPlan)");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorThickness");
        source.Should().Contain("SlidePanePlanner.DefaultDropIndicatorAccentHex");
        source.Should().Contain("SlidePanePlanner.BuildContextCommandRoute(");
        source.Should().Contain("route.SlideAction is { } slideAction");
        source.Should().Contain("route.SectionExecution is { } sectionExecution");
        source.Should().Contain("SlidePanePlanner.TryApplyAction(");
        source.Should().Contain("SlidePanePlanner.BuildBottomNewSlideAffordance(");
        source.Should().Contain("Content             = plan.Text");
        source.Should().Contain("Visibility          = plan.IsVisible ? Visibility.Visible : Visibility.Collapsed");
        source.Should().Contain("IsEnabled           = plan.Action.IsEnabled");
        source.Should().Contain("ToolTip             = plan.ToolTipText");
        source.Should().Contain("AutomationProperties.SetName(btn, plan.AccessibleName)");
        source.Should().Contain("SlidePanePlanner.TryApplyBottomNewSlideAffordance(_editor)");
        source.Should().Contain("Width            = plan.ThumbnailWidth");
        source.Should().Contain("Height           = plan.ThumbnailHeight");
        source.Should().Contain("FontSize            = plan.LabelFontSize");
        source.Should().Contain("Margin              = new Thickness(0, 0, 0, plan.LabelBottomMargin)");
        source.Should().Contain("BorderThickness = new Thickness(plan.ThumbnailBorderThickness)");
        source.Should().Contain("Margin          = new Thickness(");
        source.Should().Contain("plan.ItemMarginHorizontal");
        source.Should().Contain("plan.ItemMarginVertical");
        source.Should().Contain("ToolTip         = plan.ToolTipText");
        source.Should().Contain("AutomationProperties.SetName(item, plan.AccessibleName)");
        source.Should().Contain("Text              = plan.DisclosureText");
        source.Should().Contain("Foreground        = BrushFromHex(plan.ForegroundHex)");
        source.Should().Contain("Background      = normalBackground");
        source.Should().Contain("MouseEnter += (_, _) => header.Background = hoverBackground");
        source.Should().Contain("AutomationProperties.SetName(header, plan.AccessibleName)");
        source.Should().Contain("SlideSectionPlanner.BuildSlideContextActions(");
        source.Should().Contain("SlideSectionPlanner.BuildSectionHeaderActions(");
        source.Should().Contain("SlideSectionPlanner.TryApplyAction(_editor, execution, promptedName)");
        source.Should().NotContain("var kind = command switch");
        source.Should().NotContain("private const double ThumbWidth");
        source.Should().NotContain("private const double ThumbHeight");
        source.Should().NotContain("private const double ItemPadding");
        source.Should().NotContain("private const double LabelHeight");
        source.Should().NotContain("new Dictionary<int, PresentationSection>");
        source.Should().NotContain("sectionHeaderBefore");
        source.Should().NotContain("const double SectionHeaderHeight");
        source.Should().NotContain("SectionHeaderBg");
        source.Should().NotContain("SectionHeaderFg");
        source.Should().NotContain("runningY + ItemHeight * 0.5");
        source.Should().NotContain("Math.Abs(pos.Y - _dragStartPoint.Y) < 5");
        source.Should().NotContain("Math.Abs(pos.Y - _dragStartPoint.Y) < SlidePanePlanner.DefaultDragStartThreshold");
        source.Should().NotContain("SlidePanePlanner.HitTestInsertionPoint(");
        source.Should().NotContain("SlidePanePlanner.BuildDropVisualPlan(");
        source.Should().NotContain("_collapsedSectionIds");
        source.Should().NotContain("_dragSession");
        source.Should().NotContain("new Thickness(0, indicatorY - 1, 0, 0)");
        source.Should().NotContain("\"+ New Slide\"");
        source.Should().NotContain("\"Duplicate Slide\"");
        source.Should().NotContain("_editor.InsertSlide();");
        source.Should().NotContain("_editor.DuplicateCurrentSlide();");
        source.Should().NotContain("_editor.DeleteCurrentSlide();");
        source.Should().NotContain("_editor.MoveSlide(from, to);");
        source.Should().NotContain("_editor.AddSectionAtSlide(action.SlideIndex");
        source.Should().NotContain("_editor.RenameSection(action.SectionIndex");
        source.Should().NotContain("_editor.RemoveSection(action.SectionIndex)");
        source.Should().NotContain("_editor.RemoveAllSections()");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx"), "freep", "FreeP.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
