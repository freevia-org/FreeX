using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

public sealed class ConditionalFormatEvaluationSessionTests
{
    [Fact]
    public void EvaluateEffectiveStyle_PreservesStablePriorityStackingAndBaseStyle()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var value = new NumberValue(5);
        sheet.SetCell(address, value);
        sheet.ConditionalFormats.Add(TrueCellValueRule(address, priority: 1, new CellStyle
        {
            FillColor = new CellColor(10, 20, 30)
        }));
        sheet.ConditionalFormats.Add(TrueCellValueRule(address, priority: 1, new CellStyle
        {
            FillColor = new CellColor(200, 210, 220),
            Bold = true
        }));

        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());
        var style = session.EvaluateEffectiveStyle(address, value, new CellStyle
        {
            Italic = true,
            FontColor = new CellColor(40, 50, 60)
        });

        style.FillColor.Should().Be(new CellColor(10, 20, 30));
        style.Bold.Should().BeTrue();
        style.Italic.Should().BeTrue();
        style.FontColor.Should().Be(new CellColor(40, 50, 60));
    }

    [Fact]
    public void EvaluateEffectiveStyle_StylelessStopIfTrueSuppressesLowerRule()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var value = new NumberValue(5);
        sheet.SetCell(address, value);
        var stopRule = TrueCellValueRule(address, priority: 1, style: null);
        stopRule.StopIfTrue = true;
        sheet.ConditionalFormats.Add(stopRule);
        sheet.ConditionalFormats.Add(TrueCellValueRule(address, priority: 2, new CellStyle { Bold = true }));

        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());

        session.EvaluateEffectiveStyle(address, value).Bold.Should().BeFalse();
    }

    [Fact]
    public void EvaluateEffectiveStyle_UsesCanonicalNamesArraysErrorsAndAdditionalRanges()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var gate = new CellAddress(sheet.Id, 1, 1);
        var primary = new CellAddress(sheet.Id, 1, 2);
        var additional = new CellAddress(sheet.Id, 2, 2);
        var errorTarget = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(gate, new NumberValue(1));
        sheet.SetCell(primary, new TextValue("Primary"));
        sheet.SetCell(additional, new TextValue("Additional"));
        sheet.SetCell(errorTarget, new TextValue("Error"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(4));
        workbook.DefineNamedRange("Gate", new GridRange(gate, gate));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(primary, primary),
            AdditionalRanges = [new GridRange(additional, additional)],
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "AND(Gate,MMULT($C$1:$D$1,$C$2:$C$3)=11)",
            FormatIfTrue = new CellStyle { Bold = true }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(errorTarget, errorTarget),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "1/0",
            FormatIfTrue = new CellStyle { Bold = true }
        });

        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());

        session.EvaluateEffectiveStyle(primary, sheet.GetValue(primary)).Bold.Should().BeTrue();
        session.EvaluateEffectiveStyle(additional, sheet.GetValue(additional)).Bold.Should().BeTrue();
        session.EvaluateEffectiveStyle(errorTarget, sheet.GetValue(errorTarget)).Bold.Should().BeFalse();
    }

    [Fact]
    public void EvaluateEffectiveStyle_ProjectsDxfAndColorScaleOntoBaseStyle()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var low = new CellAddress(sheet.Id, 1, 1);
        var high = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(low, new NumberValue(0));
        sheet.SetCell(high, new NumberValue(100));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(low, low),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThanOrEqual,
            Value1 = "0",
            FormatIfTrue = new CellStyle { Bold = false, DxfBold = false }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(low, high),
            Priority = 2,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(0, 255, 0),
            MaxColor = new RgbColor(255, 0, 0),
            UseThreeColorScale = false
        });

        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());
        var style = session.EvaluateEffectiveStyle(low, sheet.GetValue(low), new CellStyle
        {
            Bold = true,
            Italic = true,
            FontColor = new CellColor(20, 30, 40)
        });

        style.Bold.Should().BeFalse("the dxf explicitly turns bold off");
        style.Italic.Should().BeTrue();
        style.FontColor.Should().Be(new CellColor(20, 30, 40));
        style.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    [Fact]
    public void Constructor_CapturesOneSparseAggregateSnapshotPerSession()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var first = new CellAddress(sheet.Id, 1, 1);
        var last = new CellAddress(sheet.Id, CellAddress.MaxRow, 1);
        sheet.SetCell(first, new NumberValue(1));
        sheet.SetCell(last, new NumberValue(10));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, last),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            AboveAverage = true,
            FormatIfTrue = new CellStyle { Bold = true }
        });
        var occupiedCells = sheet.GetOccupiedCellMap();
        var originalSession = new ConditionalFormatEvaluationSession(sheet, workbook, occupiedCells);

        sheet.SetCell(first, new NumberValue(100));

        originalSession.EvaluateEffectiveStyle(first, sheet.GetValue(first)).Bold.Should().BeFalse();
        var refreshedSession = new ConditionalFormatEvaluationSession(sheet, workbook, occupiedCells);
        refreshedSession.EvaluateEffectiveStyle(first, sheet.GetValue(first)).Bold.Should().BeTrue();
    }

    private static ConditionalFormat TrueCellValueRule(
        CellAddress address,
        int priority,
        CellStyle? style) =>
        new()
        {
            AppliesTo = new GridRange(address, address),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = style
        };
}
