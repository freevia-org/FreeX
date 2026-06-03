using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetPageOrientationCommand_SetsOrientationAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;

        var command = new SetPageOrientationCommand(sheet.Id, WorksheetPageOrientation.Portrait);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);

        command.Revert(ctx);

        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void SetPageOrientationCommand_RejectsInvalidOrientation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;

        var outcome = new SetPageOrientationCommand(sheet.Id, (WorksheetPageOrientation)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
    }

    [Fact]
    public void SetPaperSizeCommand_SetsPaperSizeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PaperSize = WorksheetPaperSize.A4;

        var command = new SetPaperSizeCommand(sheet.Id, WorksheetPaperSize.Legal);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Legal);

        command.Revert(ctx);

        sheet.PaperSize.Should().Be(WorksheetPaperSize.A4);
    }

    [Fact]
    public void SetPaperSizeCommand_RejectsInvalidPaperSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.PaperSize = WorksheetPaperSize.Letter;

        var outcome = new SetPaperSizeCommand(sheet.Id, (WorksheetPaperSize)99).Apply(ctx);

        outcome.Success.Should().BeFalse();
        sheet.PaperSize.Should().Be(WorksheetPaperSize.Letter);
    }
}
