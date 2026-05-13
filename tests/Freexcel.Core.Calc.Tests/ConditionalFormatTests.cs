using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class ConditionalFormatTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sh = wb.AddSheet("Sheet1");
        return (wb, sh);
    }

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet)
    {
        var svc = new ViewportService();
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
    }

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    // ─── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void CellValue_GreaterThan_AppliesFormatToMatchingCells()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo   = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority    = 1,
            RuleType    = CfRuleType.CellValue,
            Operator    = CfOperator.GreaterThan,
            Value1      = "3",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        var a2 = GetCell(vp, 2, 1);

        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "A1=5 > 3, so red fill should apply");
        a2.Style!.FillColor.Should().NotBe(new CellColor(255, 0, 0), "A2=2 is not > 3, so red fill should NOT apply");
    }

    [Fact]
    public void CellValue_Between_AppliesOnlyWhenInRange()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.Between,
            Value1       = "1",
            Value2       = "10",
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.Bold.Should().BeTrue("A1=5 is between 1 and 10");
    }

    [Fact]
    public void ColorScale_InterpolatesColorForMidRangeValue()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority  = 1,
            RuleType  = CfRuleType.ColorScale,
            MinColor  = new RgbColor(0, 255, 0),    // green
            MaxColor  = new RgbColor(255, 0, 0),    // red
            UseThreeColorScale = false
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert: mid-range cell (50 out of 0–100) should have roughly yellow (~128, ~128, 0)
        var a2 = GetCell(vp, 2, 1);
        a2.Style!.FillColor.Should().NotBeNull("color scale should set a fill");
        var fill = a2.Style!.FillColor!.Value;
        // Interpolation: R = 0 + 0.5*(255-0) = 127, G = 255 + 0.5*(0-255) = 127, B = 0
        fill.R.Should().BeCloseTo(127, 2, "R interpolated from 0→255 at t=0.5");
        fill.G.Should().BeCloseTo(127, 2, "G interpolated from 255→0 at t=0.5");
    }

    [Fact]
    public void MergeStyles_CfBoldOverridesBaseStyle()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Bold = false, FillColor = new CellColor(200, 200, 200) };
        var styleId   = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(99));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.Bold.Should().BeTrue("CF bold overrides base non-bold");
        // Base fill should be preserved (CF style has no fill)
        a1.Style!.FillColor.Should().Be(new CellColor(200, 200, 200), "base fill preserved when CF has none");
    }

    [Fact]
    public void ApplyConditionalFormatCommand_Revert_RemovesRule()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();

        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ApplyConditionalFormatCommand(sheet.Id, cf);

        // Apply
        bus.Execute(wb.Id, cmd);
        sheet.ConditionalFormats.Should().HaveCount(1);

        // Undo (revert)
        bus.Undo(wb.Id);
        sheet.ConditionalFormats.Should().BeEmpty("revert should remove the rule");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    private sealed class TestCommandContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id)!;
    }
}
