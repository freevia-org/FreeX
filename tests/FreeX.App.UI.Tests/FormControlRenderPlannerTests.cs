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
    [InlineData(FormControlKind.Button, false)]
    [InlineData(FormControlKind.DropDown, false)]
    [InlineData(FormControlKind.ListBox, false)]
    [InlineData(FormControlKind.Unknown, false)]
    public void IsRenderable_MatchesImplementedControlKinds(FormControlKind kind, bool expected)
    {
        FormControlRenderPlanner.IsRenderable(kind).Should().Be(expected);
    }

    [Fact]
    public void GetCaption_PrefersExplicitNameOverFallback()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Name = "Include weekends" };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Include weekends");
    }

    [Fact]
    public void GetCaption_FallsBackToKindWhenNameMissing()
    {
        var control = new FormControlModel { Kind = FormControlKind.OptionButton, Name = null };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Option Button");
    }

    [Fact]
    public void GetCaption_TrimsWhitespace()
    {
        var control = new FormControlModel { Kind = FormControlKind.CheckBox, Name = "  Tax  " };

        FormControlRenderPlanner.GetCaption(control).Should().Be("Tax");
    }
}
