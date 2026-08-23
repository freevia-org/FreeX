using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void FormulaRule_NonzeroDateResultMatches()
    {
        var (workbook, sheet) = MakeWorkbook();
        var source = new CellAddress(sheet.Id, 1, 1);
        var target = new CellAddress(sheet.Id, 1, 2);
        var fill = new CellColor(12, 34, 56);
        sheet.SetCell(source, DateTimeValue.FromDateTime(new DateTime(2024, 1, 2)));
        sheet.SetCell(target, new TextValue("Date"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(target, target),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "$A$1",
            FormatIfTrue = new CellStyle { FillColor = fill }
        });

        GetCell(GetViewport(workbook, sheet), 1, 2).Style!.FillColor.Should().Be(fill);
    }

    [Fact]
    public void FormulaRule_ScalarizesOneCellComputedArrayResult()
    {
        var (workbook, sheet) = MakeWorkbook();
        var target = new CellAddress(sheet.Id, 1, 2);
        var fill = new CellColor(12, 34, 56);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(4));
        sheet.SetCell(target, new TextValue("Matrix"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(target, target),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "MMULT($C$1:$D$1,$C$2:$C$3)=11",
            FormatIfTrue = new CellStyle { FillColor = fill }
        });

        GetCell(GetViewport(workbook, sheet), 1, 2).Style!.FillColor.Should().Be(fill);
    }

    [Fact]
    public void FormulaRule_MultiCellComputedArrayStillFailsClosed()
    {
        var (workbook, sheet) = MakeWorkbook();
        var target = new CellAddress(sheet.Id, 1, 2);
        var fill = new CellColor(12, 34, 56);
        sheet.SetCell(target, new TextValue("Array"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(target, target),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "SEQUENCE(2)",
            FormatIfTrue = new CellStyle { FillColor = fill }
        });

        GetCell(GetViewport(workbook, sheet), 1, 2).Style!.FillColor.Should().NotBe(fill);
    }
}
