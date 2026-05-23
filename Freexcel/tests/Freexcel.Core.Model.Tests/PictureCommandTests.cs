using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class PictureCommandTests
{
    [Fact]
    public void SetPictureCropCommand_SetsCropAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png",
            CropLeft = 0.1,
            CropTop = 0.2
        };
        sheet.Pictures.Add(picture);

        var command = new SetPictureCropCommand(sheet.Id, picture.Id, 0.15, 0.05, 0.2, 0.1);

        command.Apply(ctx).Success.Should().BeTrue();
        picture.CropLeft.Should().Be(0.15);
        picture.CropTop.Should().Be(0.05);
        picture.CropRight.Should().Be(0.2);
        picture.CropBottom.Should().Be(0.1);

        command.Revert(ctx);

        picture.CropLeft.Should().Be(0.1);
        picture.CropTop.Should().Be(0.2);
        picture.CropRight.Should().Be(0);
        picture.CropBottom.Should().Be(0);
    }

    [Fact]
    public void SetPictureCropCommand_RejectsInvalidCrop()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Kind = PictureKind.Image,
            ImageBytes = [1],
            ContentType = "image/png"
        };
        sheet.Pictures.Add(picture);

        new SetPictureCropCommand(sheet.Id, picture.Id, -0.1, 0, 0, 0).Apply(ctx).Success.Should().BeFalse();
        new SetPictureCropCommand(sheet.Id, picture.Id, 0.6, 0, 0.5, 0).Apply(ctx).Success.Should().BeFalse();
        new SetPictureCropCommand(sheet.Id, picture.Id, 0, 0.7, 0, 0.4).Apply(ctx).Success.Should().BeFalse();
        new SetPictureCropCommand(sheet.Id, picture.Id, double.NaN, 0, 0, 0).Apply(ctx).Success.Should().BeFalse();

        picture.CropLeft.Should().Be(0);
        picture.CropTop.Should().Be(0);
        picture.CropRight.Should().Be(0);
        picture.CropBottom.Should().Be(0);
    }

    [Fact]
    public void SetPictureLockAspectRatioCommand_SetsValueAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            LockAspectRatio = true
        };
        sheet.Pictures.Add(picture);

        var command = new SetPictureLockAspectRatioCommand(sheet.Id, picture.Id, lockAspectRatio: false);

        command.Apply(ctx).Success.Should().BeTrue();
        picture.LockAspectRatio.Should().BeFalse();

        command.Revert(ctx);

        picture.LockAspectRatio.Should().BeTrue();
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
