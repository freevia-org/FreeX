using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

public sealed partial class PageLayoutCommandTests
{
    [Fact]
    public void SetWorksheetBackgroundCommand_SetsBackgroundAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.BackgroundImage = new WorksheetBackgroundImage([1, 2, 3], "image/png", "old.png");
        var next = new WorksheetBackgroundImage([9, 8, 7], "image/jpeg", "new.jpg");

        var command = new SetWorksheetBackgroundCommand(sheet.Id, next);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().Be(next);

        command.Revert(ctx);

        sheet.BackgroundImage.Should().NotBeNull();
        sheet.BackgroundImage!.ImageBytes.Should().Equal(1, 2, 3);
        sheet.BackgroundImage.ContentType.Should().Be("image/png");
        sheet.BackgroundImage.FileName.Should().Be("old.png");
    }

    [Fact]
    public void ClearWorksheetBackgroundCommand_ClearsBackgroundAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.BackgroundImage = new WorksheetBackgroundImage([1, 2, 3], "image/png", "background.png");

        var command = new ClearWorksheetBackgroundCommand(sheet.Id);

        command.Apply(ctx).Success.Should().BeTrue();
        sheet.BackgroundImage.Should().BeNull();

        command.Revert(ctx);

        sheet.BackgroundImage.Should().NotBeNull();
        sheet.BackgroundImage!.FileName.Should().Be("background.png");
    }
}
