using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class MainWindowRibbonKeyTipTests
{
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
}
