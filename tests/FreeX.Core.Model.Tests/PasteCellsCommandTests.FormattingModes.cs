using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PasteCellsCommandTests
{
    [Fact]
    public void PasteCommandFactory_TransposedFormatsModeAppliesStylesWithoutChangingContent()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var destinationStart = new CellAddress(sheet.Id, 3, 3);
        var secondDestination = new CellAddress(sheet.Id, 4, 3);
        var firstSourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var secondSourceStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var firstDestinationStyle = wb.RegisterStyle(new CellStyle { Underline = true });
        var secondDestinationStyle = wb.RegisterStyle(new CellStyle { Strikethrough = true });
        var firstSourceCell = Cell.FromValue(new TextValue("source 1"));
        firstSourceCell.StyleId = firstSourceStyle;
        var secondSourceCell = Cell.FromValue(new TextValue("source 2"));
        secondSourceCell.StyleId = secondSourceStyle;
        sheet.SetCell(sourceStart, firstSourceCell);
        sheet.SetCell(sourceEnd, secondSourceCell);
        var firstDestinationCell = Cell.FromValue(new TextValue("keep 1"));
        firstDestinationCell.StyleId = firstDestinationStyle;
        sheet.SetCell(destinationStart, firstDestinationCell);
        var secondDestinationCell = Cell.FromFormula("A1+1");
        secondDestinationCell.Value = new NumberValue(7);
        secondDestinationCell.StyleId = secondDestinationStyle;
        sheet.SetCell(secondDestination, secondDestinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, firstSourceCell.Clone()), (sourceEnd, secondSourceCell.Clone())],
            destinationStart,
            PasteCellsMode.Formats,
            new PasteSpecialOptions(Transpose: true));

        command.Apply(ctx).Success.Should().BeTrue();

        var firstPasted = sheet.GetCell(destinationStart)!;
        firstPasted.Value.Should().Be(new TextValue("keep 1"));
        firstPasted.FormulaText.Should().BeNull();
        firstPasted.StyleId.Should().Be(firstSourceStyle);
        var secondPasted = sheet.GetCell(secondDestination)!;
        secondPasted.FormulaText.Should().Be("A1+1");
        secondPasted.Value.Should().Be(new NumberValue(7));
        secondPasted.StyleId.Should().Be(secondSourceStyle);
    }

    [Fact]
    public void PasteCommandFactory_AllModeCopiesSourceStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle { Bold = true });
        var destinationStyle = wb.RegisterStyle(new CellStyle { Italic = true });
        var sourceCell = Cell.FromValue(new TextValue("new"));
        sourceCell.StyleId = sourceStyle;
        sheet.SetCell(source, sourceCell);
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.Value.Should().Be(new TextValue("new"));
        pasted.StyleId.Should().Be(sourceStyle);
    }

    [Fact]
    public void PasteCommandFactory_SkipBlanksLeavesDestinationCellsUnchanged()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 1, 2);
        var blankSource = Cell.FromValue(BlankValue.Instance);
        var valueSource = Cell.FromValue(new TextValue("new"));
        var destinationStart = new CellAddress(sheet.Id, 3, 1);
        var destinationBlankSlot = Cell.FromValue(new TextValue("keep"));
        var destinationValueSlot = Cell.FromValue(new TextValue("old"));
        sheet.SetCell(sourceStart, blankSource);
        sheet.SetCell(sourceEnd, valueSource);
        sheet.SetCell(destinationStart, destinationBlankSlot);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), destinationValueSlot);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, blankSource.Clone()), (sourceEnd, valueSource.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(SkipBlanks: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destinationStart).Should().Be(new TextValue("keep"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new TextValue("new"));
    }

    [Fact]
    public void PasteCommandFactory_AllExceptBordersCopiesContentAndNonBorderStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(240, 248, 255),
            BorderLeft = new CellBorder(BorderStyle.Thick, new CellColor(255, 0, 0)),
            NumberFormat = "$#,##0.00"
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Italic = true,
            BorderLeft = new CellBorder(BorderStyle.Double, new CellColor(0, 0, 255)),
            BorderRight = new CellBorder(BorderStyle.Dashed, new CellColor(0, 255, 0))
        });
        var sourceCell = Cell.FromValue(new NumberValue(42));
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllExceptBorders));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.Value.Should().Be(new NumberValue(42));
        var style = wb.GetStyle(pasted.StyleId);
        style.Bold.Should().BeTrue();
        style.FillColor.Should().Be(new CellColor(240, 248, 255));
        style.NumberFormat.Should().Be("$#,##0.00");
        style.BorderLeft.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(0, 0, 255)));
        style.BorderRight.Should().Be(new CellBorder(BorderStyle.Dashed, new CellColor(0, 255, 0)));
    }

    [Fact]
    public void PasteCommandFactory_ValuesAndNumberFormatsCopiesValueAndNumberFormatOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "0.00%"
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(255, 255, 0),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
            NumberFormat = "General"
        });
        var sourceCell = Cell.FromFormula("B1+1");
        sourceCell.Value = new NumberValue(0.25);
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(0.25));
        var style = wb.GetStyle(pasted.StyleId);
        style.NumberFormat.Should().Be("0.00%");
        style.Italic.Should().BeTrue();
        style.FillColor.Should().Be(new CellColor(255, 255, 0));
        style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)));
        style.Bold.Should().BeFalse();
    }

    [Fact]
    public void PasteCommandFactory_ValuesAndNumberFormatsWithAddCopiesNumberFormatOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "0.00%"
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(255, 255, 0),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
            NumberFormat = "$#,##0"
        });
        var sourceCell = Cell.FromValue(new NumberValue(0.25));
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new NumberValue(2));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(
                Operation: PasteSpecialOperation.Add,
                ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(2.25));
        var style = wb.GetStyle(pasted.StyleId);
        style.NumberFormat.Should().Be("0.00%");
        style.Italic.Should().BeTrue();
        style.FillColor.Should().Be(new CellColor(255, 255, 0));
        style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)));
        style.Bold.Should().BeFalse();
    }

    [Fact]
    public void PasteCommandFactory_ValuesAndSourceFormattingCopiesValueAndFullSourceStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 2, 1);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            FillColor = new CellColor(12, 34, 56),
            NumberFormat = "0.00%",
            BorderBottom = new CellBorder(BorderStyle.Double, new CellColor(1, 2, 3))
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(255, 255, 0),
            NumberFormat = "General"
        });
        var sourceCell = Cell.FromFormula("B1+1");
        sourceCell.Value = new NumberValue(0.25);
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndSourceFormatting));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().BeNull();
        pasted.Value.Should().Be(new NumberValue(0.25));
        var style = wb.GetStyle(pasted.StyleId);
        style.Bold.Should().BeTrue();
        style.Italic.Should().BeFalse();
        style.FillColor.Should().Be(new CellColor(12, 34, 56));
        style.NumberFormat.Should().Be("0.00%");
        style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Double, new CellColor(1, 2, 3)));
    }

    [Fact]
    public void PasteCommandFactory_FormulasAndNumberFormatsRebasesFormulaAndCopiesNumberFormatOnly()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 3, 2);
        var sourceStyle = wb.RegisterStyle(new CellStyle
        {
            Bold = true,
            NumberFormat = "$#,##0"
        });
        var destinationStyle = wb.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(200, 220, 255),
            NumberFormat = "General"
        });
        var sourceCell = Cell.FromFormula("B1+$C$1");
        sourceCell.StyleId = sourceStyle;
        var destinationCell = Cell.FromValue(new TextValue("old"));
        destinationCell.StyleId = destinationStyle;
        sheet.SetCell(source, sourceCell);
        sheet.SetCell(destination, destinationCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sourceCell.Clone())],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.FormulasAndNumberFormats));

        command.Apply(ctx).Success.Should().BeTrue();

        var pasted = sheet.GetCell(destination)!;
        pasted.FormulaText.Should().Be("C3+$C$1");
        var style = wb.GetStyle(pasted.StyleId);
        style.NumberFormat.Should().Be("$#,##0");
        style.Italic.Should().BeTrue();
        style.FillColor.Should().Be(new CellColor(200, 220, 255));
        style.Bold.Should().BeFalse();
    }

    [Fact]
    public void PasteCommandFactory_AllMergingConditionalFormatsCopiesContentAndAddsShiftedRules()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var sourceStart = new CellAddress(sheet.Id, 1, 1);
        var sourceEnd = new CellAddress(sheet.Id, 2, 1);
        var destinationStart = new CellAddress(sheet.Id, 4, 3);
        var destinationEnd = new CellAddress(sheet.Id, 5, 3);
        var existingRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 10, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.LessThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Italic = true },
            Priority = 1
        };
        var sourceRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(sourceStart, sourceEnd),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { Bold = true },
            Priority = 2
        };
        sheet.ConditionalFormats.Add(existingRule);
        sheet.ConditionalFormats.Add(sourceRule);

        var sourceCell = Cell.FromValue(new NumberValue(42));
        sheet.SetCell(sourceStart, sourceCell);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(sourceStart, sourceEnd),
            [(sourceStart, sourceCell.Clone())],
            destinationStart,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.AllMergingConditionalFormats));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.GetValue(destinationStart).Should().Be(new NumberValue(42));
        sheet.ConditionalFormats.Should().HaveCount(3);
        sheet.ConditionalFormats.Should().Contain(existingRule);
        var pastedRule = sheet.ConditionalFormats.Single(rule => rule.Id != existingRule.Id && rule.Id != sourceRule.Id);
        pastedRule.AppliesTo.Should().Be(new GridRange(destinationStart, destinationEnd));
        pastedRule.RuleType.Should().Be(CfRuleType.CellValue);
        pastedRule.Operator.Should().Be(CfOperator.GreaterThan);
        pastedRule.Value1.Should().Be("10");
        pastedRule.FormatIfTrue!.Bold.Should().BeTrue();

        command.Revert(ctx);

        sheet.GetCell(destinationStart).Should().BeNull();
        sheet.ConditionalFormats.Should().Equal(existingRule, sourceRule);
    }
}
