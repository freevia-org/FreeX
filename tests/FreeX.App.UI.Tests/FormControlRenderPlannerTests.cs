using System.Windows;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class FormControlRenderPlannerTests
{
    [Fact]
    public void TryCreateSpanningAnchorRect_SpansFromTopLeftToBottomRightOfToCell()
    {
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20)],
            [new ColMetric(1, 80, 0), new ColMetric(2, 80, 80), new ColMetric(3, 80, 160)]);
        // Single-row anchor (from row 1 col 1, to row 1 col 3) -> 0-based From(0,0) To(2,0).
        var anchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(0, 0, 0, 0),
            new DrawingAnchorPoint(2, 0, 0, 0));

        var created = GridDrawingObjectPlanner.TryCreateSpanningAnchorRect(
            viewport,
            anchor,
            rowHeaderWidth: 0,
            columnHeaderHeight: 0,
            out var rect);

        created.Should().BeTrue();
        // Spans col1.left(0) .. col3.right(160+80=240); row1.top(0) .. row1.bottom(0+20=20).
        rect.Should().Be(new Rect(0, 0, 240, 20));
    }

    private static GridRange Range(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheet = SheetId.New();
        return new GridRange(
            new CellAddress(sheet, startRow, startCol),
            new CellAddress(sheet, endRow, endCol));
    }

    [Fact]
    public void TryCreateAnchorRange_ConvertsOneBasedGridRangeToZeroBasedDrawingAnchor()
    {
        var control = new FormControlModel { Anchor = Range(3, 2, 5, 4) };

        var created = FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor);

        created.Should().BeTrue();
        anchor!.From.Column.Should().Be(1);
        anchor.From.Row.Should().Be(2);
        anchor.To.Column.Should().Be(3);
        anchor.To.Row.Should().Be(4);
        anchor.From.ColumnOffsetEmu.Should().Be(0);
        anchor.From.RowOffsetEmu.Should().Be(0);
    }

    [Fact]
    public void TryCreateAnchorRange_PrefersPreservedSubCellOffsetsWhenPresent()
    {
        var control = new FormControlModel
        {
            Anchor = Range(9, 6, 11, 6),
            // 0-based EMU offsets (as preserved by the IO mapper).
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(5, 171450, 8, 171450),
                new DrawingAnchorPoint(5, 476250, 10, 19050)),
        };

        var created = FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor);

        created.Should().BeTrue();
        anchor!.From.Column.Should().Be(5);
        anchor.From.Row.Should().Be(8);
        anchor.From.ColumnOffsetEmu.Should().Be(171450);
        anchor.From.RowOffsetEmu.Should().Be(171450);
        anchor.To.Column.Should().Be(5);
        anchor.To.Row.Should().Be(10);
        anchor.To.ColumnOffsetEmu.Should().Be(476250);
        anchor.To.RowOffsetEmu.Should().Be(19050);
    }

    [Fact]
    public void TryCreateAnchorRange_FallsBackToWholeCellWhenOffsetsAbsent()
    {
        var control = new FormControlModel { Anchor = Range(3, 2, 5, 4), AnchorOffsets = null };

        FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor).Should().BeTrue();
        anchor!.From.Column.Should().Be(1);
        anchor.From.ColumnOffsetEmu.Should().Be(0);
        anchor.To.Column.Should().Be(3);
        anchor.To.RowOffsetEmu.Should().Be(0);
    }

    [Fact]
    public void HasSubCellOffsets_TrueOnlyWhenOffsetsPreserved()
    {
        var withOffsets = new FormControlModel
        {
            Anchor = Range(1, 1, 1, 1),
            AnchorOffsets = new DrawingAnchorRange(
                new DrawingAnchorPoint(0, 100, 0, 100),
                new DrawingAnchorPoint(0, 200, 0, 200)),
        };
        var withoutOffsets = new FormControlModel { Anchor = Range(1, 1, 1, 1) };

        FormControlRenderPlanner.HasSubCellOffsets(withOffsets).Should().BeTrue();
        FormControlRenderPlanner.HasSubCellOffsets(withoutOffsets).Should().BeFalse();
    }

    [Fact]
    public void OffsetAwareRect_PlacesControlWithinCellNotFlushToGrid()
    {
        // Single column (col6, 60px wide) spanning rows 9..11 (each 20px).
        var viewport = new ViewportModel(
            [],
            [new RowMetric(9, 20, 0), new RowMetric(10, 20, 20), new RowMetric(11, 20, 40)],
            [new ColMetric(6, 60, 0)]);
        // from col6 row9 +18px(171450 EMU), to col6 row11 +50px col / +2px row.
        var offsetAnchor = new DrawingAnchorRange(
            new DrawingAnchorPoint(5, 171450, 8, 171450),
            new DrawingAnchorPoint(5, 476250, 10, 19050));

        var created = GridDrawingObjectPlanner.TryCreateDrawingAnchorRect(
            viewport,
            offsetAnchor,
            rowHeaderWidth: 0,
            columnHeaderHeight: 0,
            out var rect);

        created.Should().BeTrue();
        // left = col6.left(0) + 18px; top = row9.top(0) + 18px.
        rect.Left.Should().BeApproximately(18, 0.01);
        rect.Top.Should().BeApproximately(18, 0.01);
        // right = col6.left(0) + 50px; bottom = row11.top(40) + 2px = 42.
        rect.Width.Should().BeApproximately(50 - 18, 0.01);
        rect.Height.Should().BeApproximately(42 - 18, 0.01);
    }

    [Fact]
    public void TryCreateAnchorRange_ReturnsFalseWhenAnchorMissing()
    {
        var control = new FormControlModel { Anchor = null };

        FormControlRenderPlanner.TryCreateAnchorRange(control, out var anchor)
            .Should().BeFalse();
        anchor.Should().BeNull();
    }

    [Theory]
    [InlineData(FormControlKind.CheckBox, true)]
    [InlineData(FormControlKind.OptionButton, true)]
    [InlineData(FormControlKind.Spinner, true)]
    [InlineData(FormControlKind.ScrollBar, true)]
    [InlineData(FormControlKind.Label, true)]
    [InlineData(FormControlKind.GroupBox, true)]
    [InlineData(FormControlKind.Button, true)]
    [InlineData(FormControlKind.DropDown, true)]
    [InlineData(FormControlKind.ListBox, true)]
    [InlineData(FormControlKind.Unknown, false)]
    public void IsRenderable_MatchesImplementedControlKinds(FormControlKind kind, bool expected)
    {
        FormControlRenderPlanner.IsRenderable(kind).Should().Be(expected);
    }

    [Fact]
    public void GetDropDownButtonRect_PlacesSquareButtonFlushAgainstRightEdge()
    {
        var rect = new Rect(10, 20, 120, 21);

        var button = FormControlRenderPlanner.GetDropDownButtonRect(rect);

        // Button is a square sized to the control height, flush against the right edge.
        button.Width.Should().BeApproximately(21, 0.01);
        button.Height.Should().BeApproximately(21, 0.01);
        button.Right.Should().BeApproximately(rect.Right, 0.01);
        button.Top.Should().BeApproximately(rect.Top, 0.01);
    }

    [Fact]
    public void GetDropDownButtonRect_ClampsButtonWidthToHalfWhenControlIsNarrow()
    {
        // A short, tall control: the button must not consume the whole box.
        var rect = new Rect(0, 0, 12, 40);

        var button = FormControlRenderPlanner.GetDropDownButtonRect(rect);

        button.Width.Should().BeLessThanOrEqualTo(rect.Width / 2 + 0.01);
        button.Right.Should().BeApproximately(rect.Right, 0.01);
    }

    [Fact]
    public void GetDropDownTextRect_OccupiesAreaLeftOfButton()
    {
        var rect = new Rect(10, 20, 120, 21);
        var button = FormControlRenderPlanner.GetDropDownButtonRect(rect);

        var textRect = FormControlRenderPlanner.GetDropDownTextRect(rect, button);

        textRect.Left.Should().BeApproximately(rect.Left, 0.01);
        textRect.Right.Should().BeApproximately(button.Left, 0.01);
        textRect.Top.Should().BeApproximately(rect.Top, 0.01);
        textRect.Height.Should().BeApproximately(rect.Height, 0.01);
    }

    [Fact]
    public void GetCaption_ReturnsAuthoredCaption()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Caption = "Include weekends" };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Include weekends");
    }

    [Fact]
    public void GetCaption_ReturnsEmptyWhenNoCaption_AndNeverUsesName()
    {
        // The internal shape Name ("Check Box 1") is NOT a visible label — when there is no authored
        // caption, Excel draws nothing and so must we (no Name / kind-label fallback).
        var control = new FormControlModel { Kind = FormControlKind.OptionButton, Name = "Option Button 3", Caption = null };

        FormControlRenderPlanner.GetCaption(control).Should().BeEmpty();
    }

    [Fact]
    public void GetCaption_TrimsWhitespace()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Caption = "  Tax  " };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Tax");
    }
}
