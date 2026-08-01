using Avalonia;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

public sealed class AvaloniaInlineTableLayoutPlannerTests
{
    [Fact]
    public void AuthoredCellInsetsAndBottomAnchorShapeInlineTableTextArea()
    {
        var cell = new TableCell
        {
            InsetLeftPt = 6,
            InsetTopPt = 12,
            InsetRightPt = 18,
            InsetBottomPt = 24,
            Anchor = TableCellAnchor.Bottom,
        };

        var plan = AvaloniaInlineTableLayoutPlanner.PlanCellText(
            cell,
            new Rect(10, 20, 100, 80),
            measuredTextHeight: 10);

        plan.Area.Should().Be(new Rect(18, 36, 68, 32));
        plan.Origin.Should().Be(new Point(18, 58));
    }

    [Fact]
    public void UnspecifiedInsetsKeepExistingInlineTableInsetAndTopAnchor()
    {
        var plan = AvaloniaInlineTableLayoutPlanner.PlanCellText(
            new TableCell(),
            new Rect(10, 20, 40, 30),
            measuredTextHeight: 100);

        plan.Area.Should().Be(new Rect(12, 22, 36, 26));
        plan.Origin.Should().Be(new Point(12, 22));
    }
}
