using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteSpecialCommandTests
{
    [Fact]
    public void PasteRangeAsPictureCommand_AddsImmutablePictureSnapshotAndUndoRemoves()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), "Q1"),
            (new CellAddress(sheet.Id, 1, 2), "Q2"),
            (new CellAddress(sheet.Id, 2, 1), "10"),
            (new CellAddress(sheet.Id, 2, 2), "20")
        };

        var command = new PasteRangeAsPictureCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            source,
            new CellAddress(sheet.Id, 5, 5));

        command.Apply(ctx).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(new CellAddress(sheet.Id, 5, 5));
        picture.SourceRowCount.Should().Be(2);
        picture.SourceColumnCount.Should().Be(2);
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "20");

        source[3].Item2.Should().Be("20");
        command.Revert(ctx);

        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void PasteRangeAsPictureCommand_LinkedPictureRecordsSourceRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), "Q1"),
            (new CellAddress(sheet.Id, 2, 2), "20")
        };

        var command = new PasteRangeAsPictureCommand(
            sheet.Id,
            sourceRange,
            source,
            new CellAddress(sheet.Id, 5, 5),
            isLinkedToSourceRange: true,
            sourceSheetName: "Sheet1");

        command.Apply(ctx).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.LinkedSourceRange.Should().Be(sourceRange);
        picture.LinkedSourceSheetName.Should().Be("Sheet1");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "20");
    }

    [Fact]
    public void InsertPictureCommand_AddsBinaryImagePictureAndUndoRemoves()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var bytes = new byte[] { 1, 2, 3, 4 };
        var anchor = new CellAddress(sheet.Id, 4, 2);

        var command = new InsertPictureCommand(
            sheet.Id,
            anchor,
            bytes,
            "image/png",
            width: 96,
            height: 72);

        command.Apply(ctx).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(anchor);
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ContentType.Should().Be("image/png");
        picture.ImageBytes.Should().Equal(bytes);
        picture.Width.Should().Be(96);
        picture.Height.Should().Be(72);

        command.Revert(ctx);

        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void InsertPictureCommand_RejectsInvalidInitialSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 4, 2);

        new InsertPictureCommand(sheet.Id, anchor, [1], "image/png", double.NaN, 72)
            .Apply(ctx).Success.Should().BeFalse();
        new InsertPictureCommand(sheet.Id, anchor, [1], "image/png", 96, double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();
        new InsertPictureCommand(sheet.Id, anchor, [1], "image/png", 0, 72)
            .Apply(ctx).Success.Should().BeFalse();

        sheet.Pictures.Should().BeEmpty();
    }

    [Fact]
    public void ClipboardPictureService_CreatesPngPictureCommandUsingImageSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var anchor = new CellAddress(sheet.Id, 2, 3);

        var command = ClipboardPictureService.CreateInsertCommand(
            sheet.Id,
            anchor,
            [5, 6, 7],
            pixelWidth: 320,
            pixelHeight: 180);

        command.Apply(ctx).Success.Should().BeTrue();

        var picture = sheet.Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Should().Be(anchor);
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ContentType.Should().Be("image/png");
        picture.ImageBytes.Should().Equal(5, 6, 7);
        picture.Width.Should().Be(320);
        picture.Height.Should().Be(180);
    }

    [Fact]
    public void ResizePictureCommand_SetsSizeAndUndoRestores()
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
            Width = 100,
            Height = 80
        };
        sheet.Pictures.Add(picture);

        var command = new ResizePictureCommand(sheet.Id, picture.Id, width: 160, height: 90);

        command.Apply(ctx).Success.Should().BeTrue();
        picture.Width.Should().Be(160);
        picture.Height.Should().Be(90);

        command.Revert(ctx);

        picture.Width.Should().Be(100);
        picture.Height.Should().Be(80);
    }

    [Fact]
    public void ResizePictureCommand_RejectsInvalidSize()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            Width = 100,
            Height = 80
        };
        sheet.Pictures.Add(picture);

        new ResizePictureCommand(sheet.Id, picture.Id, double.NaN, 90)
            .Apply(ctx).Success.Should().BeFalse();
        new ResizePictureCommand(sheet.Id, picture.Id, 160, double.PositiveInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        picture.Width.Should().Be(100);
        picture.Height.Should().Be(80);
    }

    [Fact]
    public void RotatePictureCommand_SetsRotationAndUndoRestores()
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
            RotationDegrees = 15
        };
        sheet.Pictures.Add(picture);

        var command = new RotatePictureCommand(sheet.Id, picture.Id, rotationDegrees: 450);

        command.Apply(ctx).Success.Should().BeTrue();
        picture.RotationDegrees.Should().Be(90);

        command.Revert(ctx);

        picture.RotationDegrees.Should().Be(15);
    }

    [Fact]
    public void RotatePictureCommand_RejectsInvalidRotation()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var picture = new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 1, 1),
            RotationDegrees = 15
        };
        sheet.Pictures.Add(picture);

        new RotatePictureCommand(sheet.Id, picture.Id, double.NaN)
            .Apply(ctx).Success.Should().BeFalse();
        new RotatePictureCommand(sheet.Id, picture.Id, double.NegativeInfinity)
            .Apply(ctx).Success.Should().BeFalse();

        picture.RotationDegrees.Should().Be(15);
    }
}
