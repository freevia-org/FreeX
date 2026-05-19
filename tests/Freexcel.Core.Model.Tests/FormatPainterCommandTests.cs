using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FormatPainterCommandTests
{
    [Fact]
    public void CreateApplyFormatPainterCommand_CopiesAllSourceStylePropertiesWithoutChangingTargetValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            Italic = true,
            FontName = "Aptos Display",
            FontSize = 14,
            FontColor = new CellColor(192, 0, 0),
            FillColor = new CellColor(255, 242, 204),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            WrapText = true,
            NumberFormat = "$#,##0.00",
            BorderBottom = new CellBorder(BorderStyle.Thick, CellColor.Black)
        });
        var sourceCell = Cell.FromValue(new TextValue("source"));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(target, new NumberValue(123));

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(target)!.Value.Should().Be(new NumberValue(123));
        wb.GetStyle(sheet.GetCell(target)!.StyleId).Should().Be(wb.GetStyle(sourceStyle));
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_CopiesStyleOnlySourceFormattingLikeExcel()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 4, 4);
        var target = new CellAddress(sheet.Id, 6, 4);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            FillColor = new CellColor(198, 239, 206),
            FontColor = new CellColor(0, 97, 0),
            Bold = true
        });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetCell(target).Should().BeNull("format painter should not materialize an empty destination cell");
        var targetStyleOnly = sheet.GetStyleOnly(target.Row, target.Col);
        targetStyleOnly.Should().NotBeNull();
        wb.GetStyle(targetStyleOnly!.Value).Should().Be(wb.GetStyle(sourceStyle));
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_AppliesSourceFormatAcrossTargetRangeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var topLeft = new CellAddress(sheet.Id, 2, 2);
        var bottomRight = new CellAddress(sheet.Id, 3, 3);
        var oldStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 255, 0) });
        var targetCell = Cell.FromValue(new TextValue("keep"));
        targetCell.StyleId = oldStyle;
        sheet.SetCell(topLeft, targetCell);
        var sourceCell = Cell.FromValue(new NumberValue(1));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(topLeft, bottomRight));

        command.Apply(ctx).Success.Should().BeTrue();

        foreach (var address in new GridRange(topLeft, bottomRight).AllCells())
        {
            var styleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col);
            styleId.Should().NotBeNull();
            wb.GetStyle(styleId!.Value).Should().Be(wb.GetStyle(sourceStyle));
        }

        command.Revert(ctx);

        sheet.GetCell(topLeft)!.StyleId.Should().Be(oldStyle);
        sheet.GetStyleOnly(2, 3).Should().BeNull();
        sheet.GetStyleOnly(3, 2).Should().BeNull();
        sheet.GetStyleOnly(3, 3).Should().BeNull();
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_RepeatsMultiCellSourcePatternAcrossTargetRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceTopLeft = new CellAddress(sheet.Id, 1, 1);
        var sourceBottomRight = new CellAddress(sheet.Id, 2, 2);
        var targetTopLeft = new CellAddress(sheet.Id, 4, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 6, 6);
        var red = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 199, 206) });
        var green = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(198, 239, 206) });
        var blue = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(189, 215, 238) });
        var yellow = wb.RegisterStyle(new CellStyle { FillColor = new CellColor(255, 235, 156) });
        sheet.SetStyleOnly(1, 1, red);
        sheet.SetStyleOnly(1, 2, green);
        sheet.SetStyleOnly(2, 1, blue);
        sheet.SetStyleOnly(2, 2, yellow);

        var command = FormatPainterCommandFactory.Create(
            wb,
            sheet,
            new GridRange(sourceTopLeft, sourceBottomRight),
            new GridRange(targetTopLeft, targetBottomRight));

        command.Apply(ctx).Success.Should().BeTrue();

        StyleId StyleAt(uint row, uint col) =>
            sheet.GetCell(new CellAddress(sheet.Id, row, col))?.StyleId
            ?? sheet.GetStyleOnly(row, col)
            ?? StyleId.Default;

        StyleAt(4, 4).Should().Be(red);
        StyleAt(4, 5).Should().Be(green);
        StyleAt(4, 6).Should().Be(red);
        StyleAt(5, 4).Should().Be(blue);
        StyleAt(5, 5).Should().Be(yellow);
        StyleAt(5, 6).Should().Be(blue);
        StyleAt(6, 4).Should().Be(red);
        StyleAt(6, 5).Should().Be(green);
        StyleAt(6, 6).Should().Be(red);
    }

    private sealed class SimpleCtx(Workbook wb) : ICommandContext
    {
        public Workbook Workbook { get; } = wb;
        public Sheet GetSheet(SheetId id) => Workbook.GetSheet(id)!;
    }
}
