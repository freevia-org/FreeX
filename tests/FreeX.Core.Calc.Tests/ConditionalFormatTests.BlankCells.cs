using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void Formula_Rule_BlankCellInsideRange_ShowsConditionalFill()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            FormulaText  = "$A1=\"\"",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);

        var a1 = GetCell(vp, 1, 1);
        a1.RawValue.Should().BeOfType<BlankValue>();
        a1.DisplayText.Should().BeEmpty();
        a1.Style!.FillColor.Should().Be(
            new CellColor(255, 0, 0),
            "Excel renders CF fills on fully blank cells matched by =$A1=\"\" formula rules");
    }

    [Fact]
    public void Blanks_Rule_FillsFullyBlankCellsInsideRange()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(7)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),
            Priority     = 1,
            RuleType     = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 255, 0) }
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 255, 0), "A1 has a value");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(255, 255, 0));
        GetCell(vp, 1, 2).Style!.FillColor.Should().Be(new CellColor(255, 255, 0));
        GetCell(vp, 2, 2).Style!.FillColor.Should().Be(new CellColor(255, 255, 0));
    }

    [Fact]
    public void BlankCellsOutsideConditionalRanges_MaterializeNothing()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 255, 0) }
        });

        var vp = GetViewport(wb, sheet);

        vp.Cells.Should().HaveCount(2, "only the blank slots inside the CF range materialize display cells");
        vp.Cells.Should().OnlyContain(c => c.Col == 1 && c.Row <= 2);
    }

    [Fact]
    public void BlankCellInsideRange_WithNoMatchingRule_MaterializesNothing()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            FormulaText  = "$A1<>\"\"",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        });

        var vp = GetViewport(wb, sheet);

        vp.Cells.Should().BeEmpty("no blank cell matches the not-blank formula rule, so nothing materializes");
    }

    [Fact]
    public void Blanks_Rule_FillsBlankCellsInSplitPaneCells()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SplitRow = 3;
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Blanks,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 255, 0) }
        });

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(3, 1, 500, 500));

        var a1 = vp.SplitPanes!.Cells.Single(c => c.Row == 1 && c.Col == 1);
        a1.Style!.FillColor.Should().Be(
            new CellColor(255, 255, 0),
            "blank cells inside CF ranges materialize in the split-pane top rows too");
    }
}
