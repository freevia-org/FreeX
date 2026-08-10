using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class FreeXPracticalResidualOwnershipTests
{
    [Fact]
    public void ViewportAndSelectionPolicies_HavePortableOwners()
    {
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var viewportPlanner = Read("src", "FreeX.App.Services", "WorkbookViewportScrollPlanner.cs");
        var findPlanner = Read("src", "FreeX.App.Services", "FindReplaceDialogPlanner.cs");
        var wpfFind = Read("src", "FreeX.App.Host", "FindReplaceDialog.xaml.cs");

        avalonia.Should().Contain("ViewportService.CountScrollableRows(viewport.RowMetrics");
        avalonia.Should().Contain("ViewportService.CountScrollableColumns(viewport.ColMetrics");
        viewportPlanner.Should().Contain("ViewportService.CountScrollableRows(viewport.RowMetrics");
        viewportPlanner.Should().Contain("ViewportService.CountScrollableColumns(viewport.ColMetrics");
        findPlanner.Should().Contain("public static IReadOnlyList<GridRange>? ResolveSelectionScopeAtOpen");
        avalonia.Should().Contain("FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(");
        wpfFind.Should().Contain("FindReplaceDialogPlanner.ResolveSelectionScopeAtOpen(");
    }

    [Fact]
    public void CellCommentAndFillSemantics_HavePortableOwners()
    {
        var wpfGrid = Read("src", "FreeX.App.UI", "GridView.cs");
        var wpfComments = Read("src", "FreeX.App.UI", "GridView.CommentPreview.cs");
        var wpfFill = Read("src", "FreeX.App.Host", "MainWindow.HomeEditing.cs");
        var avalonia = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");
        var session = Read("src", "FreeX.App.Services", "WorkbookSession.cs");

        wpfGrid.Should().Contain("CellAnnouncementPlanner.BuildName(");
        avalonia.Should().Contain("CellAnnouncementPlanner.BuildName(");
        wpfGrid.Should().NotContain("record struct CellAnnouncementMetadata");
        avalonia.Should().NotContain("FormatCellAccessibleName");
        wpfComments.Should().Contain("ThreadedCommentDialogPlanner.DescribeReply(");
        wpfComments.Should().Contain("ThreadedCommentTimestampProfile.InlineRelativeLocal");
        wpfComments.Should().NotContain("private static string SummarizeReplyText");
        wpfFill.Should().Contain("WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle");
        session.Should().Contain("WorksheetCommandPresentationCatalog.DescribeFill(direction).CommandTitle");
        session.Should().NotContain("GetFillCellsTitle");
        avalonia.Should().Contain("WorksheetCommandPresentationCatalog.FormatFillStatus(direction, rangeReference)");
        avalonia.Should().NotContain("FormatFillCellsAction");
    }

    [Fact]
    public void RendererGeometryTimelineAndKeyTips_AreThinRealizers()
    {
        var wpfGlyph = Read("src", "FreeX.App.UI", "ConditionalIconGlyphRenderer.cs");
        var avaloniaGlyph = Read("src", "FreeX.App.Avalonia", "ConditionalFormatIconGlyphFactory.cs");
        var wpfTimeline = Read("src", "FreeX.App.UI", "GridView.DrawingObjects.cs");
        var avaloniaTimeline = Read("src", "FreeX.App.Avalonia", "MainWindow.SlicerTimeline.cs");
        var wpfKeyTips = Read("src", "FreeX.App.Host", "MenuKeyTipAssigner.cs");
        var avaloniaKeyTips = Read("src", "FreeX.App.Avalonia", "AvaloniaPivotChartContextMenus.cs");

        wpfGlyph.Should().Contain("ConditionalIconGlyphGeometry.PlanStarFill(op)");
        avaloniaGlyph.Should().Contain("ConditionalIconGlyphGeometry.PlanStarFill(op)");
        wpfGlyph.Should().NotContain("double.MaxValue");
        avaloniaGlyph.Should().NotContain("double.MaxValue");
        wpfTimeline.Should().Contain("layout.GranularityLabel");
        wpfTimeline.Should().Contain("layout.ClearFilterGlyph");
        avaloniaTimeline.Should().Contain("layout.GranularityLabel");
        avaloniaTimeline.Should().Contain("layout.ClearFilterGlyph");
        wpfKeyTips.Should().Contain("MenuKeyTipAssignmentPlanner.AssignUnique(");
        avaloniaKeyTips.Should().Contain("MenuKeyTipAssignmentPlanner.AssignUnique(");
        wpfKeyTips.Should().NotContain("PreserveExistingKeyTip");
        avaloniaKeyTips.Should().NotContain("RibbonKeyTipText.CreateUniqueKeyTip");
    }

    private static string Read(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
