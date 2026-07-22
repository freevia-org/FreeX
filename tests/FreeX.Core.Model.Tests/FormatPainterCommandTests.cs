using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class FormatPainterCommandTests
{
    [Fact]
    public void CreateApplyFormatPainterCommand_CopiesAllSourceStylePropertiesWithoutChangingTargetValue()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
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
        var ctx = new TestCommandContext(wb);
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
        var ctx = new TestCommandContext(wb);
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
    public void CreateApplyFormatPainterCommand_CopiesSingleCellValidationToTargetRangeAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var targetTopLeft = new CellAddress(sheet.Id, 4, 2);
        var targetBottomRight = new CellAddress(sheet.Id, 5, 3);
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);
        var oldValidationRange = Range(sheet.Id, 4, 2, 5, 4);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true, FillColor = new CellColor(255, 242, 204) });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AllowBlank = false,
            ErrorTitle = "Numbers only"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = oldValidationRange,
            Type = DvType.List,
            Formula1 = "Old"
        });

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, targetRange);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == targetRange)
            .Which.Should().Match<DataValidation>(rule =>
                rule.Type == DvType.WholeNumber &&
                rule.Operator == DvOperator.Between &&
                rule.Formula1 == "1" &&
                rule.Formula2 == "10" &&
                !rule.AllowBlank &&
                rule.ErrorTitle == "Numbers only");
        foreach (var address in targetRange.AllCells())
        {
            var styleId = sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col);
            styleId.Should().NotBeNull();
            wb.GetStyle(styleId!.Value).Should().Be(wb.GetStyle(sourceStyle));
        }
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == Range(sheet.Id, 4, 4, 5, 4) &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Old");

        command.Revert(ctx);

        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == new GridRange(source, source) &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "1" &&
            rule.Formula2 == "10");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == oldValidationRange &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Old");
        sheet.GetStyleOnly(targetTopLeft.Row, targetTopLeft.Col).Should().BeNull();
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_RebasesRelativeValidationFormulasLikePaste()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 3);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(source, source),
            Type = DvType.Custom,
            Formula1 = "=B1+$C1+B$1+$C$1>0"
        });

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().ContainSingle(rule => rule.AppliesTo == new GridRange(target, target))
            .Which.Formula1.Should().Be("=D3+$C3+D$1+$C$1>0");
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_RepeatsMultiCellSourcePatternAcrossTargetRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
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

    [Fact]
    public void CreateApplyFormatPainterCommand_RepeatsMultiCellValidationPatternAcrossTargetRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var sourceTopLeft = new CellAddress(sheet.Id, 1, 1);
        var sourceBottomRight = new CellAddress(sheet.Id, 2, 2);
        var targetTopLeft = new CellAddress(sheet.Id, 4, 4);
        var targetBottomRight = new CellAddress(sheet.Id, 6, 6);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(sourceTopLeft, sourceTopLeft),
            Type = DvType.List,
            Formula1 = "Red,Blue"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(sourceBottomRight, sourceBottomRight),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9"
        });

        var command = FormatPainterCommandFactory.Create(
            wb,
            sheet,
            new GridRange(sourceTopLeft, sourceBottomRight),
            new GridRange(targetTopLeft, targetBottomRight));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == Range(sheet.Id, 4, 4, 4, 4) &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Red,Blue");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == Range(sheet.Id, 4, 6, 4, 6) &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Red,Blue");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == Range(sheet.Id, 6, 4, 6, 4) &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Red,Blue");
        sheet.DataValidations.Should().Contain(rule =>
            rule.AppliesTo == Range(sheet.Id, 6, 6, 6, 6) &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Red,Blue");
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == Range(sheet.Id, 5, 5, 5, 5) &&
            rule.Type == DvType.WholeNumber &&
            rule.Formula1 == "1" &&
            rule.Formula2 == "9");
    }

    // ---- R68-commands-format-painter-6-1 ----------------------------------------------------
    // Format Painter copies cell style + data validation but not conditional-formatting rules,
    // unlike Excel's Format Painter which also carries CF. Fixed by adding a
    // PasteConditionalFormatsCommand to the composite, mirroring how PasteCommandFactory wires it
    // for a normal "All merging conditional formats" paste.

    [Fact]
    public void CreateApplyFormatPainterCommand_CopiesThreeColorScaleConditionalFormatToTargetRange()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        var targetTopLeft = new CellAddress(sheet.Id, 1, 2); // B1
        var targetBottomRight = new CellAddress(sheet.Id, 5, 2); // B5
        var targetRange = new GridRange(targetTopLeft, targetBottomRight);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(source, new CellAddress(sheet.Id, 5, 1)), // A1:A5
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = true,
            MinColor = new RgbColor(248, 105, 107),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(99, 190, 123)
        });

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, targetRange);

        command.Apply(ctx).Success.Should().BeTrue();

        // Style + data validation copy still works (no regression).
        foreach (var address in targetRange.AllCells())
            wb.GetStyle(sheet.GetCell(address)?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col)!.Value)
                .Bold.Should().BeTrue();

        var pasted = sheet.ConditionalFormats.Should().ContainSingle(rule => rule.AppliesTo == targetRange).Subject;
        pasted.RuleType.Should().Be(CfRuleType.ColorScale);
        pasted.UseThreeColorScale.Should().BeTrue();
        pasted.MinColor.Should().Be(new RgbColor(248, 105, 107));
        pasted.MidColor.Should().Be(new RgbColor(255, 235, 132));
        pasted.MaxColor.Should().Be(new RgbColor(99, 190, 123));
    }

    [Fact]
    public void CreateApplyFormatPainterCommand_SourceCellWithNoConditionalFormat_PaintsNoConditionalFormat_NoRegression()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(source.Row, source.Col, sourceStyle);

        var command = FormatPainterCommandFactory.Create(wb, sheet, source, new GridRange(target, target));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.ConditionalFormats.Should().BeEmpty();
        wb.GetStyle(sheet.GetStyleOnly(target.Row, target.Col)!.Value).Bold.Should().BeTrue();
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheetId, startRow, startCol),
            new CellAddress(sheetId, endRow, endCol));
}
