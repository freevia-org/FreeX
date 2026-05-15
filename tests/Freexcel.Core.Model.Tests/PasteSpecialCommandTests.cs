using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class PasteSpecialCommandTests
{
    [Fact]
    public void PasteSpecialCellsCommand_TransposesCellsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("A"))),
            (new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("B"))),
            (new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("C"))),
            (new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new TextValue("D")))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            source,
            new CellAddress(sheet.Id, 5, 5),
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(5, 5).Should().Be(new TextValue("A"));
        sheet.GetValue(6, 5).Should().Be(new TextValue("B"));
        sheet.GetValue(5, 6).Should().Be(new TextValue("C"));
        sheet.GetValue(6, 6).Should().Be(new TextValue("D"));

        command.Revert(ctx);

        sheet.GetCell(5, 5).Should().BeNull();
        sheet.GetCell(6, 6).Should().BeNull();
    }

    [Fact]
    public void PasteSpecialCellsCommand_AddOperationCombinesNumericValues()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Add));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(new NumberValue(15));
    }

    [Fact]
    public void PasteSpecialCellsCommand_DivideByZeroReturnsError()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var dest = new CellAddress(sheet.Id, 3, 3);
        sheet.SetCell(dest, new NumberValue(10));
        var source = new[]
        {
            (new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)))
        };

        var command = new PasteSpecialCellsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            source,
            dest,
            new PasteSpecialOptions(Operation: PasteSpecialOperation.Divide));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(dest).Should().Be(ErrorValue.DivByZero);
    }

    [Fact]
    public void PasteColumnWidthsCommand_CopiesWidthsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 24;
        sheet.ColumnWidths[5] = 9;

        var command = new PasteColumnWidthsCommand(
            sheet.Id,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            destinationStartCol: 5);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ColumnWidths[5].Should().Be(18);
        sheet.ColumnWidths[6].Should().Be(24);

        command.Revert(ctx);

        sheet.ColumnWidths[5].Should().Be(9);
        sheet.ColumnWidths.Should().NotContainKey(6);
    }

    [Fact]
    public void PasteLinkService_CreatesFormulasReferencingSourceCells()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: new CellAddress(sheetId, 5, 5),
            sourceSheetName: "Sales 2026",
            transpose: false);

        linkedCells.Should().HaveCount(2);
        linkedCells[0].Address.Should().Be(new CellAddress(sheetId, 5, 5));
        linkedCells[0].Cell.FormulaText.Should().Be("'Sales 2026'!A1");
        linkedCells[1].Address.Should().Be(new CellAddress(sheetId, 5, 6));
        linkedCells[1].Cell.FormulaText.Should().Be("'Sales 2026'!B1");
    }

    [Fact]
    public void PasteLinkService_TransposesLinkedCells()
    {
        var sheetId = SheetId.New();
        var sourceRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 2));

        var linkedCells = PasteLinkService.CreateLinkedCells(
            sourceRange,
            destination: new CellAddress(sheetId, 5, 5),
            sourceSheetName: "Sheet1",
            transpose: true);

        linkedCells[0].Address.Should().Be(new CellAddress(sheetId, 5, 5));
        linkedCells[1].Address.Should().Be(new CellAddress(sheetId, 6, 5));
    }

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

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
