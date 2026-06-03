using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetPrintOptionsCommand_SetsOptionsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PrintGridlines = false;
        sheet.PrintHeadings = true;

        var command = new SetPrintOptionsCommand(sheet.Id, printGridlines: true, printHeadings: false);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintGridlines.Should().BeTrue();
        sheet.PrintHeadings.Should().BeFalse();

        command.Revert(ctx);

        sheet.PrintGridlines.Should().BeFalse();
        sheet.PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void SetScaleToFitCommand_SetsScaleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.ScaleToFit = new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null);
        var next = new WorksheetScaleToFit(ScalePercent: null, FitToPagesWide: 1, FitToPagesTall: 1);

        var command = new SetScaleToFitCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.ScaleToFit.Should().Be(next);

        command.Revert(ctx);

        sheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(ScalePercent: 100, FitToPagesWide: null, FitToPagesTall: null));
    }

    [Fact]
    public void SetPrintTitlesCommand_SetsRowsAndColumnsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 1);

        var command = new SetPrintTitlesCommand(
            sheet.Id,
            rows: new WorksheetRepeatRange(2, 3),
            columns: new WorksheetRepeatRange(1, 2));

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(2, 3));
        sheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 2));

        command.Revert(ctx);

        sheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 1));
        sheet.PrintTitleColumns.Should().BeNull();
    }

    [Fact]
    public void SetPageBreaksCommand_ReplacesManualBreaksAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.RowPageBreaks.Add(10);

        var command = new SetPageBreaksCommand(sheet.Id, rowBreaks: [20, 30], columnBreaks: [4]);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.RowPageBreaks.Should().Equal(20u, 30u);
        sheet.ColumnPageBreaks.Should().Equal(4u);

        command.Revert(ctx);

        sheet.RowPageBreaks.Should().Equal(10u);
        sheet.ColumnPageBreaks.Should().BeEmpty();
    }
}
