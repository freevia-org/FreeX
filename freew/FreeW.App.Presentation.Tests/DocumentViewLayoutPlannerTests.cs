using System.IO;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class DocumentViewLayoutPlannerTests
{
    [Fact]
    public void BuildSurfacePlan_PrintLayout_UsesPageMarginsAndDeskGeometry()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72,
            MarginTopPt = 72,
            MarginBottomPt = 72,
        };

        var plan = DocumentViewLayoutPlanner.BuildSurfacePlan(
            page,
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 816);

        plan.PageWidthDip.Should().BeApproximately(816, 0.01);
        plan.PageHeightDip.Should().BeApproximately(1056, 0.01);
        plan.PageLeftDip.Should().Be(24);
        plan.ContentLeftDip.Should().BeApproximately(120, 0.01);
        plan.ContentWidthDip.Should().BeApproximately(624, 0.01);
        plan.TextAreaHeightDip.Should().BeApproximately(864, 0.01);
        plan.PageTopDip(1).Should().BeApproximately(1100, 0.01);
        plan.ScrollableHeightForPages(2).Should().BeApproximately(2272, 0.01);
    }

    [Fact]
    public void BuildSurfacePlan_WebAndDraftKeepContinuousSingleSurfaceGeometry()
    {
        var page = new PageSettings();

        var web = DocumentViewLayoutPlanner.BuildSurfacePlan(
            page,
            DocumentViewLayoutKind.WebLayout,
            availableWidthDip: 1200);
        var draft = DocumentViewLayoutPlanner.BuildSurfacePlan(
            page,
            DocumentViewLayoutKind.Draft,
            availableWidthDip: 900);

        web.IsPrintLayout.Should().BeFalse();
        web.ContentLeftDip.Should().Be(24);
        web.ContentWidthDip.Should().Be(1000);
        web.PageIndexFromPageSpaceY(9000).Should().Be(0);

        draft.IsPrintLayout.Should().BeFalse();
        draft.ContentLeftDip.Should().Be(16);
        draft.ContentWidthDip.Should().Be(868);
        draft.ContentYToPageSpaceY(50, columnCount: 3).Should().Be(66);
    }

    [Fact]
    public void BuildColumnPlan_UsesSameColumnGeometryForPlatformRenderers()
    {
        var page = new PageSettings
        {
            ColumnCount = 2,
            ColumnSpacingPt = 36,
            ColumnsLineBetween = true,
        };

        var plan = DocumentViewLayoutPlanner.BuildColumnPlan(
            page,
            contentWidthDip: 624,
            usePageColumns: true);

        plan.Count.Should().Be(2);
        plan.GapDip.Should().BeApproximately(48, 0.01);
        plan.WidthDip.Should().BeApproximately(288, 0.01);
        plan.LineBetween.Should().BeTrue();
        plan.LeftDip(contentLeftDip: 120, columnIndex: 1).Should().BeApproximately(456, 0.01);
    }

    [Fact]
    public void BuildColumnPlan_UnequalColumnsUseNarrowestWidthAndNonPrintModesCollapseToSingleColumn()
    {
        var page = new PageSettings
        {
            ColumnCount = 3,
            ColumnSpacingPt = 18,
            ColumnWidthsPt = [90, 120, 180],
        };

        var print = DocumentViewLayoutPlanner.BuildColumnPlan(page, contentWidthDip: 640, usePageColumns: true);
        var continuous = DocumentViewLayoutPlanner.BuildColumnPlan(page, contentWidthDip: 640, usePageColumns: false);

        print.Count.Should().Be(3);
        print.WidthDip.Should().BeApproximately(120, 0.01);
        print.GapDip.Should().BeApproximately(24, 0.01);

        continuous.Count.Should().Be(1);
        continuous.WidthDip.Should().Be(640);
        continuous.GapDip.Should().Be(0);
        continuous.LineBetween.Should().BeFalse();
    }

    [Fact]
    public void BuildGridlinesAndRulerTicks_ArePageSpacePlans()
    {
        var page = new PageSettings
        {
            WidthPt = 144,
            HeightPt = 144,
            MarginLeftPt = 18,
            MarginRightPt = 18,
            MarginTopPt = 18,
            MarginBottomPt = 18,
        };
        var plan = DocumentViewLayoutPlanner.BuildSurfacePlan(
            page,
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 400,
            new DocumentViewLayoutOptions(
                MinPrintPageWidthDip: 0,
                MinPrintPageHeightDip: 0,
                MinContentWidthDip: 0,
                MinPrintTextAreaHeightDip: 0,
                MinHorizontalGutterDip: 0,
                DeskPaddingDip: 24,
                PageGapDip: 20,
                WebMaxContentWidthDip: 1000,
                WebInsetDip: 24,
                DraftInsetDip: 16));

        var gridlines = DocumentViewLayoutPlanner.BuildGridlines(plan, pageCount: 2, stepDip: 72);
        var ticks = DocumentViewLayoutPlanner.BuildRulerTicks(plan, tickStepDip: 72);

        gridlines.Should().Contain(g => g.Y1 == 48 && g.Y2 == 48);
        gridlines.Should().Contain(g => g.Y1 == 260 && g.Y2 == 260);
        ticks.Should().Equal(104, 176, 248);
    }

    [Fact]
    public void BuildFloatingObjectPlacement_ResolvesParagraphAndPageAnchors()
    {
        var surface = DocumentViewLayoutPlanner.BuildSurfacePlan(
            new PageSettings(),
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 816);

        var paragraph = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            surface,
            anchorContentYDip: 0,
            columnCount: 1,
            HorizontalAnchor.Column,
            horizontalOffsetPt: 36,
            VerticalAnchor.Paragraph,
            verticalOffsetPt: 72);
        var page = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            surface,
            anchorContentYDip: 0,
            columnCount: 1,
            HorizontalAnchor.Page,
            horizontalOffsetPt: 18,
            VerticalAnchor.Page,
            verticalOffsetPt: 36);

        paragraph.XDip.Should().BeApproximately(168, 0.01);
        paragraph.YDip.Should().BeApproximately(216, 0.01);
        paragraph.AnchorPageIndex.Should().Be(0);

        page.XDip.Should().BeApproximately(48, 0.01);
        page.YDip.Should().BeApproximately(72, 0.01);
        page.AnchorPageIndex.Should().Be(0);
    }

    [Fact]
    public void BuildFloatingObjectPlacement_ResolvesMarginAndContinuousAnchors()
    {
        var print = DocumentViewLayoutPlanner.BuildSurfacePlan(
            new PageSettings(),
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 816);
        var web = DocumentViewLayoutPlanner.BuildSurfacePlan(
            new PageSettings(),
            DocumentViewLayoutKind.WebLayout,
            availableWidthDip: 1200);

        var margin = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            print,
            anchorContentYDip: 0,
            columnCount: 1,
            new FloatingPlacement
            {
                HorizontalAnchor = HorizontalAnchor.Margin,
                HorizontalOffsetPt = 18,
                VerticalAnchor = VerticalAnchor.Margin,
                VerticalOffsetPt = 36,
            });
        var continuous = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            web,
            anchorContentYDip: 500,
            columnCount: 1,
            HorizontalAnchor.Page,
            horizontalOffsetPt: 9,
            VerticalAnchor.Paragraph,
            verticalOffsetPt: 18);

        margin.XDip.Should().BeApproximately(144, 0.01);
        margin.YDip.Should().BeApproximately(168, 0.01);

        continuous.XDip.Should().BeApproximately(36, 0.01);
        continuous.YDip.Should().BeApproximately(548, 0.01);
        continuous.AnchorPageIndex.Should().Be(0);
    }

    [Fact]
    public void BuildFloatingOverlaySurfacePlan_PreservesWpfOverlayAnchorCoordinates()
    {
        var page = new PageSettings
        {
            MarginLeftPt = 72,
            MarginTopPt = 72,
            MarginRightPt = 72,
            MarginBottomPt = 72,
        };

        var printSurface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
            page,
            printLayout: true,
            plainInsetDip: 48);
        var continuousSurface = DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(
            page,
            printLayout: false,
            plainInsetDip: 48);

        var printMargin = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            printSurface,
            anchorContentYDip: 0,
            columnCount: 1,
            HorizontalAnchor.Margin,
            horizontalOffsetPt: 18,
            VerticalAnchor.Margin,
            verticalOffsetPt: 36);
        var continuousPage = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            continuousSurface,
            anchorContentYDip: 0,
            columnCount: 1,
            HorizontalAnchor.Page,
            horizontalOffsetPt: 18,
            VerticalAnchor.Page,
            verticalOffsetPt: 36);
        var continuousMargin = DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(
            continuousSurface,
            anchorContentYDip: 0,
            columnCount: 1,
            HorizontalAnchor.Margin,
            horizontalOffsetPt: 18,
            VerticalAnchor.Margin,
            verticalOffsetPt: 36);

        printMargin.XDip.Should().BeApproximately(120, 0.01);
        printMargin.YDip.Should().BeApproximately(144, 0.01);

        continuousPage.XDip.Should().BeApproximately(24, 0.01);
        continuousPage.YDip.Should().BeApproximately(48, 0.01);
        continuousMargin.XDip.Should().BeApproximately(72, 0.01);
        continuousMargin.YDip.Should().BeApproximately(96, 0.01);
    }

    [Fact]
    public void BuildWrapExclusionPlans_ShrinkLinesAndPromoteWideFloatsBelow()
    {
        var leftZone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(100, 40, 80, 60),
            ImageWrapping.Square);
        var rightZone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(320, 40, 70, 60),
            ImageWrapping.Tight);

        var lateral = DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(
            [leftZone!, rightZone!],
            lineTopDip: 50,
            lineHeightDip: 16,
            columnLeftDip: 100,
            columnWidthDip: 300);

        lateral.LeftDeltaDip.Should().BeApproximately(89, 0.01);
        lateral.RightShrinkDip.Should().BeApproximately(89, 0.01);

        var surface = new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.PrintLayout,
            PageWidthDip: 400,
            PageHeightDip: 600,
            MarginLeftDip: 50,
            MarginTopDip: 50,
            MarginRightDip: 50,
            MarginBottomDip: 50,
            PageLeftDip: 0,
            ContentLeftDip: 50,
            ContentWidthDip: 300,
            TextAreaHeightDip: 200,
            DeskPaddingDip: 10,
            PageGapDip: 20);
        var wideZone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(55, 60, 290, 40),
            ImageWrapping.Square);

        var bottom = DocumentViewLayoutPlanner.BuildTopAndBottomWrapExclusionBottom(
            [wideZone!],
            lineTopDip: 70,
            lineHeightDip: 14,
            contentLeftDip: 50,
            columnCount: 2,
            columnWidthDip: 150,
            columnGapDip: 20);
        var advanced = DocumentViewLayoutPlanner.BuildContentYAfterTopAndBottomWrapExclusion(
            surface,
            currentContentYDip: 0,
            peekContentYDip: 0,
            bottom!.Value,
            columnCount: 2);

        bottom.Should().BeApproximately(100, 0.01);
        advanced.Should().BeApproximately(240, 0.01);
    }

    [Fact]
    public void BuildFloatingHandleGeometry_HitTestsMovesAndResizesSelectionRects()
    {
        var rect = new DocumentFloatRect(10, 20, 100, 80);

        var handles = DocumentViewLayoutPlanner.BuildFloatingHandleRects(rect, handleSizeDip: 8);
        var topLeft = handles.Single(h => h.Handle == DocumentFloatingHandle.TopLeft);
        var hitTopLeft = DocumentViewLayoutPlanner.HitTestFloatingHandle(
            rect,
            new DocumentFloatPoint(6, 16),
            handleSizeDip: 8,
            hitPaddingDip: 0);
        var hitBody = DocumentViewLayoutPlanner.HitTestFloatingHandle(
            rect,
            new DocumentFloatPoint(60, 60),
            handleSizeDip: 8,
            hitPaddingDip: 0);
        var moved = DocumentViewLayoutPlanner.BuildFloatingMoveRect(
            rect,
            new DocumentFloatPoint(60, 60),
            new DocumentFloatPoint(70, 85));
        var resized = DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            rect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(150, 140),
            preserveAspect: false,
            minimumSizeDip: 20);
        var clamped = DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            rect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(5, 5),
            preserveAspect: false,
            minimumSizeDip: 20);

        handles.Should().HaveCount(8);
        topLeft.Rect.Should().Be(new DocumentFloatRect(6, 16, 8, 8));
        hitTopLeft.Should().Be(DocumentFloatingHandle.TopLeft);
        hitBody.Should().Be(DocumentFloatingHandle.Body);
        moved.Should().Be(new DocumentFloatRect(20, 45, 100, 80));
        resized.Should().Be(new DocumentFloatRect(10, 20, 140, 120));
        clamped.Should().Be(new DocumentFloatRect(10, 20, 20, 20));
    }
}

