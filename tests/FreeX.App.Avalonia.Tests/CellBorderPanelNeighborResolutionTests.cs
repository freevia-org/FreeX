using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Headless-Avalonia render tests for <see cref="CellBorderPanel"/>'s adjacent-edge conflict
/// resolution (R66-render-gridlines-borders-6-1). Before this fix, <c>CellBorderPanel</c> drew
/// each cell's own <see cref="CellStyle"/> border edges unconditionally with no awareness of the
/// touching neighbor cell's border on the same shared physical edge, so a bordered cell next to
/// another bordered cell double-drew that edge with a paint-order-dependent winner instead of
/// Excel's deterministic "heavier style wins" rule (already implemented, but uncalled, in
/// <see cref="FreeX.App.Presentation.Rendering.CellBorderVisualPlanner.ResolveEdgeWinner"/>).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class CellBorderPanelNeighborResolutionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static CellStyle StyleWithBottomBorder(BorderStyle style, CellColor color)
    {
        var cellStyle = new CellStyle();
        cellStyle.BorderBottom = new CellBorder(style, color);
        return cellStyle;
    }

    [Fact]
    public async Task SharedEdge_HeavierNeighborWins_NotOwnLighterStyle()
    {
        // Failure scenario (finding R66-render-gridlines-borders-6-1): B1 has a Thin black bottom
        // border; B2 (directly below B1) has a Thick red top border on the exact same shared grid
        // edge. Pre-fix, B1's CellBorderPanel drew its own Thin black line with zero awareness of
        // B2's heavier Thick red border on that edge -- a paint-order-dependent double-draw instead
        // of Excel's single heavier-wins edge.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thin, CellColor.Black);
            var neighborTopFromBelow = new CellBorder(BorderStyle.Thick, new CellColor(0xFF, 0, 0));
            var neighbors = new CellBorderNeighborEdges(Below: neighborTopFromBelow);

            var panel = new CellBorderPanel(style, neighbors);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1,
                "only the resolved winner edge should be drawn -- not the panel's own weaker style and not two overlapping lines");

            var line = lines[0];
            line.StrokeThickness.Should().BeApproximately(3, 0.001,
                "the resolved edge must use the neighbor's heavier Thick thickness, not this cell's own Thin thickness");
            var stroke = line.Stroke.Should().BeOfType<SolidColorBrush>().Subject;
            stroke.Color.Should().Be(Color.FromRgb(0xFF, 0, 0),
                "the resolved edge must use the neighbor's red color -- the heavier style wins entirely, not just its thickness");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NoNeighborBorder_DrawsOwnEdgeUnchanged()
    {
        // Sibling no-regression case: a cell with no neighbor border info (the default
        // CellBorderNeighborEdges, e.g. a cell on the grid's edge or whose neighbor has no border)
        // must keep drawing its own edge exactly as before this fix.
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thin, CellColor.Black);

            var panel = new CellBorderPanel(style);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1);

            var line = lines[0];
            line.StrokeThickness.Should().BeApproximately(1, 0.001,
                "with no neighbor border, this cell's own Thin thickness must be unaffected");
            var stroke = line.Stroke.Should().BeOfType<SolidColorBrush>().Subject;
            stroke.Color.Should().Be(Colors.Black);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SharedEdge_OwnStyleHeavier_NeighborLighterStyleSuppressed()
    {
        // Complementary direction: this cell's OWN edge is the heavier one, so the neighbor's
        // lighter style must be fully suppressed (not blended, not drawn as a second line).
        await Session.Dispatch(() =>
        {
            var style = StyleWithBottomBorder(BorderStyle.Thick, new CellColor(0xFF, 0, 0));
            var neighborTopFromBelow = new CellBorder(BorderStyle.Thin, CellColor.Black);
            var neighbors = new CellBorderNeighborEdges(Below: neighborTopFromBelow);

            var panel = new CellBorderPanel(style, neighbors);
            panel.Measure(new Size(60, 20));
            panel.Arrange(new Rect(0, 0, 60, 20));

            var lines = panel.Children.OfType<Line>().ToList();
            lines.Should().HaveCount(1);
            lines[0].StrokeThickness.Should().BeApproximately(3, 0.001);
            var stroke = lines[0].Stroke.Should().BeOfType<SolidColorBrush>().Subject;
            stroke.Color.Should().Be(Color.FromRgb(0xFF, 0, 0));
        }, CancellationToken.None);
    }
}
