using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.UI.Tests;

// Regression coverage for R47-render-rtl-mixed-3-1: wrapped-text General alignment must honor
// RTL/isNumeric, matching real Excel (a wrapped RTL paragraph ragged-edges on the LEFT i.e.
// flush-right; a wrapped numeric General cell stays right; only wrapped LTR text is flush-left) and
// the already-correct Avalonia shell logic (MainWindow.MapCellTextAlignment).
public sealed class GridViewWrapTextAlignmentTests
{
    [Fact]
    public void ResolveWrapTextAlignment_GeneralText_RightToLeft_IsFlushRight()
    {
        // Pre-fix bug: General always resolved to TextAlignment.Left regardless of RTL, so a
        // wrapped Hebrew/Arabic paragraph's ragged edge showed on the wrong (right) side instead
        // of the Excel-correct left side (i.e. text flush-RIGHT).
        GridView.ResolveWrapTextAlignment(CellHAlign.General, isNumeric: false, isEffectivelyRightToLeft: true)
            .Should().Be(TextAlignment.Right);
    }

    [Fact]
    public void ResolveWrapTextAlignment_GeneralText_LeftToRight_IsFlushLeft_NoRegression()
    {
        // Sibling no-regression case: General text in a normal LTR sheet must keep flushing left,
        // exactly as before the fix.
        GridView.ResolveWrapTextAlignment(CellHAlign.General, isNumeric: false, isEffectivelyRightToLeft: false)
            .Should().Be(TextAlignment.Left);
    }

    [Fact]
    public void ResolveWrapTextAlignment_GeneralNumeric_RightToLeft_IsFlushLeft()
    {
        GridView.ResolveWrapTextAlignment(CellHAlign.General, isNumeric: true, isEffectivelyRightToLeft: true)
            .Should().Be(TextAlignment.Left);
    }

    [Fact]
    public void ResolveWrapTextAlignment_GeneralNumeric_LeftToRight_IsFlushRight()
    {
        // Pre-fix bug: General-numeric always resolved to Left; Excel-correct is Right (numbers
        // anchor to the end of the reading direction, i.e. the right side in an LTR sheet).
        GridView.ResolveWrapTextAlignment(CellHAlign.General, isNumeric: true, isEffectivelyRightToLeft: false)
            .Should().Be(TextAlignment.Right);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveWrapTextAlignment_ExplicitRight_AlwaysFlushRight_NoRegression(bool isEffectivelyRightToLeft)
    {
        GridView.ResolveWrapTextAlignment(CellHAlign.Right, isNumeric: false, isEffectivelyRightToLeft)
            .Should().Be(TextAlignment.Right);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveWrapTextAlignment_ExplicitLeft_AlwaysFlushLeft_NoRegression(bool isEffectivelyRightToLeft)
    {
        GridView.ResolveWrapTextAlignment(CellHAlign.Left, isNumeric: false, isEffectivelyRightToLeft)
            .Should().Be(TextAlignment.Left);
    }

    [Theory]
    [InlineData(CellHAlign.Center)]
    [InlineData(CellHAlign.Justify)]
    [InlineData(CellHAlign.Distributed)]
    public void ResolveWrapTextAlignment_CenterJustifyDistributed_AlwaysCenter_NoRegression(CellHAlign hAlign)
    {
        GridView.ResolveWrapTextAlignment(hAlign, isNumeric: false, isEffectivelyRightToLeft: true)
            .Should().Be(TextAlignment.Center);
        GridView.ResolveWrapTextAlignment(hAlign, isNumeric: false, isEffectivelyRightToLeft: false)
            .Should().Be(TextAlignment.Center);
    }

    [Theory]
    [InlineData(120)]
    [InlineData(220)]
    public void RightAlignedWrappedText_UsesParagraphBoxWidthSoWiderCellsStayInside(double cellWidth)
    {
        var rect = new Rect(10, 0, cellWidth, 20);
        var wrapMaxTextWidth = Math.Max(1, rect.Width - 4);
        var layoutWidth = GridView.ResolveCellTextLayoutWidth(
            formattedTextWidth: 62,
            wrapMaxTextWidth,
            wrapText: true);

        var layout = GridView.CalculateCellTextRenderLayout(
            rect,
            layoutWidth,
            textHeight: 12,
            CellHAlign.Right,
            vAlign: null,
            isNumeric: true,
            indentPx: 0,
            textRotation: 0);

        layout.TextPoint.X.Should().Be(rect.Left + 2);
        layout.Bounds.Right.Should().BeLessThanOrEqualTo(rect.Right - 2);
    }

    [Fact]
    public void ResolveCellTextLayoutWidth_UnwrappedTextKeepsMeasuredGlyphWidth()
    {
        GridView.ResolveCellTextLayoutWidth(
                formattedTextWidth: 62,
                wrapMaxTextWidth: 196,
                wrapText: false)
            .Should()
            .Be(62);
    }
}
