using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
    [Fact]
    public void DrawShapesKeyTip_OpensShapeMenuAndInsertsRectangle()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(3, 2, 3, 2);

            harness.OpenRibbonMenu(Key.J, Key.S, Key.H);

            harness.SelectedRibbonTabHeader.Should().Be("Draw");
            harness.KeyTipScope.Should().Be("Menu");
            harness.ActiveMenuItemGestureText("Lines").Should().Be("1");
            harness.ActiveMenuItemGestureText("Rectangles").Should().Be("2");
            harness.ActiveMenuItemGestureText("Basic Shapes").Should().Be("3");
            harness.ActiveMenuItemGestureText("Block Arrows").Should().Be("4");
            harness.ActiveMenuItemGestureText("Equation Shapes").Should().Be("5");
            harness.ActiveMenuItemGestureText("Flowchart").Should().Be("6");
            harness.ActiveMenuItemGestureText("Stars and Banners").Should().Be("7");
            harness.ActiveMenuItemGestureText("Callouts").Should().Be("8");

            harness.HandleKeyTip(Key.D2);
            harness.ActiveMenuItemGestureText("Rectangle").Should().Be("R");
            harness.ActiveMenuItemGestureText("Rounded Rectangle").Should().Be("O");
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
    public void InsertChartsKeyTip_DoesNotSurfaceDeferredMapChart()
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            // Charts is a primary Insert group and must remain reachable at normal narrow widths.
            harness.SelectRibbonTab("Insert", 900);

            harness.RibbonGroupIsCollapsed("Charts").Should().BeTrue(
                "the wide Charts group may compact at normal narrow widths, but it must remain visible");
            harness.CollapsedRibbonGroupOverflowWidth("Charts").Should().BeGreaterThan(0,
                "the Charts overflow button must paint so chart commands are reachable");
            harness.CollapsedRibbonGroupOverflowKeyTip("Charts").Should().Be("CH");

            // The visible chart surface exposes implemented chart commands only.
            var commands = harness.CollapsedRibbonGroupOverflowMenuKeyTips("Charts");
            commands.Should().ContainKey("Recommended Charts").WhoseValue.Should().Be("RC");
            commands.Should().ContainKey("Column Chart").WhoseValue.Should().Be("CC");
            commands.Should().NotContainKey("Map Chart",
                "the deferred Map Chart command must not be exposed by the Insert Charts surface");
        });
    }

    [Theory]
    [InlineData(Key.D1, Key.E, DrawingShapeKind.ElbowConnector)]
    [InlineData(Key.D3, Key.O, DrawingShapeKind.Ellipse)]
    [InlineData(Key.D4, Key.R, DrawingShapeKind.RightArrow)]
    [InlineData(Key.D5, Key.N, DrawingShapeKind.NotEqualSign)]
    [InlineData(Key.D6, Key.D, DrawingShapeKind.FlowchartDecision)]
    [InlineData(Key.D7, Key.D5, DrawingShapeKind.Star5)]
    [InlineData(Key.D8, Key.V, DrawingShapeKind.OvalCallout)]
    public void DrawShapesMenuKeyTips_InsertVisibleDrawingCommands(
        Key groupKeyTip,
        Key shapeKeyTip,
        DrawingShapeKind expectedKind)
    {
        RunSta(() =>
        {
            using var harness = MainWindowHarness.Create();
            harness.SelectRange(4, 3, 4, 3);

            harness.OpenRibbonMenu(Key.J, Key.S, Key.H);
            harness.HandleKeyTip(groupKeyTip);
            harness.HandleKeyTip(shapeKeyTip);

            harness.KeyTipScope.Should().Be("None");
            harness.DrawingShapeCount.Should().Be(1);
            harness.LastDrawingShapeKind.Should().Be(expectedKind);
            harness.LastDrawingShapeAnchor.Should().Be((4u, 3u));
        });
    }
}
