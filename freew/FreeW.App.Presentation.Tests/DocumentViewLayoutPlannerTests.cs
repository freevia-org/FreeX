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
    public void BuildDropCapLayoutPlan_DroppedReservesFirstLinesBesideCap()
    {
        var paragraph = new Paragraph("Hello world");
        DropCap.ApplyDropCap(
            paragraph,
            DropCapPosition.Dropped,
            sizePt: 42,
            lineSpan: 3,
            distanceFromTextPt: 6);

        var plan = DocumentViewLayoutPlanner.BuildDropCapLayoutPlan(
            paragraph,
            blockIndex: 7,
            paragraphLeftDip: 100,
            paragraphTopDip: 24,
            textWidthDip: 320,
            defaultLineHeightDip: 18);

        plan.Should().NotBeNull();
        plan!.BlockIndex.Should().Be(7);
        plan.RunIndex.Should().Be(0);
        plan.LeadingGlyph.Should().Be("H");
        plan.IsDropped.Should().BeTrue();
        plan.LineSpan.Should().Be(3);
        plan.CapBox.LeftDip.Should().BeApproximately(100, 0.001);
        plan.TextReservation.HeightDip.Should().BeApproximately(54, 0.001);
        plan.BodyTextLeftInsetDip.Should().BeGreaterThan(0);
        plan.BodyTextWidthDip.Should().BeLessThan(320);
    }

    [Fact]
    public void BuildDropCapLayoutPlan_InMarginPlacesCapOutsideColumnWithoutShrinkingBody()
    {
        var paragraph = new Paragraph("Margin");
        DropCap.ApplyDropCap(
            paragraph,
            DropCapPosition.InMargin,
            sizePt: 48,
            lineSpan: 4,
            distanceFromTextPt: 9);

        var plan = DocumentViewLayoutPlanner.BuildDropCapLayoutPlan(
            paragraph,
            blockIndex: 2,
            paragraphLeftDip: 120,
            paragraphTopDip: 40,
            textWidthDip: 360,
            defaultLineHeightDip: 20);

        plan.Should().NotBeNull();
        plan!.IsInMargin.Should().BeTrue();
        plan.LineSpan.Should().Be(4);
        plan.CapBox.RightDip.Should().BeLessThan(120);
        plan.TextReservation.RightDip.Should().BeApproximately(120, 0.001);
        plan.BodyTextLeftInsetDip.Should().Be(0);
        plan.BodyTextWidthDip.Should().BeApproximately(360, 0.001);
    }

    [Fact]
    public void BuildTableLayoutPlans_RecordsSharedWordTableContracts()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildComplexTableLayoutDocument();

        var plan = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document).Single();

        plan.RowCount.Should().Be(5);
        plan.GridColumnCount.Should().Be(4);
        plan.HasHeaderRow.Should().BeTrue();
        plan.RepeatsHeaderRow.Should().BeTrue();
        plan.HasBandedRows.Should().BeTrue();
        plan.HasFirstColumn.Should().BeTrue();
        plan.HasLastColumn.Should().BeFalse();
        plan.HasLastRow.Should().BeFalse();
        plan.HasMergedCells.Should().BeTrue();
        plan.HasVerticalMerges.Should().BeTrue();
        plan.HasCellShading.Should().BeTrue();
        plan.HasCustomCellBorders.Should().BeTrue();
        plan.HasCellMargins.Should().BeTrue();
        plan.HasCellSpacing.Should().BeTrue();
        plan.HasVerticalText.Should().BeTrue();
        plan.HasVerticalAlignment.Should().BeTrue();
        plan.HasPreferredWidths.Should().BeTrue();
        plan.HasNamedStyle.Should().BeTrue();
        plan.Alignment.Should().Be(nameof(TableAlignment.Center));
        plan.AutoFit.Should().Be(nameof(AutoFitMode.Fixed));
        plan.TableStyleId.Should().Be("GridTable4");
        plan.ColumnWidthsDip.Should().HaveCount(4);
        plan.Cells.Where(cell => cell.RowIndex == 0)
            .Should()
            .OnlyContain(cell => cell.ShadingColorHex == null, "header fill is style-derived evidence, not explicit cell shading");
        var headerCell = plan.Cells.Single(cell => cell.RowIndex == 0 && cell.CellIndex == 0);
        headerCell.EffectiveFill.ExplicitFillHex.Should().BeNull();
        headerCell.EffectiveFill.StyleDerivedFillSource.Should().Be("style-derived-header");
        headerCell.EffectiveFill.StyleDerivedFillHex.Should().Be("#2F5496");
        headerCell.EffectiveFill.EffectiveFillSource.Should().Be("style-derived-header");
        headerCell.EffectiveFill.EffectiveFillHex.Should().Be("#2F5496");
        headerCell.EffectiveFill.StyleDerivedBold.Should().BeTrue();
        headerCell.EffectiveFill.EffectiveBold.Should().BeTrue();
        var explicitBodyCell = plan.Cells.Single(cell =>
            cell.RowIndex == 1
            && cell.ShadingColorHex == "#EAF2F8");
        explicitBodyCell.EffectiveFill.ExplicitFillHex.Should().Be("#EAF2F8");
        explicitBodyCell.EffectiveFill.StyleDerivedFillSource.Should().BeNull();
        explicitBodyCell.EffectiveFill.StyleDerivedFillHex.Should().BeNull();
        explicitBodyCell.EffectiveFill.EffectiveFillSource.Should().Be("explicit-cell");
        explicitBodyCell.EffectiveFill.EffectiveFillHex.Should().Be("#EAF2F8");
        explicitBodyCell.EffectiveFill.StyleDerivedBold.Should().BeTrue();
        explicitBodyCell.EffectiveFill.EffectiveBold.Should().BeTrue();
        var bandedBodyCell = plan.Cells.Single(cell => cell.RowIndex == 1 && cell.CellIndex == 1);
        bandedBodyCell.EffectiveFill.StyleDerivedFillSource.Should().Be("style-derived-banded-row");
        bandedBodyCell.EffectiveFill.StyleDerivedFillHex.Should().Be("#BDD7EE");
        bandedBodyCell.EffectiveFill.EffectiveFillSource.Should().Be("style-derived-banded-row");
        bandedBodyCell.EffectiveFill.EffectiveFillHex.Should().Be("#BDD7EE");
        plan.Cells.Where(cell => cell.RowIndex > 0)
            .Should()
            .Contain(cell => cell.ShadingColorHex != null, "body cells still carry explicit cell-shading evidence");
        plan.Cells.Should().Contain(cell => cell.GridSpan == 2);
        plan.Cells.Should().Contain(cell => cell.RowSpan == 2);
        plan.Cells.Should().Contain(cell => cell.IsVerticalMergeContinuation);
        plan.Cells.Should().Contain(cell => cell.TextDirection == nameof(CellTextDirection.Rotate90));
    }

    [Fact]
    public void BuildTablePaginationPlan_RepeatsHeaderAndKeepsRowsTogetherAcrossPages()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();

        var plan = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document).Single();
        var pagination = plan.Pagination;

        pagination.EstimatedPageCount.Should().Be(2);
        pagination.RepeatsHeaderRows.Should().BeTrue();
        pagination.HeaderRowIndexes.Should().Equal(0);
        pagination.HasKeepTogetherRows.Should().BeTrue();
        pagination.Rows.Should().Contain(row =>
            row.RowIndex == 4
            && row.KeepTogether
            && row.AssignedPageNumber == 1);
        pagination.Rows.Should().Contain(row =>
            row.RowIndex == 1
            && row.IsBandedBodyRow);
        pagination.Rows.Should().Contain(row =>
            row.RowIndex == 2
            && !row.IsBandedBodyRow);
        pagination.Pages.Should().HaveCount(2);
        pagination.Pages[0].IncludesRepeatedHeader.Should().BeFalse();
        pagination.Pages[0].SourceRowIndexes.Should().StartWith(0);
        pagination.Pages[1].IncludesRepeatedHeader.Should().BeTrue();
        pagination.Pages[1].RepeatedHeaderRowIndexes.Should().Equal(0);
        pagination.Pages[1].KeepTogetherRowIndexes.Should().Contain(7);
        pagination.Pages[1].RenderRows[0].Should().Match<DocumentTablePaginationRenderRowPlan>(row =>
            row.SourceRowIndex == 0
            && row.IsRepeatedHeader
            && row.StartsPlannedPage
            && row.PageNumber == 2
            && row.PageOffsetYDip == 0);
        pagination.Pages[1].RenderRows[1].Should().Match<DocumentTablePaginationRenderRowPlan>(row =>
            row.SourceRowIndex == pagination.Pages[1].SourceRowIndexes[0]
            && !row.IsRepeatedHeader
            && !row.StartsPlannedPage
            && row.PageOffsetYDip == pagination.HeaderHeightDip);
    }

    [Fact]
    public void BuildTableLayoutPlans_AccountsForLeadingDocumentContentWhenEstimatingFirstTablePage()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePageCompositionStressDocument();

        var pagination = DocumentViewLayoutPlanner.BuildTableLayoutPlans(document).Single().Pagination;

        pagination.EstimatedPageCount.Should().Be(3);
        pagination.Pages.Should().HaveCount(3);
        pagination.Pages[0].SourceRowIndexes.Should().Equal(0, 1, 2);
        pagination.Pages[1].SourceRowIndexes.Should().Equal(3, 4, 5, 6);
        pagination.Pages[2].SourceRowIndexes.Should().Equal(7, 8);
        pagination.Rows.Select(row => row.AssignedPageNumber).Should().Equal(
            1, 1, 1, 2, 2, 2, 2, 3, 3);
        pagination.Pages[0].AvailableHeightDip.Should().BeLessThan(pagination.AvailableBodyHeightDip);
        pagination.Pages[1].IncludesRepeatedHeader.Should().BeTrue();
        pagination.Pages[1].RepeatedHeaderRowIndexes.Should().Equal(0);
        pagination.Pages[1].KeepTogetherRowIndexes.Should().Equal(3, 6);
        pagination.Pages[2].IncludesRepeatedHeader.Should().BeTrue();
        pagination.Pages[2].RepeatedHeaderRowIndexes.Should().Equal(0);
        pagination.Pages.Select(page => page.RenderRows[0].SourceRowIndex).Should().Equal(0, 0, 0);
        pagination.Pages.Select(page => page.RenderRows[0].IsRepeatedHeader).Should().Equal(false, true, true);
    }

    [Fact]
    public void BuildTablePaginationPlan_MarksPlannedPageStartsWithoutRepeatedHeaders()
    {
        var document = FreeWVisualEvidenceDocumentFactory.BuildTablePaginationRepeatHeaderDocument();
        var table = document.Blocks.OfType<Table>().Single();
        table.Formatting = table.Formatting with { RepeatHeaderRow = false };

        var pagination = DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, document.Page);

        pagination.EstimatedPageCount.Should().Be(2);
        pagination.RepeatsHeaderRows.Should().BeFalse();
        pagination.Pages[1].RepeatedHeaderRowIndexes.Should().BeEmpty();
        pagination.Pages[1].RenderRows[0].Should().Match<DocumentTablePaginationRenderRowPlan>(row =>
            row.SourceRowIndex == pagination.Pages[1].SourceRowIndexes[0]
            && !row.IsRepeatedHeader
            && row.StartsPlannedPage
            && row.PageNumber == 2
            && row.PageOffsetYDip == 0);
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
    public void BuildFloatingTextWrapLinePlan_RepresentsSquareTightLineInsets()
    {
        var surface = new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.WebLayout,
            PageWidthDip: 400,
            PageHeightDip: 800,
            MarginLeftDip: 0,
            MarginTopDip: 0,
            MarginRightDip: 0,
            MarginBottomDip: 0,
            PageLeftDip: 0,
            ContentLeftDip: 100,
            ContentWidthDip: 300,
            TextAreaHeightDip: 800,
            DeskPaddingDip: 0,
            PageGapDip: 0);
        var leftZone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(100, 0, 80, 60),
            ImageWrapping.Square);
        var rightZone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(320, 0, 70, 60),
            ImageWrapping.Tight);

        var plan = DocumentViewLayoutPlanner.BuildFloatingTextWrapLinePlan(
            [leftZone!, rightZone!],
            surface,
            currentContentYDip: 10,
            lineContentYDip: 10,
            lineHeightDip: 16,
            contentLeftDip: 100,
            columnCount: 1,
            columnWidthDip: 300,
            columnGapDip: 0,
            baseTextWidthDip: 300);

        plan.HasLateralExclusion.Should().BeTrue();
        plan.HasTopAndBottomAdvance.Should().BeFalse();
        plan.ColumnIndex.Should().Be(0);
        plan.ColumnLeftDip.Should().Be(100);
        plan.LeftDeltaDip.Should().BeApproximately(89, 0.01);
        plan.RightShrinkDip.Should().BeApproximately(89, 0.01);
        plan.EffectiveTextWidthDip.Should().BeApproximately(122, 0.01);
        plan.TextLeftDip().Should().BeApproximately(189, 0.01);
        plan.TextRightDip().Should().BeApproximately(311, 0.01);
    }

    [Fact]
    public void BuildFloatingTextWrapLinePlan_AdvancesPastTopAndBottomBand()
    {
        var surface = new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.WebLayout,
            PageWidthDip: 400,
            PageHeightDip: 800,
            MarginLeftDip: 0,
            MarginTopDip: 0,
            MarginRightDip: 0,
            MarginBottomDip: 0,
            PageLeftDip: 0,
            ContentLeftDip: 100,
            ContentWidthDip: 300,
            TextAreaHeightDip: 800,
            DeskPaddingDip: 0,
            PageGapDip: 0);
        var zone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(100, 20, 300, 40),
            ImageWrapping.TopAndBottom);

        var plan = DocumentViewLayoutPlanner.BuildFloatingTextWrapLinePlan(
            [zone!],
            surface,
            currentContentYDip: 30,
            lineContentYDip: 30,
            lineHeightDip: 14,
            contentLeftDip: 100,
            columnCount: 1,
            columnWidthDip: 300,
            columnGapDip: 0,
            baseTextWidthDip: 300);

        plan.HasTopAndBottomAdvance.Should().BeTrue();
        plan.TopAndBottomExclusionBottomDip.Should().BeApproximately(60, 0.01);
        plan.PlannedContentYDip.Should().BeApproximately(60, 0.01);
        plan.PageSpaceYDip.Should().BeApproximately(60, 0.01);
        plan.EffectiveTextWidthDip.Should().BeApproximately(300, 0.01);
    }

    [Fact]
    public void BuildFloatingTextWrapLinePlan_BothSidesSquareCreatesTwoFragments()
    {
        var surface = new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.WebLayout,
            PageWidthDip: 400,
            PageHeightDip: 800,
            MarginLeftDip: 0,
            MarginTopDip: 0,
            MarginRightDip: 0,
            MarginBottomDip: 0,
            PageLeftDip: 0,
            ContentLeftDip: 100,
            ContentWidthDip: 300,
            TextAreaHeightDip: 800,
            DeskPaddingDip: 0,
            PageGapDip: 0);
        var zone = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(190, 20, 80, 60),
            ImageWrapping.Square,
            FloatingWrapTextSide.BothSides);

        var plan = DocumentViewLayoutPlanner.BuildFloatingTextWrapLinePlan(
            [zone!],
            surface,
            currentContentYDip: 30,
            lineContentYDip: 30,
            lineHeightDip: 14,
            contentLeftDip: 100,
            columnCount: 1,
            columnWidthDip: 300,
            columnGapDip: 0,
            baseTextWidthDip: 300);

        plan.HasSplitTextFragments.Should().BeTrue();
        plan.SplitLine!.FirstWidthDip.Should().BeApproximately(81, 0.01);
        plan.SplitLine.SecondStartDeltaDip.Should().BeApproximately(179, 0.01);
        plan.SplitLine.SecondWidthDip.Should().BeApproximately(121, 0.01);
        plan.SplitLine.EffectiveTextWidthDip.Should().BeApproximately(202, 0.01);
        plan.EffectiveTextWidthDip.Should().BeApproximately(121, 0.01,
            "the established single-side fallback remains available for unsupported text layouts");
    }

    [Fact]
    public void BuildSquareTightWrapExclusion_HonorsSingleSidePolicy()
    {
        var leftOnly = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(190, 20, 80, 60),
            ImageWrapping.Square,
            FloatingWrapTextSide.Left);
        var rightOnly = DocumentViewLayoutPlanner.BuildWrapExclusionZone(
            new DocumentFloatRect(190, 20, 80, 60),
            ImageWrapping.Square,
            FloatingWrapTextSide.Right);

        var leftPlan = DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(
            [leftOnly!], 30, 14, 100, 300);
        var rightPlan = DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(
            [rightOnly!], 30, 14, 100, 300);

        leftPlan.LeftDeltaDip.Should().Be(0);
        leftPlan.RightShrinkDip.Should().BeApproximately(219, 0.01);
        rightPlan.LeftDeltaDip.Should().BeApproximately(179, 0.01);
        rightPlan.RightShrinkDip.Should().Be(0);
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

    // FB1: BuildFloatingResizeRect resolves the pointer against the object's OWN axes when rotated, not
    // the screen axes — dragging the BottomRight handle of a 90°-rotated square to the SCREEN point that
    // un-rotates to local (140,120) must grow the LOCAL rect to that same local target, anchoring the
    // opposite (TopLeft) corner in local space (which is also the correct anchor once the +90° rotation
    // is re-applied for rendering).
    [Fact]
    public void BuildFloatingResizeRect_Rotated90_ResolvesPointerInLocalFrameAndAnchorsOppositeCorner()
    {
        var rect = new DocumentFloatRect(0, 0, 100, 100); // centre (50, 50)

        // Screen point (-20, 140) un-rotates (by -90°) to local (140, 120) — see method doc for the math.
        var resized = DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            rect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(-20, 140),
            preserveAspect: false,
            minimumSizeDip: 20,
            rotationAngle: 90);

        // Anchored TopLeft corner (0,0) stays fixed in LOCAL space; the local rect grows to (140, 120).
        resized.Should().Be(new DocumentFloatRect(0, 0, 140, 120));

        // The SAME drag with rotationAngle defaulted to 0 (screen == local) must NOT reproduce this
        // local-space result — confirms the un-rotate is actually doing something, not a no-op.
        var unrotated = DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            rect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(-20, 140),
            preserveAspect: false,
            minimumSizeDip: 20);
        unrotated.Should().NotBe(resized);
    }

    // FB3: flip composes with the same un-rotate pipeline — a horizontally-flipped object's BottomRight
    // handle drag resolves against the flipped local axes.
    [Fact]
    public void BuildFloatingResizeRect_FlippedHorizontal_ResolvesPointerInLocalFrame()
    {
        var rect = new DocumentFloatRect(0, 0, 100, 100); // centre (50, 50)

        // Screen point (-40, 120) un-flips (FlipH) to local (140, 120).
        var resized = DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            rect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(-40, 120),
            preserveAspect: false,
            minimumSizeDip: 20,
            flipH: true);

        resized.Should().Be(new DocumentFloatRect(0, 0, 140, 120));
    }

    // FB1: BuildFloatingHandleRects must draw handles at the VISIBLE rotated corners, not the
    // axis-aligned ones, so the drawn square and the clickable target for e.g. TopRight sit where the
    // shape is actually rendered after a +90° rotation about its centre.
    [Fact]
    public void BuildFloatingHandleRects_Rotated90_DrawsHandlesAtVisibleRotatedCorners()
    {
        var rect = new DocumentFloatRect(0, 0, 100, 100); // centre (50, 50)

        var handles = DocumentViewLayoutPlanner.BuildFloatingHandleRects(rect, handleSizeDip: 8, rotationAngle: 90);
        var topRight = handles.Single(h => h.Handle == DocumentFloatingHandle.TopRight);

        // Model TopRight (100, 0) rotated +90° about (50,50): relative (50,-50) -> (50, 50) -> absolute (100, 100).
        topRight.Rect.CenterXDip.Should().BeApproximately(100, 0.01);
        topRight.Rect.CenterYDip.Should().BeApproximately(100, 0.01);

        // Without rotation, TopRight stays at the axis-aligned corner (100, 0).
        var unrotatedHandles = DocumentViewLayoutPlanner.BuildFloatingHandleRects(rect, handleSizeDip: 8);
        var unrotatedTopRight = unrotatedHandles.Single(h => h.Handle == DocumentFloatingHandle.TopRight);
        unrotatedTopRight.Rect.CenterXDip.Should().BeApproximately(100, 0.01);
        unrotatedTopRight.Rect.CenterYDip.Should().BeApproximately(0, 0.01);
    }

    // FB1/FB2: HitTestFloatingHandle must resolve a click on the VISIBLE rotated handle to the correct
    // model handle, and a click on the empty (un-rotated) box corner must NOT be mistaken for a handle.
    [Fact]
    public void HitTestFloatingHandle_Rotated90_ResolvesVisibleCornerNotAxisAlignedCorner()
    {
        var rect = new DocumentFloatRect(0, 0, 100, 100);

        // Visible TopRight handle after +90° rotation sits at (100, 100) (see prior test) — a click there
        // must resolve to TopRight even though (100,100) is the axis-aligned BottomRight corner.
        var hitVisibleCorner = DocumentViewLayoutPlanner.HitTestFloatingHandle(
            rect, new DocumentFloatPoint(100, 100), handleSizeDip: 8, hitPaddingDip: 0, rotationAngle: 90);
        hitVisibleCorner.Should().Be(DocumentFloatingHandle.TopRight);

        // A 90° rotation just relabels one axis-aligned corner as another (it stays a square), so use a
        // 45° rotation to probe a genuinely EMPTY box corner: the rotated diamond's vertices pull well
        // away from the box corners, so the un-rotated TopRight corner (100, 0) sits in the gap outside
        // the rotated shape's footprint and must not resolve to any handle.
        var hitEmptyAxisCorner = DocumentViewLayoutPlanner.HitTestFloatingHandle(
            rect, new DocumentFloatPoint(100, 0), handleSizeDip: 8, hitPaddingDip: 0, rotationAngle: 45);
        hitEmptyAxisCorner.Should().Be(DocumentFloatingHandle.None);
    }

    [Fact]
    public void BuildFloatingObjectSnapshots_CollectsEveryFloatingKindAndWrapZones()
    {
        var surface = DocumentViewLayoutPlanner.BuildSurfacePlan(
            new PageSettings(),
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 816);
        var paragraph = BuildAllFloatingKindsParagraph();

        var snapshots = DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
            paragraph,
            blockIndex: 2,
            anchorContentYDip: 100,
            surface,
            columnCount: 1);

        snapshots.Select(snapshot => snapshot.Kind).Should().Equal(
            DocumentFloatingObjectKind.Image,
            DocumentFloatingObjectKind.Shape,
            DocumentFloatingObjectKind.Chart,
            DocumentFloatingObjectKind.WordArt,
            DocumentFloatingObjectKind.SmartArt,
            DocumentFloatingObjectKind.Group);
        snapshots.Should().OnlyContain(snapshot => snapshot.BlockIndex == 2);

        var image = snapshots[0];
        image.Rect.Should().Be(new DocumentFloatRect(144, 232, 96, 48));
        image.ZOrderIndex.Should().Be(3);
        image.BehindText.Should().BeFalse();
        image.TypeTag.Should().Be("Image");

        snapshots[1].BehindText.Should().BeTrue();
        snapshots[3].Rect.Should().Be(new DocumentFloatRect(216, 268, 200, 80),
            "authored WordArt bounds must override the text-length fallback");
        snapshots[4].TypeTag.Should().Be("SmartArt");

        var wrapZones = DocumentViewLayoutPlanner.BuildFloatingWrapExclusionZones(snapshots);
        wrapZones.Select(zone => zone.Wrapping).Should().Equal(
            ImageWrapping.Square,
            ImageWrapping.Tight,
            ImageWrapping.TopAndBottom,
            ImageWrapping.Square);
    }

    [Fact]
    public void BuildFloatingWrapReservation_UsesFloatingObjectDimensionsOnlyForWrappingModes()
    {
        var square = Run.FromImage(new InlineImage([], widthPt: 72, heightPt: 54)
        {
            Wrapping = ImageWrapping.Square,
        });
        var topAndBottom = Run.FromImage(new InlineImage([], widthPt: 90, heightPt: 45)
        {
            Wrapping = ImageWrapping.TopAndBottom,
        });
        var behind = Run.FromImage(new InlineImage([], widthPt: 72, heightPt: 54)
        {
            Wrapping = ImageWrapping.Behind,
        });

        var squarePlan = DocumentViewLayoutPlanner.BuildFloatingWrapReservation(square);
        var topAndBottomPlan = DocumentViewLayoutPlanner.BuildFloatingWrapReservation(topAndBottom);

        squarePlan.Should().Be(new DocumentFloatingWrapReservationPlan(
            DocumentFloatingObjectKind.Image,
            WidthDip: 96,
            HeightDip: 72,
            ImageWrapping.Square));
        topAndBottomPlan.Should().Be(new DocumentFloatingWrapReservationPlan(
            DocumentFloatingObjectKind.Image,
            WidthDip: 120,
            HeightDip: 60,
            ImageWrapping.TopAndBottom));
        DocumentViewLayoutPlanner.BuildFloatingWrapReservation(topAndBottom, topAndBottomReservationWidthDip: 624)
            .Should().Be(new DocumentFloatingWrapReservationPlan(
                DocumentFloatingObjectKind.Image,
                WidthDip: 624,
                HeightDip: 60,
                ImageWrapping.TopAndBottom));
        DocumentViewLayoutPlanner.BuildFloatingWrapReservation(behind).Should().BeNull();
    }

    [Fact]
    public void BuildFloatingObjectDrawOrder_OrdersMergedKindsInsideEachBand()
    {
        var surface = DocumentViewLayoutPlanner.BuildSurfacePlan(
            new PageSettings(),
            DocumentViewLayoutKind.PrintLayout,
            availableWidthDip: 816);
        var snapshots = DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
            BuildAllFloatingKindsParagraph(),
            blockIndex: 0,
            anchorContentYDip: 0,
            surface,
            columnCount: 1);

        DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: true)
            .Select(snapshot => (snapshot.ZOrderIndex, snapshot.TypeTag))
            .Should().Equal((1, "Shape"));

        DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: false)
            .Select(snapshot => (snapshot.ZOrderIndex, snapshot.TypeTag))
            .Should().Equal(
                (3, "Image"),
                (5, "WordArt"),
                (7, "SmartArt"),
                (9, "Chart"),
                (11, "Group"));
    }

    [Fact]
    public void BuildFloatingObjectDrawOrder_InterleavesShapeAndImageByZOrder()
    {
        var snapshots = new[]
        {
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Image,
                0,
                0,
                new DocumentFloatRect(0, 0, 80, 40),
                BehindText: true,
                ZOrderIndex: 8,
                ImageWrapping.Behind),
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape,
                0,
                1,
                new DocumentFloatRect(8, 8, 80, 40),
                BehindText: true,
                ZOrderIndex: 3,
                ImageWrapping.Behind),
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape,
                0,
                2,
                new DocumentFloatRect(16, 16, 80, 40),
                BehindText: false,
                ZOrderIndex: 1,
                ImageWrapping.InFront),
        };

        DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: true)
            .Select(snapshot => (snapshot.Kind, snapshot.ZOrderIndex))
            .Should().Equal(
                (DocumentFloatingObjectKind.Shape, 3),
                (DocumentFloatingObjectKind.Image, 8));

        DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(snapshots, behindText: false)
            .Select(snapshot => (snapshot.Kind, snapshot.ZOrderIndex))
            .Should().Equal((DocumentFloatingObjectKind.Shape, 1));
    }

    [Fact]
    public void HitTestFloatingObject_PrefersFrontBandThenHighestZOrder()
    {
        var rect = new DocumentFloatRect(10, 20, 100, 80);
        var snapshots = new[]
        {
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape, 0, 0, rect, BehindText: true, ZOrderIndex: 100, ImageWrapping.Behind),
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Image, 0, 1, rect, BehindText: false, ZOrderIndex: 2, ImageWrapping.Square),
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Chart, 0, 2, rect, BehindText: false, ZOrderIndex: 8, ImageWrapping.InFront),
        };

        var hit = DocumentViewLayoutPlanner.HitTestFloatingObject(
            snapshots,
            new DocumentFloatPoint(40, 50));

        hit.Should().NotBeNull();
        hit!.Kind.Should().Be(DocumentFloatingObjectKind.Chart);
        hit.ZOrderIndex.Should().Be(8);

        DocumentViewLayoutPlanner.HitTestFloatingObject(
            snapshots,
            new DocumentFloatPoint(200, 50))
            .Should().BeNull();
    }

    // FB2: HitTestFloatingObject must un-rotate the pointer into each candidate's own frame before the
    // containment test, so a rotated float's hit region tracks its VISIBLE (rotated) footprint rather
    // than its axis-aligned bounding box: clicking the visible rotated shape (which reaches outside the
    // axis-aligned box, near the middle of a rotated edge) hits; clicking an empty axis-aligned box
    // corner (which the 45° rotation moved the shape away from) misses.
    [Fact]
    public void HitTestFloatingObject_Rotated45_MatchesVisibleFootprintNotAxisAlignedBox()
    {
        // A 100x100 square rotated 45° about its centre (50,50) becomes a diamond whose vertices reach
        // out to (50, 50±70.71) and (50±70.71, 50) — i.e. straight above/below/left/right of centre,
        // ~20.7 past the axis-aligned box edge (50,0)/(50,100)/(0,50)/(100,50).
        var rect = new DocumentFloatRect(0, 0, 100, 100);
        var snapshots = new[]
        {
            new DocumentFloatingObjectSnapshot(
                DocumentFloatingObjectKind.Shape, 0, 0, rect, BehindText: false, ZOrderIndex: 1,
                ImageWrapping.Square, RotationAngle: 45),
        };

        // 1) The axis-aligned box corner (5, 5) is empty space once the shape is rotated 45° about its
        //    centre (the corner rotated away from that spot) -> must NOT hit.
        DocumentViewLayoutPlanner.HitTestFloatingObject(snapshots, new DocumentFloatPoint(5, 5))
            .Should().BeNull("the un-rotated box corner is empty space once the shape is rotated 45°");

        // 2) The shape's own centre (50, 50) is invariant under rotation about itself -> must hit.
        DocumentViewLayoutPlanner.HitTestFloatingObject(snapshots, new DocumentFloatPoint(50, 50))
            .Should().NotBeNull("the centre point is unaffected by rotation about itself");

        // 3) The SCREEN point that the +45° render rotates onto local (85, 85) (a point safely inside the
        //    un-rotated square) must still hit the rotated shape — proves the un-rotate in the hit-test
        //    is the true inverse of the render transform, not an approximation.
        var screenForLocal85 = InverseRotate(new DocumentFloatPoint(85, 85), rect, 45);
        DocumentViewLayoutPlanner.HitTestFloatingObject(snapshots, screenForLocal85)
            .Should().NotBeNull("the screen point that rotates onto local (85,85) must hit the rotated shape");
    }

    /// <summary>Test helper: the SCREEN point that rotates onto <paramref name="local"/> by +<paramref name="angle"/>°
    /// about <paramref name="rect"/>'s centre (the forward transform DrawFloatingShape applies) — the exact inverse of
    /// <see cref="DocumentViewLayoutPlanner.UnTransformPoint"/>, used here to build test fixtures rather than to test
    /// production logic directly.</summary>
    private static DocumentFloatPoint InverseRotate(DocumentFloatPoint local, DocumentFloatRect rect, double angle)
    {
        var cx = rect.CenterXDip;
        var cy = rect.CenterYDip;
        var ax = local.XDip - cx;
        var ay = local.YDip - cy;
        var rad = angle * System.Math.PI / 180.0;
        var cos = System.Math.Cos(rad);
        var sin = System.Math.Sin(rad);
        var sx = ax * cos - ay * sin;
        var sy = ax * sin + ay * cos;
        return new DocumentFloatPoint(sx + cx, sy + cy);
    }

    [Fact]
    public void BuildFloatingGroupChildSnapshots_ResolvesChildKindsAndOffsets()
    {
        var group = new DrawingGroup();
        group.Children.Add(new InlineImage([1], widthPt: 36, heightPt: 18));
        group.Children.Add(new Shape { Kind = ShapeKind.Ellipse, WidthPt = 24, HeightPt = 12 });
        group.Children.Add(Chart.Create(ChartKind.Column, ["A"], [4]));
        group.Children.Add(WordArt.Create("Arc", fontSizePt: 20));
        group.Children.Add(SmartArt.Create(SmartArtKind.Process, ["Plan", "Ship"]));
        group.ChildOffsets.Add((0, 0));
        group.ChildOffsets.Add((9, 6));
        group.ChildOffsets.Add((18, 12));
        group.ChildOffsets.Add((27, 18));
        group.ChildOffsets.Add((36, 24));

        var snapshots = DocumentViewLayoutPlanner.BuildFloatingGroupChildSnapshots(
            group,
            new DocumentFloatRect(100, 200, 300, 400));

        snapshots.Select(snapshot => snapshot.Kind).Should().Equal(
            DocumentFloatingObjectKind.Image,
            DocumentFloatingObjectKind.Shape,
            DocumentFloatingObjectKind.Chart,
            DocumentFloatingObjectKind.WordArt,
            DocumentFloatingObjectKind.SmartArt);
        snapshots[1].Rect.Should().Be(new DocumentFloatRect(112, 208, 32, 16));
        snapshots[4].Rect.XDip.Should().BeApproximately(148, 0.01);
        snapshots[4].Rect.YDip.Should().BeApproximately(232, 0.01);
    }

    [Fact]
    public void GroupChildTransformHelpers_MapHandlesAndGesturesThroughParentTransform()
    {
        var groupRect = new DocumentFloatRect(100, 80, 240, 160);
        var childRect = new DocumentFloatRect(160, 116, 96, 48);
        var visibleHandles = DocumentViewLayoutPlanner.BuildFloatingGroupChildHandleRects(
            groupRect,
            childRect,
            handleSizeDip: 8,
            childRotationAngle: 30,
            childFlipH: true,
            groupRotationAngle: 90,
            groupFlipV: true);
        var targetHandle = visibleHandles.Single(handle =>
            handle.Handle == DocumentFloatingHandle.BottomRight);

        DocumentViewLayoutPlanner.HitTestFloatingGroupChildHandle(
                groupRect,
                childRect,
                new DocumentFloatPoint(targetHandle.Rect.CenterXDip, targetHandle.Rect.CenterYDip),
                handleSizeDip: 8,
                hitPaddingDip: 1,
                childRotationAngle: 30,
                childFlipH: true,
                groupRotationAngle: 90,
                groupFlipV: true)
            .Should().Be(DocumentFloatingHandle.BottomRight);

        var moved = DocumentViewLayoutPlanner.BuildFloatingGroupChildMoveRect(
            groupRect,
            childRect,
            new DocumentFloatPoint(188, 140),
            new DocumentFloatPoint(212, 152),
            groupRotationAngle: 90,
            groupFlipV: true);
        moved.XDip.Should().NotBeApproximately(childRect.XDip + 24, 0.01);
        moved.YDip.Should().NotBeApproximately(childRect.YDip + 12, 0.01);

        var resized = DocumentViewLayoutPlanner.BuildFloatingGroupChildResizeRect(
            groupRect,
            childRect,
            DocumentFloatingHandle.BottomRight,
            new DocumentFloatPoint(targetHandle.Rect.CenterXDip + 24, targetHandle.Rect.CenterYDip + 12),
            preserveAspect: false,
            minimumSizeDip: 8,
            childRotationAngle: 30,
            childFlipH: true,
            groupRotationAngle: 90,
            groupFlipV: true);
        resized.WidthDip.Should().BeGreaterThan(0);
        resized.HeightDip.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GroupChildBodyHit_UsesForwardRenderedPolygon()
    {
        var groupRect = new DocumentFloatRect(472, 400, 280, 173.333333);
        var childRect = new DocumentFloatRect(618.666667, 473.333333, 86.666667, 46.666667);
        var visibleCenter = DocumentViewLayoutPlanner.TransformPoint(
            new DocumentFloatPoint(childRect.CenterXDip, childRect.CenterYDip),
            groupRect,
            25,
            flipH: true,
            flipV: false);

        DocumentViewLayoutPlanner.ContainsFloatingGroupChildPoint(
            groupRect,
            childRect,
            visibleCenter,
            childRotationAngle: 15,
            childFlipV: true,
            groupRotationAngle: 25,
            groupFlipH: true).Should().BeTrue();
    }

    [Fact]
    public void NestedGroupChildTransformHelpersComposeInnerAndOuterTransforms()
    {
        var outerRect = new DocumentFloatRect(140, 90, 260, 150);
        var innerRect = new DocumentFloatRect(196, 126, 128, 72);
        var leafRect = new DocumentFloatRect(238, 154, 64, 32);
        var parents = new DocumentFloatTransform[]
        {
            new(innerRect, RotationAngle: -18, FlipV: true),
            new(outerRect, RotationAngle: 27, FlipH: true)
        };

        var visibleCenter = DocumentViewLayoutPlanner.TransformPointThroughGroupChain(
            new DocumentFloatPoint(leafRect.CenterXDip, leafRect.CenterYDip),
            leafRect,
            childRotationAngle: 13,
            childFlipH: true,
            childFlipV: false,
            parents);

        DocumentViewLayoutPlanner.ContainsFloatingGroupChildPointThroughGroupChain(
            leafRect,
            visibleCenter,
            childRotationAngle: 13,
            childFlipH: true,
            childFlipV: false,
            parents).Should().BeTrue();

        var localCenter = DocumentViewLayoutPlanner.UnTransformPointThroughGroupChain(
            visibleCenter,
            leafRect,
            childRotationAngle: 13,
            childFlipH: true,
            childFlipV: false,
            parents);
        localCenter.XDip.Should().BeApproximately(leafRect.CenterXDip, 0.001);
        localCenter.YDip.Should().BeApproximately(leafRect.CenterYDip, 0.001);

        var handles = DocumentViewLayoutPlanner.BuildFloatingGroupChildHandleRectsThroughGroupChain(
            leafRect,
            handleSizeDip: 8,
            childRotationAngle: 13,
            childFlipH: true,
            childFlipV: false,
            parents);
        var bottomRight = handles.Single(handle =>
            handle.Handle == DocumentFloatingHandle.BottomRight);
        DocumentViewLayoutPlanner.HitTestFloatingGroupChildHandleThroughGroupChain(
            leafRect,
            new DocumentFloatPoint(bottomRight.Rect.CenterXDip, bottomRight.Rect.CenterYDip),
            handleSizeDip: 8,
            hitPaddingDip: 1,
            childRotationAngle: 13,
            childFlipH: true,
            childFlipV: false,
            parents).Should().Be(DocumentFloatingHandle.BottomRight);

        var moved = DocumentViewLayoutPlanner.BuildFloatingGroupChildMoveRectThroughGroupChain(
            leafRect,
            visibleCenter,
            new DocumentFloatPoint(visibleCenter.XDip + 22, visibleCenter.YDip - 14),
            parents);
        moved.XDip.Should().NotBeApproximately(leafRect.XDip + 22, 0.01);
        moved.YDip.Should().NotBeApproximately(leafRect.YDip - 14, 0.01);
    }

    private static Paragraph BuildAllFloatingKindsParagraph()
    {
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(string.Empty)
        {
            Image = new InlineImage([1], widthPt: 72, heightPt: 36)
            {
                Wrapping = ImageWrapping.Square,
                HorizontalOffsetPt = 18,
                VerticalOffsetPt = 9,
                ZOrderIndex = 3,
            }
        });
        paragraph.Runs.Add(Run.FromShape(new Shape
        {
            WidthPt = 90,
            HeightPt = 45,
            Placement = Floating(ImageWrapping.Behind, zOrder: 1, horizontalOffsetPt: 36, verticalOffsetPt: 18),
        }));
        paragraph.Runs.Add(Run.FromChart(new Chart
        {
            Kind = ChartKind.Column,
            WidthPt = 180,
            HeightPt = 90,
            Placement = Floating(ImageWrapping.InFront, zOrder: 9, horizontalOffsetPt: 54, verticalOffsetPt: 27),
        }));
        paragraph.Runs.Add(Run.FromWordArt(new WordArt("Go", fontSizePt: 24)
        {
            WidthPt = 150,
            HeightPt = 60,
            Placement = Floating(ImageWrapping.Tight, zOrder: 5, horizontalOffsetPt: 72, verticalOffsetPt: 36),
        }));
        paragraph.Runs.Add(Run.FromSmartArt(new SmartArt
        {
            Kind = SmartArtKind.Process,
            WidthPt = 200,
            HeightPt = 100,
            Placement = Floating(ImageWrapping.TopAndBottom, zOrder: 7, horizontalOffsetPt: 90, verticalOffsetPt: 45),
        }));
        paragraph.Runs.Add(Run.FromDrawingGroup(new DrawingGroup
        {
            WidthPt = 144,
            HeightPt = 72,
            Placement = Floating(ImageWrapping.Square, zOrder: 11, horizontalOffsetPt: 108, verticalOffsetPt: 54),
        }));
        return paragraph;
    }

    private static FloatingPlacement Floating(
        ImageWrapping wrapping,
        int zOrder,
        double horizontalOffsetPt,
        double verticalOffsetPt) =>
        new()
        {
            Wrapping = wrapping,
            ZOrderIndex = zOrder,
            HorizontalOffsetPt = horizontalOffsetPt,
            VerticalOffsetPt = verticalOffsetPt,
        };
}

