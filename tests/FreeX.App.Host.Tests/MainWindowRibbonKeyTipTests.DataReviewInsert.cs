using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void DataWhatIfKeyTip_OpensAnalysisMenuWithExcelChoices()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();

            harness.OpenRibbonMenu(Key.A, Key.W);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Goal Seek...").Should().Be("G");
            harness.ActiveMenuItemGestureText("Scenario Manager...").Should().Be("S");
            harness.ActiveMenuItemGestureText("Data Table...").Should().Be("D");
        });
    }

    [Fact]
    public void DataOutlineKeyTips_GroupAndUngroupSelectedRows()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(2, 1, 4, 1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.G);

            harness.SelectedRibbonTabHeader.Should().Be("Data");
            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(1);
            harness.RowOutlineLevel(3).Should().Be(1);
            harness.RowOutlineLevel(4).Should().Be(1);

            harness.HandleDirectTopLevelKeyTip(Key.A).Should().BeTrue();
            harness.HandleKeyTip(Key.U);

            harness.KeyTipScope.Should().Be("None");
            harness.RowOutlineLevel(2).Should().Be(0);
            harness.RowOutlineLevel(3).Should().Be(0);
            harness.RowOutlineLevel(4).Should().Be(0);
        });
    }

    [Fact]
    public void ReviewNoteAndCommentNavigationKeyTips_RouteSplitReviewLanes()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.AddNote(2, 2, "Plain note");
            harness.AddThreadedComment(4, 4, "Threaded note");
            harness.SelectRange(1, 1, 1, 1);

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u), "Next Note should cycle simple notes without crossing into threaded comments");
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.J);
            harness.KeyTipScope.Should().Be("Commands", "J is the shared Review prefix before Next Comment resolves");
            harness.HandleKeyTip(Key.C);

            harness.SelectedCellAddress.Should().Be((4u, 4u));
            harness.KeyTipScope.Should().Be("None");

            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.P);
            harness.KeyTipScope.Should().Be("Commands", "P is a shared Review prefix before Previous Note resolves");
            harness.HandleKeyTip(Key.N);

            harness.SelectedCellAddress.Should().Be((2u, 2u));
            harness.KeyTipScope.Should().Be("None");
        });
    }

    [Fact]
    public void ReviewAllowEditRangesKeyTip_IsDisabledWhenSheetIsProtected()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(workbook =>
            {
                workbook.Sheets[0].IsProtected = true;
            });

            harness.RefreshSheetProtectionUi();

            harness.NamedButtonIsEnabled("AllowEditRangesButton").Should().BeFalse();
            harness.HandleDirectTopLevelKeyTip(Key.R).Should().BeTrue();
            harness.HandleKeyTip(Key.A);

            harness.KeyTipScope.Should().Be("None", "disabled Review commands should not stay routable through keytips");
            harness.StartScreenIsVisible.Should().BeFalse("Alt,R,A,R must not open the Allow Edit Ranges workflow on a protected sheet");
        });
    }

    [Fact]
    public void InsertShapesKeyTip_OpensShapeMenuAndInsertsRectangle()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(3, 2, 3, 2);

            harness.OpenRibbonMenu(Key.N, Key.S, Key.H);

            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Rectangle").Should().Be("R");
            harness.ActiveMenuItemGestureText("Ellipse").Should().Be("E");
            harness.ActiveMenuItemGestureText("Line").Should().Be("L");

            harness.HandleKeyTip(Key.R);

            harness.KeyTipScope.Should().Be("None");
            harness.DrawingShapeCount.Should().Be(1);
            harness.LastDrawingShapeKind.Should().Be(DrawingShapeKind.Rectangle);
            harness.LastDrawingShapeAnchor.Should().Be((3u, 2u));
        });
    }

    [Fact]
    public void InsertChartKeyTip_InsertsRenderableChartFromVisibleRibbonCommand()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SetNumber(1, 1, 10);
            harness.SetNumber(1, 2, 20);
            harness.SetNumber(2, 1, 30);
            harness.SetNumber(2, 2, 40);
            harness.SelectRange(1, 1, 2, 2);

            harness.HandleDirectTopLevelKeyTip(Key.N).Should().BeTrue();
            harness.SelectedRibbonTabHeader.Should().Be("Insert");
            harness.VisibleCommandKeyTips("CC").Should().ContainSingle("Column Chart");

            harness.HandleKeyTip(Key.C);
            harness.KeyTipScope.Should().Be("Commands", "C is a shared Insert command prefix before CC resolves");
            harness.HandleKeyTip(Key.C);

            harness.KeyTipScope.Should().Be("None");
            harness.ChartCount.Should().Be(1);
            harness.LastChartType.Should().Be(ChartType.Column);
        });
    }

    [Fact]
    public void CollapsedInsertChartsKeyTip_DoesNotSurfaceDeferredMapChart()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRibbonTab("Insert", 800);

            harness.OpenRibbonMenu(Key.N, Key.C, Key.H);
            harness.ActiveMenuItemGestureText("Column Chart").Should().Be("CC");
            harness.ActiveMenuItemGestureText("Map Chart").Should().BeNull();
        });
    }

    [Theory]
    [InlineData(Key.E, DrawingShapeKind.Ellipse)]
    [InlineData(Key.L, DrawingShapeKind.Line)]
    public void InsertShapesMenuKeyTips_InsertVisibleDrawingCommands(Key shapeKeyTip, DrawingShapeKind expectedKind)
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(4, 3, 4, 3);

            harness.OpenRibbonMenu(Key.N, Key.S, Key.H);
            harness.HandleKeyTip(shapeKeyTip);

            harness.KeyTipScope.Should().Be("None");
            harness.DrawingShapeCount.Should().Be(1);
            harness.LastDrawingShapeKind.Should().Be(expectedKind);
            harness.LastDrawingShapeAnchor.Should().Be((4u, 3u));
        });
    }

    [Fact]
    public void PivotContextualTabs_AppearDisappearWithPivotSelectionAndExposeJaJdKeyTips()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create(ConfigureWorkbookWithPivotTable);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();
            harness.PivotFieldListPaneIsVisible.Should().BeFalse();

            harness.SelectRange(6, 5, 6, 5);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeTrue();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeTrue();
            harness.PivotFieldListPaneIsVisible.Should().BeTrue();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().Contain(["JA", "JD"]);
            harness.HandleKeyTip(Key.J);
            harness.HandleKeyTip(Key.A);

            harness.SelectedRibbonTabHeader.Should().Be("PivotTable Analyze");
            harness.KeyTipScope.Should().Be("Commands");
            harness.VisibleCommandKeyTips("R").Should().ContainSingle("Refresh");

            harness.SelectRange(20, 1, 20, 1);
            harness.RefreshViewport();

            harness.ContextualTabIsVisible("PivotTableAnalyzeTab").Should().BeFalse();
            harness.ContextualTabIsVisible("PivotTableDesignTab").Should().BeFalse();
            harness.PivotFieldListPaneIsVisible.Should().BeFalse();

            harness.EnterKeyTipScope("TopLevel");
            harness.OverlayBadgeTexts.Should().NotContain(["JA", "JD"]);
        });
    }

}
