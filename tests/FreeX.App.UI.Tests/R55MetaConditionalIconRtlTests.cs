using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R55-meta-1: R54 added RTL-mirroring support to the portable
/// <see cref="ConditionalIconCellLayoutPlanner"/> (an <c>isRightToLeft</c> parameter), but
/// <see cref="GridView.CalculateConditionalIconCellLayout"/> — the exact call chain both
/// GridView.Rendering.cs render passes (main and split-pane) invoke — never threaded the
/// sheet's reading order through, so icon-set glyphs stayed pinned to the left edge on
/// right-to-left sheets. These tests drive that same call chain directly.
/// </summary>
public sealed class R55MetaConditionalIconRtlTests
{
    [Fact]
    public void CalculateConditionalIconCellLayout_RightToLeftSheet_MirrorsIconToRightEdge()
    {
        var cellRect = new Rect(10, 20, 80, 22);
        var icon = new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: true);

        var layout = GridView.CalculateConditionalIconCellLayout(cellRect, icon, isRightToLeft: true);

        // Excel mirrors the icon glyph to the cell's right edge on an RTL sheet (matching how it
        // already mirrors data bars, row headers, and cell alignment) — the glyph must sit in the
        // right half of the cell, hugging the right edge, not the left.
        layout.IconRect.Right.Should().BeApproximately(cellRect.Right - 4, 0.001);
        layout.IconRect.Left.Should().BeGreaterThan(cellRect.Left + cellRect.Width / 2);

        // The value text is pushed to the left of the (right-pinned) icon, mirroring the LTR layout.
        layout.ShouldDrawText.Should().BeTrue();
        layout.TextRect.Left.Should().BeApproximately(cellRect.Left, 0.001);
        layout.TextRect.Right.Should().BeLessThanOrEqualTo(layout.IconRect.Left);
    }

    [Fact]
    public void CalculateConditionalIconCellLayout_LeftToRightSheet_KeepsIconPinnedLeft()
    {
        // Sibling no-regression case: an LTR sheet (the default, and the pre-existing behavior for
        // every caller that doesn't pass isRightToLeft) must still pin the glyph to the left edge.
        var cellRect = new Rect(10, 20, 80, 22);
        var icon = new ConditionalFormatIcon("3TrafficLights1", 1, 3, ShowValue: true);

        var explicitLtr = GridView.CalculateConditionalIconCellLayout(cellRect, icon, isRightToLeft: false);
        var defaultLtr = GridView.CalculateConditionalIconCellLayout(cellRect, icon);

        foreach (var layout in new[] { explicitLtr, defaultLtr })
        {
            layout.IconRect.Left.Should().BeApproximately(cellRect.Left + 4, 0.001);
            layout.IconRect.Left.Should().BeLessThan(cellRect.Left + cellRect.Width / 2);
            layout.ShouldDrawText.Should().BeTrue();
            layout.TextRect.Left.Should().BeGreaterThan(layout.IconRect.Right);
        }
    }
}
