using System.Windows;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;

namespace FreeW.App.Host.Tests;

public sealed class RulerInteractionTests
{
    [Fact]
    public void TryMetrics_MapsContentPointsToHorizontalRulerCoordinates()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginLeftPt = 72,
            MarginRightPt = 72
        };

        var metrics = Ruler.TryMetrics(new Size(1000, 16), page, zoom: 1)!;

        metrics.ContentStart.Should().BeApproximately(188, 0.1);
        metrics.ContentEnd.Should().BeApproximately(812, 0.1);
        metrics.ContentPtToX(72).Should().BeApproximately(284, 0.1);
        metrics.PointToContentPt(metrics.ContentPtToX(144)).Should().BeApproximately(144, 0.1);
    }

    [Fact]
    public void MoveOrAddLeftTabStop_AddsSnappedSortedStop()
    {
        var stops = Ruler.MoveOrAddLeftTabStop(
            [new TabStop(144, TabStopAlignment.Right, TabLeader.Dots)],
            index: -1,
            positionPt: 71);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(144, TabStopAlignment.Right, TabLeader.Dots));
    }

    [Fact]
    public void MoveOrAddLeftTabStop_MovesExistingStop_AndPreservesAlignmentLeader()
    {
        var stops = Ruler.MoveOrAddLeftTabStop(
            [new TabStop(72), new TabStop(144, TabStopAlignment.Decimal, TabLeader.Underline)],
            index: 1,
            positionPt: 218);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(216, TabStopAlignment.Decimal, TabLeader.Underline));
    }

    [Fact]
    public void RemoveTabStop_RemovesRequestedStop()
    {
        var stops = Ruler.RemoveTabStop(
            [
                new TabStop(72),
                new TabStop(144, TabStopAlignment.Decimal, TabLeader.Underline),
                new TabStop(216)
            ],
            index: 1);

        stops.Should().Equal(
            new TabStop(72),
            new TabStop(216));
    }

    [Fact]
    public void RemoveTabStop_IgnoresInvalidIndex()
    {
        var start = new[]
        {
            new TabStop(72),
            new TabStop(144, TabStopAlignment.Right, TabLeader.Dashes)
        };

        Ruler.RemoveTabStop(start, index: -1).Should().Equal(start);
        Ruler.RemoveTabStop(start, index: 2).Should().Equal(start);
    }

    [Fact]
    public void IsTabStopRemovalDrop_RequiresClearVerticalDropOutsideRuler()
    {
        var size = new Size(1000, 16);

        Ruler.IsTabStopRemovalDrop(new Point(200, 8), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, -6), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, 22), size).Should().BeFalse();
        Ruler.IsTabStopRemovalDrop(new Point(200, -8), size).Should().BeTrue();
        Ruler.IsTabStopRemovalDrop(new Point(200, 24), size).Should().BeTrue();
    }

    [Fact]
    public void IndentsForDrag_UpdatesTheRequestedIndentOnly()
    {
        var start = ParagraphFormatting.Default with
        {
            IndentLeftPt = 36,
            IndentRightPt = 18,
            FirstLineIndentPt = -12
        };

        Ruler.IndentsForDrag(start, Ruler.DragKind.LeftIndent, 74).Should().Be(
            start with { IndentLeftPt = 72 });

        Ruler.IndentsForDrag(start, Ruler.DragKind.FirstLineIndent, 60).Should().Be(
            start with { FirstLineIndentPt = 24 });

        Ruler.IndentsForDrag(start, Ruler.DragKind.RightIndent, 30).Should().Be(
            start with { IndentRightPt = 30 });
    }
}