public sealed class DocumentViewLayoutPlannerSourceGuardTests
{
    [Fact]
    public void PlatformDocumentViews_DelegatePageColumnAndFloatingPlanningToPresentationPlanner()
    {
        var hostSource = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaSource = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        hostSource.Should().Contain("using FreeW.App.Presentation.DocumentView;");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildPageMetrics(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildColumnPlan(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingOverlaySurfacePlan(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingWrapReservation(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingWrapReservationTextWidthDip(");
        hostSource.Should().Contain("DocumentViewLayoutPlanner.BuildDropCapLayoutPlan(");
        hostSource.Should().NotContain("Formatting.FontSizePt ?? 0) >= DropCap.DefaultSizePt");
        hostSource.Should().Contain("DrawingObjectVisualPlanner.BuildVisualPlan(");
        hostSource.Should().Contain("BuildGroupPlannedChildVisual(");
        hostSource.Should().Contain("DrawingObjectVisualKind.Image when child is InlineImage image");
        hostSource.Should().Contain("BuildFloatingImageVisual(image, plan.Rect, enableSelection: false)");
        hostSource.Should().Contain("DrawingObjectVisualKind.Chart when child is Chart chart");
        hostSource.Should().Contain("BuildFloatingChartVisual(chart, plan.Rect, enableSelection: false)");
        hostSource.Should().Contain("DrawingObjectVisualKind.SmartArt when child is SmartArt smartArt");
        hostSource.Should().Contain("BuildFloatingSmartArtVisual(smartArt, plan.Rect, enableSelection: false)");

        avaloniaSource.Should().Contain("using FreeW.App.Presentation.DocumentView;");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildSurfacePlan(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildColumnPlan(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingWrapExclusionZones(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.HitTestFloatingObject(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingGroupChildSnapshots(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingTextWrapLinePlan(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildDropCapLayoutPlan(");
        avaloniaSource.Should().Contain("_dropCapLayoutPlans.Add(dropCapPlan)");
        avaloniaSource.Should().NotContain("DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(");
        avaloniaSource.Should().NotContain("DocumentViewLayoutPlanner.BuildTopAndBottomWrapExclusionBottom(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingHandleRects(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildFloatingResizeRect(");
        avaloniaSource.Should().Contain("BuildGridlines(");
        avaloniaSource.Should().Contain("DocumentViewLayoutPlanner.BuildRulerTicks(");
        avaloniaSource.Should().Contain("DrawingObjectVisualPlanner.BuildVisualPlan(");
    }

    [Fact]
    public void PlatformDocumentViews_UseSharedTableCellEffectiveFillPlans()
    {
        var hostSource = ReadSource("freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaSource = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        hostSource.Should().Contain("cell => cell.EffectiveFill");
        hostSource.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        hostSource.Should().NotContain("ResolveCellStyle(");

        avaloniaSource.Should().Contain("cell => cell.EffectiveFill");
        avaloniaSource.Should().Contain("DocumentTableCellEffectiveFillPlan.Empty");
        avaloniaSource.Should().NotContain("ResolveCellStyle(");
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

    [Fact]
    public void AvaloniaDocumentView_DoesNotReownFloatingObjectPlanning()
    {
        var source = ReadSource("freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        source.Should().NotContain("DocumentViewLayoutPlanner.BuildFloatingObjectPlacement(");
        source.Should().NotContain("private void AddWrapExclusion(");
        source.Should().NotContain("private void CollectFloatingImages(");
        source.Should().NotContain("private void CollectFloatingShapes(");
        source.Should().NotContain("private void CollectFloatingCharts(");
        source.Should().NotContain("private void CollectFloatingWordArts(");
        source.Should().NotContain("private void CollectFloatingSmartArts(");
        source.Should().NotContain("private void CollectFloatingGroups(");
        source.Should().NotContain("private (double X, double Y) ResolveFloatingPos(");
        source.Should().NotContain("var candidates = new List<(bool BehindText, int ZOrder");
        source.Should().NotContain("var behindDraws = new List<(int ZOrder, Action Draw)>");
        source.Should().NotContain("var frontDraws = new List<(int ZOrder, Action Draw)>");
    }

    private static string ReadSource(params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 1];
        parts[0] = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        Array.Copy(relativeParts, 0, parts, 1, relativeParts.Length);
        return File.ReadAllText(Path.Combine(parts));
    }

}