public sealed class DocumentViewLayoutPlannerSourceGuardTests
{
    [Fact]
    public void PlatformDocumentViews_DelegatePageAndColumnGeometryToPresentationPlanner()
    {
        var hostSource = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaSource = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        hostSource.Should().Contain("using FreeW.App.Presentation.DocumentView;");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildPageMetrics(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildColumnPlan(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(");

        avaloniaSource.Should().Contain("using FreeW.App.Presentation.DocumentView;");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildSurfacePlan(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildColumnPlan(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildTopAndBottomWrapExclusionBottom(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingHandleRects(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingResizeRect(");
        avaloniaSource.Should().Contain("BuildGridlines(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildRulerTicks(");
    }

    [Fact]
    public void AvaloniaDocumentView_DoesNotReownNeutralPageOrColumnMath()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        source.Should().NotContain("Math.Max(320, _doc.Page.WidthPt * PxPerPoint)");
        source.Should().NotContain("Math.Max(400, _doc.Page.HeightPt * PxPerPoint)");
        source.Should().NotContain("_contentWidth = Math.Max(120, _pageWidth - marginLeft - marginRight)");
        source.Should().NotContain("(_contentWidth - (pageColCount - 1) * gapDip) / pageColCount");
        source.Should().NotContain("for (var x = _pageLeft; x <= _pageLeft + _pageWidth + 0.01; x += inchDip)");
        source.Should().NotContain("var anchorPageIndex = _viewMode == DocumentViewMode.PrintLayout");
        source.Should().NotContain("HorizontalAnchor.Page   => _pageLeft");
        source.Should().NotContain("PageTop(anchorPageIndex)");
        source.Should().NotContain("private const double WrapGap");
        source.Should().NotContain("var freeLeft  = rect.Left");
        source.Should().NotContain("var hx = new[] { rect.X");
        source.Should().NotContain("right  = Math.Max(pointer.X");
    }

    private static string ReadSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = FindRepositoryRoot();
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return File.ReadAllText(Path.Combine(parts));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FreeW.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
