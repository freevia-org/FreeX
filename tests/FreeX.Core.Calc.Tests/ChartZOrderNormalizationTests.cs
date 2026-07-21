using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R62-meta-1: a Chart is a supported <see cref="SelectionPaneObjectKind"/> (see
/// <see cref="DrawingObjectZOrder.IsSupportedKind"/>), but <see cref="DrawingObjectZOrder.ContainsObject"/>
/// used to report "not present" for every Chart entry, so <see cref="DrawingObjectZOrder.GetNormalizedOrder"/>
/// silently stripped a chart's recorded z-order slot on every normalization pass -- and
/// <see cref="MoveSelectionPaneObjectCommand"/> routed a Chart's Bring Forward/Send Backward entirely
/// through the (unrelated) Charts list, never touching <see cref="Sheet.DrawingObjectZOrder"/> at all.
/// Net effect: a chart always rendered/hit-tested as topmost regardless of its real stacking position,
/// and moving it in the Selection Pane had no effect on the mixed shape/chart stack.
/// </summary>
public sealed class ChartZOrderNormalizationTests
{
    [Fact]
    public void GetNormalizedOrder_PreservesChartRecordedBelowShape()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 3))
        };
        sheet.DrawingShapes.Add(shape);
        sheet.Charts.Add(chart);

        // Mirrors exactly what a correct OOXML loader records via AddLoadedDrawingObjectOrder /
        // ApplyLoadedDrawingObjectZOrder for a chart drawn BELOW the shape in the file's drawing
        // order (chart's anchor comes first in the drawing XML, so its order index is lower).
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id));
        sheet.DrawingObjectZOrder.Add(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));

        var normalized = DrawingObjectZOrder.GetNormalizedOrder(sheet);

        // The chart must stay in its recorded slot BELOW the shape, not get stripped out entirely
        // (pre-fix: ContainsObject(sheet, chartEntry) returned false, so the chart entry was dropped
        // by AddNormalizedEntries and never restored by any "missing object" fallback -- the
        // normalized order came back as just [Shape], silently losing the chart altogether).
        normalized.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
    }

    [Fact]
    public void GetNormalizedOrder_AppendsChartWithNoRecordedPosition_LikeOtherMissingObjects()
    {
        // Sibling / no-regression: a sheet with no explicit DrawingObjectZOrder at all still
        // normalizes every supported kind -- including a chart -- exactly like the existing
        // "missing object" fallback already does for shapes/pictures/text boxes, instead of
        // throwing or silently omitting the chart from the normalized order.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var shape = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 3))
        };
        sheet.DrawingShapes.Add(shape);
        sheet.Charts.Add(chart);

        var normalized = DrawingObjectZOrder.GetNormalizedOrder(sheet);

        normalized.Should().HaveCount(2);
        normalized.Should().Contain(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, chart.Id));
        normalized.Should().Contain(new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, shape.Id));
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_BringsChartBackwardWithinMixedStackAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var target = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 3))
        };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        sheet.DrawingShapes.Add(back);
        sheet.Charts.Add(target);
        sheet.TextBoxes.Add(front);

        // Default normalized order (no explicit z-order yet) places the chart last: Shape, TextBox,
        // Chart. Send the chart backward one slot: it should swap with the TextBox in front of it.
        var sendBackward = new MoveSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.Chart, target.Id, forward: false);

        sendBackward.Apply(ctx).Success.Should().BeTrue();

        // Pre-fix, Chart routed through Move(sheet.Charts, ...): with only one chart in that list,
        // "backward" computed toIndex = -1 (out of range) and returned a no-op success WITHOUT ever
        // touching sheet.DrawingObjectZOrder, so this would still be empty here.
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, target.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, front.Id));

        sendBackward.Revert(ctx);

        sheet.DrawingObjectZOrder.Should().BeEmpty();
    }

    [Fact]
    public void MoveSelectionPaneObjectCommand_MovesPictureWithinMixedStack_UnaffectedByChartPresence()
    {
        // Sibling / no-regression: adding a chart to the sheet must not disturb the existing
        // Picture/Shape/TextBox Bring-Forward/Send-Backward behaviour (mirrors the pre-existing
        // MoveSelectionPaneObjectCommand_MovesPictureWithinMixedSupportedStackAndUndoRestores case,
        // but with a chart also present in the sheet to prove it doesn't leak into this reorder).
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var back = new DrawingShapeModel { Anchor = new CellAddress(sheet.Id, 1, 1) };
        var middle = new PictureModel { Anchor = new CellAddress(sheet.Id, 1, 2) };
        var front = new TextBoxModel { Anchor = new CellAddress(sheet.Id, 1, 3) };
        var unrelatedChart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2))
        };
        sheet.DrawingShapes.Add(back);
        sheet.Pictures.Add(middle);
        sheet.TextBoxes.Add(front);
        sheet.Charts.Add(unrelatedChart);

        var command = new MoveSelectionPaneObjectCommand(sheet.Id, SelectionPaneObjectKind.Picture, middle.Id, forward: true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.DrawingObjectZOrder.Should().Equal(
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Shape, back.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.TextBox, front.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Picture, middle.Id),
            new DrawingObjectZOrderEntry(SelectionPaneObjectKind.Chart, unrelatedChart.Id));

        command.Revert(ctx);

        sheet.DrawingObjectZOrder.Should().BeEmpty();
    }
}
