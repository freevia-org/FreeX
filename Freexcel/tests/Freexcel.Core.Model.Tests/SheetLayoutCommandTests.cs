using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class SheetLayoutCommandTests
{
    private static (Workbook wb, Sheet sheet, ICommandContext ctx) Setup()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        return (wb, sheet, new SimpleCtx(wb));
    }

    [Fact]
    public void AutoFitSizingService_ColumnWidthGrowsForLongDisplayText()
    {
        var width = AutoFitSizingService.EstimateColumnWidth(["short", "a much longer display value"], defaultWidth: 8.43);

        width.Should().BeGreaterThan(8.43);
    }

    [Fact]
    public void AutoFitSizingService_RowHeightGrowsForMultilineDisplayText()
    {
        var height = AutoFitSizingService.EstimateRowHeight(["first line\nsecond line\nthird line"], defaultHeight: 20);

        height.Should().BeGreaterThan(20);
    }

    [Fact]
    public void AutoFitSizingService_ClampsColumnWidthToBounds()
    {
        AutoFitSizingService.EstimateColumnWidth(["x"], defaultWidth: 8)
            .Should().Be(24);

        AutoFitSizingService.EstimateColumnWidth([new string('x', 1_000)], defaultWidth: 8)
            .Should().Be(300);
    }

    [Fact]
    public void AutoFitSizingService_ClampsRowHeightToBounds()
    {
        AutoFitSizingService.EstimateRowHeight([""], defaultHeight: 8)
            .Should().Be(16);

        AutoFitSizingService.EstimateRowHeight([string.Join('\n', Enumerable.Repeat("line", 50))], defaultHeight: 20)
            .Should().Be(220);
    }

    [Fact]
    public void AutoFitEstimatedRowHeightCommand_UndoRestoresPreviousOverrides()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[1] = 19;

        var height = AutoFitSizingService.EstimateRowHeight(["first\nsecond"], sheet.DefaultRowHeight);
        var cmd = new SetRowHeightCommand(sheet.Id, 1, 1, height);

        cmd.Apply(ctx).Success.Should().BeTrue();
        sheet.RowHeights[1].Should().Be(height);

        cmd.Revert(ctx);

        sheet.RowHeights[1].Should().Be(19);
    }

    [Fact]
    public void AutoFitEstimatedColumnWidthCommand_UndoRestoresPreviousOverrides()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[1] = 12;

        var width = AutoFitSizingService.EstimateColumnWidth(["a much longer display value"], sheet.DefaultColumnWidth);
        var cmd = new SetColumnWidthCommand(sheet.Id, 1, 1, width);

        cmd.Apply(ctx).Success.Should().BeTrue();
        sheet.ColumnWidths[1].Should().Be(width);

        cmd.Revert(ctx);

        sheet.ColumnWidths[1].Should().Be(12);
    }

    [Fact]
    public void SetRowHeightCommand_SetsHeightForRowsAndUndoRestoresPreviousOverrides()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[2] = 18;
        sheet.RowHeights[4] = 24;

        var cmd = new SetRowHeightCommand(sheet.Id, 2, 4, 30);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowHeights[2].Should().Be(30);
        sheet.RowHeights[3].Should().Be(30);
        sheet.RowHeights[4].Should().Be(30);

        cmd.Revert(ctx);

        sheet.RowHeights[2].Should().Be(18);
        sheet.RowHeights.Should().NotContainKey(3);
        sheet.RowHeights[4].Should().Be(24);
    }

    [Fact]
    public void SetRowHeightCommand_WithNullHeightClearsOverridesAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.RowHeights[1] = 19;
        sheet.RowHeights[2] = 21;

        var cmd = new SetRowHeightCommand(sheet.Id, 1, 2, height: null);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.RowHeights.Should().NotContainKey(1);
        sheet.RowHeights.Should().NotContainKey(2);

        cmd.Revert(ctx);

        sheet.RowHeights[1].Should().Be(19);
        sheet.RowHeights[2].Should().Be(21);
    }

    [Fact]
    public void SetColumnWidthCommand_SetsWidthForColumnsAndUndoRestoresPreviousOverrides()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ColumnWidths[1] = 10;
        sheet.ColumnWidths[3] = 16;

        var cmd = new SetColumnWidthCommand(sheet.Id, 1, 3, 22);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.ColumnWidths[1].Should().Be(22);
        sheet.ColumnWidths[2].Should().Be(22);
        sheet.ColumnWidths[3].Should().Be(22);

        cmd.Revert(ctx);

        sheet.ColumnWidths[1].Should().Be(10);
        sheet.ColumnWidths.Should().NotContainKey(2);
        sheet.ColumnWidths[3].Should().Be(16);
    }

    [Fact]
    public void SetColumnWidthCommand_RejectsNonPositiveWidth()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = new SetColumnWidthCommand(sheet.Id, 1, 1, 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("positive");
        sheet.ColumnWidths.Should().BeEmpty();
    }

    [Fact]
    public void SetRowHeightCommand_RejectsNonFiniteHeight()
    {
        var (_, sheet, ctx) = Setup();

        new SetRowHeightCommand(sheet.Id, 1, 1, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new SetRowHeightCommand(sheet.Id, 1, 1, double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.RowHeights.Should().BeEmpty();
    }

    [Fact]
    public void SetColumnWidthCommand_RejectsNonFiniteWidth()
    {
        var (_, sheet, ctx) = Setup();

        new SetColumnWidthCommand(sheet.Id, 1, 1, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new SetColumnWidthCommand(sheet.Id, 1, 1, double.NegativeInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.ColumnWidths.Should().BeEmpty();
    }

    [Fact]
    public void SetRowsHiddenCommand_HidesAndUnhidesRowsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenRows.Add(1);
        sheet.HiddenRows.Add(3);

        var hide = new SetRowsHiddenCommand(sheet.Id, 2, 4, hidden: true);
        hide.Apply(ctx).Success.Should().BeTrue();

        sheet.HiddenRows.Should().BeEquivalentTo(new[] { 1u, 2u, 3u, 4u });

        hide.Revert(ctx);
        sheet.HiddenRows.Should().BeEquivalentTo(new[] { 1u, 3u });

        var unhide = new SetRowsHiddenCommand(sheet.Id, 1, 3, hidden: false);
        unhide.Apply(ctx).Success.Should().BeTrue();

        sheet.HiddenRows.Should().BeEmpty();

        unhide.Revert(ctx);
        sheet.HiddenRows.Should().BeEquivalentTo(new[] { 1u, 3u });
    }

    [Fact]
    public void SetColumnsHiddenCommand_HidesAndUnhidesColumnsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.HiddenCols.Add(2);
        sheet.HiddenCols.Add(5);

        var hide = new SetColumnsHiddenCommand(sheet.Id, 3, 5, hidden: true);
        hide.Apply(ctx).Success.Should().BeTrue();

        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 3u, 4u, 5u });

        hide.Revert(ctx);
        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 5u });

        var unhide = new SetColumnsHiddenCommand(sheet.Id, 2, 5, hidden: false);
        unhide.Apply(ctx).Success.Should().BeTrue();

        sheet.HiddenCols.Should().BeEmpty();

        unhide.Revert(ctx);
        sheet.HiddenCols.Should().BeEquivalentTo(new[] { 2u, 5u });
    }

    [Fact]
    public void SetWorksheetViewModeCommand_SetsModeAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

        var command = new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.PageLayout);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ViewMode.Should().Be(WorksheetViewMode.PageLayout);

        command.Revert(ctx);

        sheet.ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
    }

    [Fact]
    public void SetWorksheetViewModeCommand_RejectsInvalidMode()
    {
        var (_, sheet, ctx) = Setup();

        var outcome = new SetWorksheetViewModeCommand(sheet.Id, (WorksheetViewMode)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ViewMode.Should().Be(WorksheetViewMode.Normal);
    }

    [Fact]
    public void SetWorksheetViewOptionsCommand_SetsGridlinesAndHeadingsAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ShowGridlines = true;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = true;

        var command = new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: false, showHeadings: true, showRulers: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowGridlines.Should().BeFalse();
        sheet.ShowHeadings.Should().BeTrue();
        sheet.ShowRulers.Should().BeFalse();

        command.Revert(ctx);

        sheet.ShowGridlines.Should().BeTrue();
        sheet.ShowHeadings.Should().BeFalse();
        sheet.ShowRulers.Should().BeTrue();
    }

    [Fact]
    public void SetWorksheetZoomCommand_SetsZoomAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ZoomPercent = 75;

        var command = new SetWorksheetZoomCommand(sheet.Id, 125);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ZoomPercent.Should().Be(125);

        command.Revert(ctx);

        sheet.ZoomPercent.Should().Be(75);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(401)]
    public void SetWorksheetZoomCommand_RejectsUnsupportedZoom(int zoomPercent)
    {
        var (_, sheet, ctx) = Setup();
        sheet.ZoomPercent = 100;

        var outcome = new SetWorksheetZoomCommand(sheet.Id, zoomPercent).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.ZoomPercent.Should().Be(100);
    }

    [Fact]
    public void SetWorksheetShowFormulasCommand_SetsStateAndUndoRestores()
    {
        var (_, sheet, ctx) = Setup();
        sheet.ShowFormulas = false;

        var command = new SetWorksheetShowFormulasCommand(sheet.Id, true);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ShowFormulas.Should().BeTrue();

        command.Revert(ctx);

        sheet.ShowFormulas.Should().BeFalse();
    }

    [Fact]
    public void SetFreezePanesCommand_ClearsExistingSplitPaneAndUndoRestoresIt()
    {
        var (_, sheet, ctx) = Setup();
        sheet.SplitRow = 12;
        sheet.SplitColumn = 4;

        var command = new SetFreezePanesCommand(sheet.Id, 1, 2);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(1);
        sheet.FrozenCols.Should().Be(2);
        sheet.SplitRow.Should().BeNull();
        sheet.SplitColumn.Should().BeNull();

        command.Revert(ctx);

        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        sheet.SplitRow.Should().Be(12);
        sheet.SplitColumn.Should().Be(4);
    }

    [Fact]
    public void SetFreezePanesCommand_UnfreezeDoesNotClearExistingSplitPane()
    {
        var (_, sheet, ctx) = Setup();
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 2;
        sheet.SplitRow = 12;
        sheet.SplitColumn = 4;

        var command = new SetFreezePanesCommand(sheet.Id, 0, 0);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.FrozenRows.Should().Be(0);
        sheet.FrozenCols.Should().Be(0);
        sheet.SplitRow.Should().Be(12);
        sheet.SplitColumn.Should().Be(4);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
