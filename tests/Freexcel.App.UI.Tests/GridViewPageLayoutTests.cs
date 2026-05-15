using FluentAssertions;
using Freexcel.App.UI;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.UI.Tests;

public sealed class GridViewPageLayoutTests
{
    [Fact]
    public void CalculatePageMarginRulerHandles_MapsMarginsToHorizontalAndVerticalRulerHandles()
    {
        var pageBounds = new Rect(30, 18, 850, 1100);

        var handles = GridView.CalculatePageMarginRulerHandles(
            pageBounds,
            WorksheetPaperSize.Letter,
            WorksheetPageOrientation.Portrait,
            WorksheetPageMargins.Normal);

        handles.Left.Should().Be(new Rect(130 - 4, 18 - 14, 8, 12));
        handles.Right.Should().Be(new Rect(780 - 4, 18 - 14, 8, 12));
        handles.Top.Should().Be(new Rect(30 - 14, 118 - 4, 12, 8));
        handles.Bottom.Should().Be(new Rect(30 - 14, 1018 - 4, 12, 8));
    }

    [Fact]
    public void HitTestPageMarginRulerHandles_ReturnsMarginEdgeForHandle()
    {
        var handles = new PageMarginRulerHandles(
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        GridView.HitTestPageMarginRulerHandles(handles, new Point(130, 10))
            .Should().Be(WorksheetPageMarginEdge.Left);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(780, 10))
            .Should().Be(WorksheetPageMarginEdge.Right);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(20, 118))
            .Should().Be(WorksheetPageMarginEdge.Top);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(20, 1018))
            .Should().Be(WorksheetPageMarginEdge.Bottom);
        GridView.HitTestPageMarginRulerHandles(handles, new Point(200, 200))
            .Should().BeNull();
    }

    [Fact]
    public void HitTestPageMarginRulerHandles_ReturnsNullWhenRulersAreHidden()
    {
        var handles = new PageMarginRulerHandles(
            new Rect(126, 4, 8, 12),
            new Rect(776, 4, 8, 12),
            new Rect(16, 114, 12, 8),
            new Rect(16, 1014, 12, 8));

        GridView.HitTestPageMarginRulerHandles(handles, new Point(130, 10), showRulers: false)
            .Should().BeNull();
    }
}
