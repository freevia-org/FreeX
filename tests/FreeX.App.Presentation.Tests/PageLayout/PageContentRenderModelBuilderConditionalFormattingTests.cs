using FluentAssertions;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Tests.Charts;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.PageLayout;

/// <summary>
/// Covers round-43 findings R43-output-surface-consistency-sweep-2 (PDF export never evaluated
/// conditional formatting) and R43-output-surface-consistency-sweep-3 (PDF export never applied the
/// '####' column-too-narrow overflow indicator).
/// </summary>
public sealed class PageContentRenderModelBuilderConditionalFormattingTests
{
    private static readonly FakeTextMeasurer Measurer = new();

    [Fact]
    public void Build_CellValueRuleFillOverridesRawStyleFill()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        // Raw (unconditional) style is plain white -- the CF rule below must win over it.
        var rawStyle = new CellStyle { FillColor = new CellColor(255, 255, 255) };
        var cell = Cell.FromValue(new NumberValue(150));
        cell.StyleId = workbook.RegisterStyle(rawStyle);
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Fill.Should().Be(new PresentationRgb(255, 0, 0), "the matched CF rule's fill must win over the cell's raw white style");
    }

    [Fact]
    public void Build_CellValueRuleDoesNotApplyWhenConditionNotMet()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var rawStyle = new CellStyle { FillColor = new CellColor(10, 20, 30) };
        var cell = Cell.FromValue(new NumberValue(50)); // does not satisfy ">100"
        cell.StyleId = workbook.RegisterStyle(rawStyle);
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Fill.Should().Be(new PresentationRgb(10, 20, 30), "a non-matching CF rule must leave the cell's raw style untouched");
    }

    [Fact]
    public void Build_CellValueRuleAppliesFontColorAndBold()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);

        var rawStyle = new CellStyle { FontColor = new CellColor(0, 0, 0), Bold = false };
        var cell = Cell.FromValue(new NumberValue(200));
        cell.StyleId = workbook.RegisterStyle(rawStyle);
        sheet.SetCell(address, cell);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FontColor = new CellColor(0, 128, 0), Bold = true },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        var font = layout.Cells.Single(c => c.Row == 1 && c.Column == 1).Font;
        font.Color.Should().Be(new PresentationRgb(0, 128, 0));
        font.Bold.Should().BeTrue();
    }

    [Fact]
    public void Build_ColorScaleFillIsInterpolatedFromRangeValues()
    {
        var (workbook, sheet) = CreateWorkbook();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, new NumberValue(0));
        sheet.SetCell(a2, new NumberValue(50));
        sheet.SetCell(a3, new NumberValue(100));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            AppliesTo = new GridRange(a1, a3),
            UseThreeColorScale = false,
            MinColor = new RgbColor(0, 0, 0),
            MaxColor = new RgbColor(255, 255, 255),
            MinThresholdType = CfThresholdType.Min,
            MaxThresholdType = CfThresholdType.Max,
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1).Fill.Should().Be(new PresentationRgb(0, 0, 0));
        layout.Cells.Single(c => c.Row == 3).Fill.Should().Be(new PresentationRgb(255, 255, 255));
        layout.Cells.Single(c => c.Row == 2).Fill.Should().Be(new PresentationRgb(128, 128, 128));
    }

    [Fact]
    public void Build_StopIfTrueSuppressesLowerPriorityRule()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(200));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            Priority = 1,
            StopIfTrue = true,
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            Priority = 2,
            AppliesTo = new GridRange(address, address),
            FormatIfTrue = new CellStyle { FontColor = new CellColor(0, 0, 255) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Fill.Should().Be(new PresentationRgb(255, 0, 0));
        block.Font.Color.Should().Be(new PresentationRgb(0, 0, 0), "the priority-1 Stop-If-True rule must suppress the lower-priority font-color rule entirely");
    }

    [Fact]
    public void Build_NarrowColumnWithOverWideNumberShowsHashOverflowIndicator()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ColumnWidths[1] = 4; // narrow column (character units)
        var cell = Cell.FromValue(new NumberValue(123456789));
        // An explicit (non-General) number format so the "too wide" case hits Excel's '#'
        // overflow indicator instead of General format's own scientific-notation fallback.
        cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0" });
        sheet.SetCell(address, cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Text.Should().MatchRegex("^#+$", "a value too wide for its narrow printed column must show Excel's '#' overflow indicator, not the unclipped number");
    }

    [Fact]
    public void Build_NarrowColumnWithShortNumberDoesNotShowOverflowIndicator()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ColumnWidths[1] = 4; // same narrow column as the overflow test
        var cell = Cell.FromValue(new NumberValue(42));
        cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0" });
        sheet.SetCell(address, cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Text.Should().Be("42", "a value that fits its column's character budget must render normally, not as '#'");
    }

    [Fact]
    public void Build_WideColumnWithLargeNumberDoesNotShowOverflowIndicator()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.ColumnWidths[1] = 40; // plenty of room for a 9-digit number
        var cell = Cell.FromValue(new NumberValue(123456789));
        cell.StyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0" });
        sheet.SetCell(address, cell);

        var layout = BuildFirstPage(workbook, sheet)!;

        var block = layout.Cells.Single(c => c.Row == 1 && c.Column == 1);
        block.Text.Should().Be("123456789");
    }

    private static PageContentLayout? BuildFirstPage(Workbook workbook, Sheet sheet) =>
        PageContentRenderModelBuilder.Build(workbook, sheet, Paginate(sheet), 0, Measurer, new DateTime(2026, 1, 1));

    private static PagePaginationResult Paginate(Sheet sheet)
    {
        var printRange = sheet.PrintArea ?? sheet.GetUsedRange()
            ?? new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        return PagePaginationPlanner.Paginate(
            printRange,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
    }

    private static (Workbook Workbook, Sheet Sheet) CreateWorkbook(string name = "Book1.xlsx")
    {
        var workbook = new Workbook { Name = name };
        var sheet = workbook.AddSheet("Sheet1");
        return (workbook, sheet);
    }
}
