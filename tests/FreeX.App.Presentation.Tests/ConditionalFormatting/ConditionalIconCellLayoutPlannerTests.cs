using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// R54-render-cf-icon-databar-4-3: on a right-to-left sheet, Excel mirrors icon-set glyphs to the
/// cell's RIGHT edge (with the value text pushed toward the left), the same way it already mirrors
/// data bars (<c>ViewportConditionalFormatEvaluator.Thresholds.cs</c>'s <c>MirrorDataBarIfRightToLeft</c>),
/// row headers, and cell alignment. <see cref="ConditionalIconCellLayoutPlanner.CalculateCellLayout"/>
/// previously had no reading-order parameter at all, so the icon always rendered pinned to the cell's
/// left edge regardless of the sheet's <c>IsRightToLeft</c> setting.
/// </summary>
public sealed class ConditionalIconCellLayoutPlannerTests
{
    [Fact]
    public void CalculateCellLayout_RightToLeft_MirrorsIconToRightEdge_AndTextToTheLeft()
    {
        var layout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 20, showValue: true, isRightToLeft: true);

        // Icon size is the usual 10px (unaffected by reading order), but its LEFT origin must now sit
        // near the cell's RIGHT edge (100 - 4 inset - 10 size = 86), not near cellLeft (the bug: the
        // icon rendered pinned to the left edge on every sheet, RTL or not).
        layout.IconSize.Should().Be(10);
        layout.IconLeft.Should().Be(86);

        // The value text must run from the cell's LEFT edge up to where the (now right-side) gutter
        // begins (100 - 20 gutter = 80), mirroring the LTR case's "icon-then-gutter-then-text" order.
        layout.TextLeft.Should().Be(0);
        layout.TextWidth.Should().Be(80);
        layout.ShouldDrawText.Should().BeTrue();
    }

    [Fact]
    public void CalculateCellLayout_LeftToRight_PinsIconToLeftEdge_NoRegression()
    {
        // Sibling no-regression case: the same cell geometry with the default (omitted) reading-order
        // parameter must reproduce the pre-existing left-pinned layout exactly.
        var layout = ConditionalIconCellLayoutPlanner.CalculateCellLayout(
            cellLeft: 0, cellTop: 0, cellWidth: 100, cellHeight: 20, showValue: true);

        layout.IconSize.Should().Be(10);
        layout.IconLeft.Should().Be(4);
        layout.TextLeft.Should().Be(20);
        layout.TextWidth.Should().Be(80);
        layout.ShouldDrawText.Should().BeTrue();
    }
}
