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

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
