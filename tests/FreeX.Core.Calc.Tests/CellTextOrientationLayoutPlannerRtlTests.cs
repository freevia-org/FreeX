using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for K14: <see cref="CellStyle.ReadingOrder"/> was faithfully round-tripped
/// through XLSX IO but never consumed anywhere in layout/alignment resolution, so General
/// alignment always resolved left-to-right regardless of the cell's stored reading order (or the
/// sheet's RTL flag). These tests cover the new effective-reading-order / effective-alignment API
/// (<see cref="CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft"/> and
/// <see cref="CellTextOrientationLayoutPlanner.ResolveEffectiveHorizontalAlignment"/>) and its
/// wiring into <see cref="CellTextOrientationLayoutPlanner.CalculateLayout"/>.
/// </summary>
public sealed class CellTextOrientationLayoutPlannerRtlTests
{
    // ── ResolveIsEffectivelyRightToLeft ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(CellReadingOrder.Context, false, false)]
    [InlineData(CellReadingOrder.Context, true, true)]
    public void ResolveIsEffectivelyRightToLeft_ContextFollowsSheet(
        CellReadingOrder readingOrder, bool sheetIsRightToLeft, bool expected)
    {
        CellTextOrientationLayoutPlanner
            .ResolveIsEffectivelyRightToLeft(readingOrder, sheetIsRightToLeft)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveIsEffectivelyRightToLeft_LeftToRightOverride_AlwaysForcesLtr(bool sheetIsRightToLeft)
    {
        CellTextOrientationLayoutPlanner
            .ResolveIsEffectivelyRightToLeft(CellReadingOrder.LeftToRight, sheetIsRightToLeft)
            .Should().BeFalse("an explicit readingOrder=\"1\" must force LTR regardless of the sheet");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveIsEffectivelyRightToLeft_RightToLeftOverride_AlwaysForcesRtl(bool sheetIsRightToLeft)
    {
        CellTextOrientationLayoutPlanner
            .ResolveIsEffectivelyRightToLeft(CellReadingOrder.RightToLeft, sheetIsRightToLeft)
            .Should().BeTrue("an explicit readingOrder=\"2\" must force RTL regardless of the sheet");
    }

    // ── ResolveEffectiveHorizontalAlignment ─────────────────────────────────────────────────

    [Fact]
    public void ResolveEffectiveHorizontalAlignment_GeneralNumeric_Ltr_ResolvesRight()
    {
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(HorizontalAlignment.General, isNumeric: true, isEffectivelyRightToLeft: false)
            .Should().Be(HorizontalAlignment.Right);
    }

    [Fact]
    public void ResolveEffectiveHorizontalAlignment_GeneralNumeric_Rtl_ResolvesLeft()
    {
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(HorizontalAlignment.General, isNumeric: true, isEffectivelyRightToLeft: true)
            .Should().Be(HorizontalAlignment.Left, "Excel mirrors General-numeric alignment to the 'start' in an RTL sheet");
    }

    [Fact]
    public void ResolveEffectiveHorizontalAlignment_GeneralText_Ltr_ResolvesLeft()
    {
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(HorizontalAlignment.General, isNumeric: false, isEffectivelyRightToLeft: false)
            .Should().Be(HorizontalAlignment.Left);
    }

    [Fact]
    public void ResolveEffectiveHorizontalAlignment_GeneralText_Rtl_ResolvesRight()
    {
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(HorizontalAlignment.General, isNumeric: false, isEffectivelyRightToLeft: true)
            .Should().Be(HorizontalAlignment.Right, "Excel right-aligns General text in an RTL sheet");
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left)]
    [InlineData(HorizontalAlignment.Right)]
    [InlineData(HorizontalAlignment.Center)]
    [InlineData(HorizontalAlignment.Justify)]
    [InlineData(HorizontalAlignment.Distributed)]
    [InlineData(HorizontalAlignment.Fill)]
    public void ResolveEffectiveHorizontalAlignment_ExplicitAlignment_NeverMirrors(HorizontalAlignment alignment)
    {
        // Only General auto-mirrors with reading order; an explicit user choice must survive
        // unchanged in both an RTL and LTR context.
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(alignment, isNumeric: true, isEffectivelyRightToLeft: true)
            .Should().Be(alignment);
        CellTextOrientationLayoutPlanner
            .ResolveEffectiveHorizontalAlignment(alignment, isNumeric: false, isEffectivelyRightToLeft: false)
            .Should().Be(alignment);
    }

    // ── CalculateLayout wiring ───────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateLayout_GeneralNumericText_InRtlContext_LeftAlignsInsteadOfRight()
    {
        // Same geometry as CalculateLayout_RightAlignsGeneralNumericText (LTR) but with
        // isEffectivelyRightToLeft: true — Excel mirrors General-numeric to the left in an RTL sheet.
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.General,
            VerticalAlignment.Bottom,
            isNumeric: true,
            indentPixels: 0,
            textRotation: 0,
            isEffectivelyRightToLeft: true);

        layout.Bounds.Left.Should().BeApproximately(12, 0.001, "General-numeric must anchor left in an RTL context");
    }

    [Fact]
    public void CalculateLayout_GeneralText_InRtlContext_RightAlignsInsteadOfLeft()
    {
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.General,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 0,
            isEffectivelyRightToLeft: true);

        layout.Bounds.Right.Should().BeApproximately(108, 0.001, "General text must anchor right in an RTL context");
    }

    [Fact]
    public void CalculateLayout_DefaultsToLtr_WhenIsEffectivelyRightToLeftOmitted()
    {
        // Backward-compatibility: existing callers that do not pass isEffectivelyRightToLeft must
        // see unchanged (LTR) behavior.
        var layout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.General,
            VerticalAlignment.Bottom,
            isNumeric: true,
            indentPixels: 0,
            textRotation: 0);

        layout.TextPoint.X.Should().Be(78);
        layout.Bounds.Should().Be(new CellTextLayoutRect(78, 49, 30, 10));
    }

    [Fact]
    public void CalculateLayout_ExplicitRightAlignment_DoesNotMirrorInRtlContext()
    {
        // Right.PY 78 (LTR) — a genuinely-explicit Right alignment must land in the same place
        // whether or not the context is RTL, since only General mirrors.
        var ltrLayout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 0,
            isEffectivelyRightToLeft: false);

        var rtlLayout = CellTextOrientationLayoutPlanner.CalculateLayout(
            new CellTextLayoutRect(10, 20, 100, 40),
            textWidth: 30,
            textHeight: 10,
            HorizontalAlignment.Right,
            VerticalAlignment.Bottom,
            isNumeric: false,
            indentPixels: 0,
            textRotation: 0,
            isEffectivelyRightToLeft: true);

        rtlLayout.Bounds.Left.Should().Be(ltrLayout.Bounds.Left);
    }
}
