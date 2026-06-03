using FreeX.Core.Calc;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public partial class ConditionalFormatTests
{
    [Fact]
    public void Formula_Rule_AppliesWhenFormulaIsTrue()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            FormulaText  = "A1>5",   // relative — for row 2 this shifts to A2>5
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        var a1 = GetCell(vp, 1, 1);
        var a2 = GetCell(vp, 2, 1);

        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "A1=10 > 5, formula true");
        a2.Style!.FillColor.Should().NotBe(new CellColor(255, 0, 0), "A2=2, shifted formula A2>5 is false");
    }

    [Fact]
    public void Formula_Rule_AbsoluteRef_SameForAllCells()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(3)));
        // Threshold cell
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(5)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            // Absolute reference — same condition for all cells in range
            FormulaText  = "$A$1>5",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Both cells should be red because $A$1=10 > 5 is always true
        var a1 = GetCell(vp, 1, 1);
        var b1 = GetCell(vp, 1, 2);
        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
        b1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void Formula_Rule_ShiftsRelativeRefsWhileKeepingAbsoluteRefs()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(6)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(8)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(9)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromValue(new NumberValue(5)));

        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>$D$4",
            FormatIfTrue = greenStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "A1=6 is greater than $D$4=5");
        GetCell(vp, 1, 2).Style!.FillColor.Should().NotBe(new CellColor(0, 255, 0), "B1=4 is not greater than $D$4=5");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "A2=8 is greater than $D$4=5");
        GetCell(vp, 2, 2).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "B2=9 is greater than $D$4=5");
    }

    [Fact]
    public void Formula_Rule_ShiftPastSheetBounds_DoesNotMatch()
    {
        var (wb, sheet) = MakeWorkbook();
        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };

        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol), Cell.FromValue(new NumberValue(1)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1),
                new CellAddress(sheet.Id, 1, CellAddress.MaxCol)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "XFD1=1",
            FormatIfTrue = greenStyle
        });

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, CellAddress.MaxCol - 1, 500, 500));

        GetCell(vp, 1, CellAddress.MaxCol - 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0));
        GetCell(vp, 1, CellAddress.MaxCol).Style!.FillColor.Should().NotBe(
            new CellColor(0, 255, 0),
            "shifting XFD1 one column right should become an invalid reference, not an XFE1 lookup");
    }

    [Fact]
    public void Formula_Rule_CurrentRowStructuredReference_UsesCurrentCellAddress()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Flag"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Low"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("High"));

        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Sales",
            DisplayName = "Sales",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            HasAutoFilter = true,
            StyleName = "TableStyleMedium2",
            ShowRowStripes = true
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "Amount"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Flag"));
        sheet.StructuredTables.Add(table);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 3, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "[@Amount]>10",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(198, 239, 206) }
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 2, 2).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 3, 2).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
    }
}
