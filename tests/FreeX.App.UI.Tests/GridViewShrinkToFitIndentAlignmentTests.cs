using FluentAssertions;
using FreeX.App.UI;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R53-render-number-align-indent-rotation-3-2: Format Cells &gt; Alignment &gt; Indent only pulls
/// text away from the edge it anchors to (Left/Right/General); Center/Justify/Distributed/Fill
/// center or repeat the text instead, so Excel's Indent has no effect on them. The Shrink-to-Fit
/// font-size search in GridView.Rendering.cs must therefore skip subtracting the cell's Indent from
/// the available width when the alignment doesn't consume it — see
/// <see cref="GridView.DoesHorizontalAlignmentConsumeIndent"/>, which mirrors
/// CellTextOrientationLayoutPlanner.CalculateLayout's boundsX switch (whose Center/Justify/
/// Distributed/Fill branches never reference indentPixels).
/// </summary>
public sealed class GridViewShrinkToFitIndentAlignmentTests
{
    [Fact]
    public void DoesHorizontalAlignmentConsumeIndent_CenterAlignment_ReturnsFalse()
    {
        // Pre-fix bug: the Shrink-to-Fit available-width computation subtracted indentPx
        // unconditionally, even for Center, shrinking the font more than Excel would.
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Center).Should().BeFalse();
    }

    [Fact]
    public void DoesHorizontalAlignmentConsumeIndent_JustifyAndDistributedAndFill_ReturnFalse()
    {
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Justify).Should().BeFalse();
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Distributed).Should().BeFalse();
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Fill).Should().BeFalse();
    }

    [Fact]
    public void DoesHorizontalAlignmentConsumeIndent_LeftRightGeneral_ReturnTrue_NoRegression()
    {
        // Sibling/no-regression: Left/Right/General are the alignments Excel's Indent control
        // actually affects, so the Shrink-to-Fit width must keep subtracting indentPx for them,
        // exactly as before the fix.
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Left).Should().BeTrue();
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.Right).Should().BeTrue();
        GridView.DoesHorizontalAlignmentConsumeIndent(CellHAlign.General).Should().BeTrue();
    }
}
