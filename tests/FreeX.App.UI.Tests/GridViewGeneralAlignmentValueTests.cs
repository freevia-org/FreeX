using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using CellHAlign = FreeX.Core.Model.HorizontalAlignment;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R53-render-number-align-indent-rotation-3-1: General alignment must center Boolean/Error cell
/// values (matching real Excel, which left-aligns text, right-aligns numbers/dates, and centers
/// Booleans/Errors under General) instead of leaving them flush-left like text. See
/// GridView.Rendering.cs's <see cref="GridView.ResolveGeneralAlignmentHorizontalAlignment"/>, which
/// feeds the layout call (<see cref="GridView.CalculateCellTextRenderLayout"/>) an explicit Center
/// instead of General for these values (CellTextOrientationLayoutPlanner.ResolveEffectiveHorizontalAlignment
/// only ever resolves General to Left or Right, so it can't produce Center on its own).
/// </summary>
public sealed class GridViewGeneralAlignmentValueTests
{
    [Fact]
    public void ResolveGeneralAlignmentHorizontalAlignment_BooleanUnderGeneral_ResolvesToCenter()
    {
        GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new BoolValue(true))
            .Should().Be(CellHAlign.Center);
    }

    [Fact]
    public void ResolveGeneralAlignmentHorizontalAlignment_ErrorUnderGeneral_ResolvesToCenter()
    {
        GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new ErrorValue("#DIV/0!"))
            .Should().Be(CellHAlign.Center);
    }

    [Fact]
    public void ResolveGeneralAlignmentHorizontalAlignment_NumericAndTextUnderGeneral_UnchangedNoRegression()
    {
        // Sibling/no-regression: numeric and text content must keep flowing through the planner's
        // own Left/Right General resolution unmodified (only Boolean/Error get the local override).
        GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new NumberValue(5))
            .Should().Be(CellHAlign.General);
        GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new TextValue("hello"))
            .Should().Be(CellHAlign.General);
    }

    [Fact]
    public void ResolveGeneralAlignmentHorizontalAlignment_ExplicitAlignmentNeverOverridden()
    {
        // An explicit user-chosen alignment (not General) must never be re-derived from the value
        // type, even for Boolean/Error content.
        GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.Left, new BoolValue(false))
            .Should().Be(CellHAlign.Left);
    }

    [Theory]
    [InlineData(CellHAlign.Right, true, false, true)]
    [InlineData(CellHAlign.General, true, false, true)]
    [InlineData(CellHAlign.General, true, true, false)]
    [InlineData(CellHAlign.General, false, false, false)]
    [InlineData(CellHAlign.General, false, true, true)]
    [InlineData(CellHAlign.Center, true, false, false)]
    public void ShouldClipRightAlignedText_TracksEffectiveRightAlignment(
        CellHAlign hAlign,
        bool isNumeric,
        bool isEffectivelyRightToLeft,
        bool expected)
    {
        GridView.ShouldClipRightAlignedText(hAlign, isNumeric, isEffectivelyRightToLeft)
            .Should().Be(expected);
    }

    [Fact]
    public void CalculateCellTextRenderLayout_BooleanGeneralAlignment_CentersTextInCell()
    {
        // End-to-end through the real production wrapper: a General-aligned Boolean cell must land
        // its text horizontally centered in the cell, not flush-left like plain text would.
        var rect = new Rect(0, 0, 100, 20);
        var hAlign = GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new BoolValue(true));

        var layout = GridView.CalculateCellTextRenderLayout(
            rect, textWidth: 20, textHeight: 12, hAlign, vAlign: null, isNumeric: false, indentPx: 0, textRotation: 0);

        layout.TextPoint.X.Should().Be(40, "Center anchors the text at Left + (Width - textWidth) / 2 = (100 - 20) / 2");
    }

    [Fact]
    public void CalculateCellTextRenderLayout_TextGeneralAlignment_StaysFlushLeft_NoRegression()
    {
        var rect = new Rect(0, 0, 100, 20);
        var hAlign = GridView.ResolveGeneralAlignmentHorizontalAlignment(CellHAlign.General, new TextValue("hello"));

        var layout = GridView.CalculateCellTextRenderLayout(
            rect, textWidth: 20, textHeight: 12, hAlign, vAlign: null, isNumeric: false, indentPx: 0, textRotation: 0);

        layout.TextPoint.X.Should().Be(2, "plain text under General must remain flush-left (Left + 2), unaffected by the Boolean/Error fix");
    }
}
