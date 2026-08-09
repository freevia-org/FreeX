using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PrintRendererContentPolicySourceGuardTests
{
    [Fact]
    public void WpfRenderer_ConsumesSharedPageContentPlan()
    {
        var renderer = DialogSourceTestSupport.ReadHostSources("PrintRenderer.cs");
        var pageMaterializer = DialogSourceTestSupport.ReadHostSources("PrintRenderer.HeaderFooter.cs");
        var headerDrawing = DialogSourceTestSupport.ReadHostSources("PrintRenderer.HeaderFooterDrawing.cs");
        var headerPictures = DialogSourceTestSupport.ReadHostSources("PrintRenderer.HeaderFooterPictures.cs");
        var cells = DialogSourceTestSupport.ReadHostSources("PrintRenderer.GridCells.cs");
        var comments = DialogSourceTestSupport.ReadHostSources("PrintRenderer.Comments.cs");
        var avalonia = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Avalonia",
            "AvaloniaPrintPreviewPaginationContext.cs");
        var sharedContext = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation",
            "PageLayout",
            "PrintPreviewPaginationContext.cs");
        var workbookContext = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation",
            "PageLayout",
            "PrintPreviewWorkbookPaginationContext.cs");
        var portableBuilder = WorkspaceFileLocator.ReadAllText(
            "src",
            "FreeX.App.Presentation",
            "PageLayout",
            "PageContentRenderModelBuilder.cs");

        renderer.Should().Contain("WorksheetPrintPageContentPlanner.Build(");
        renderer.Should().Contain("WorksheetPrintMaterializationProfile.WpfNative");
        renderer.Should().NotContain("BuildPrintableHyperlinkLookup");
        renderer.Should().NotContain("BuildColumnWidthsPixels(");

        pageMaterializer.Should().Contain("WorksheetPrintPageContentPlan plan");
        pageMaterializer.Should().Contain("plan.Drawings.Pictures");
        pageMaterializer.Should().Contain("plan.Drawings.TextBoxes");
        pageMaterializer.Should().Contain("plan.Comments.DisplayedComments");
        pageMaterializer.Should().Contain("transform.PageClip.Left");
        pageMaterializer.Should().NotContain("PageGeometryRules.ResolveUniformScale");
        pageMaterializer.Should().NotContain("PagePictureLayoutPlanner.Build");
        pageMaterializer.Should().NotContain("PageTextBoxLayoutPlanner.Build");
        pageMaterializer.Should().NotContain("ResolveHeaderFooterForPage");
        pageMaterializer.Should().NotContain("CommentNavigationPlanner.FormatThreadedComment");
        headerDrawing.Should().Contain("WorksheetPrintHeaderFooterPlan plan");
        headerDrawing.Should().Contain("WorksheetPrintHeaderFooterGeometryPlanner.ResolveSectionBounds(");
        headerPictures.Should().Contain("WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(");
        headerPictures.Should().Contain("WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(");
        headerPictures.Should().Contain("WorksheetPrintHeaderFooterGeometryPlanner.ResolveLineHeight(");

        cells.Should().Contain("WorksheetPrintCellGeometryPlanner.MeasureOverflowWidth(");
        cells.Should().Contain("WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(");
        cells.Should().Contain("WorksheetPrintCellGeometryPlanner.ResolveBorderWinner(");
        comments.Should().Contain("IReadOnlyList<PageDisplayedCommentBlock> comments");
        comments.Should().NotContain("WorksheetPageLayout.GetDisplayedCommentOverlays");

        avalonia.Should().Contain("WorksheetPrintPageContentPlanner.Build(");
        avalonia.Should().Contain("WorksheetPrintMaterializationProfile.AvaloniaPreview");
        sharedContext.Should().Contain("WorksheetPrintPageContentPlanner.Build(");
        sharedContext.Should().Contain("BuildContentPlan(");
        workbookContext.Should().Contain("BuildContentPlan(");
        workbookContext.Should().Contain("PrintPreviewInstructionBuilder.Build(plan)");
        portableBuilder.Should().Contain("WorksheetPrintPageContentPlanner.ResolveHeaderFooterVariant(");
        portableBuilder.Should().Contain("WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(");
        portableBuilder.Should().Contain("WorksheetPrintCellGeometryPlanner.MeasureMergedColumnSpan(");
        portableBuilder.Should().NotContain("ResolveHeaderFooterForPage(");
        portableBuilder.Should().NotContain("MeasureMergedExtent(");
    }
}
