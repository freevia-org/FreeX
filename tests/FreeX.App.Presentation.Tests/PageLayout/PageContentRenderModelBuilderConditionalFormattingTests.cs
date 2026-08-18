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

    // R140 cf-print-pdf-rule-type-gap: Formula, Top10, DuplicateValues, and Blanks previously fell
    // into ConditionalFormatRenderEvaluator's `default: return false` branch, so print preview / PDF
    // export silently dropped these rule types even though the screen grid applied them correctly.
    // These tests go through the real production print/PDF planner entry point
    // (PageContentRenderModelBuilder.Build, the same call PrintPreviewInstructionBuilder and PDF
    // export use), not the evaluator in isolation, and all four would have failed before the fix
    // (every matched cell's Fill would have stayed the raw/default color instead of the CF fill).
    [Fact]
    public void Build_FormulaRuleAppliesStyleAndStopIfTrueSuppressesLowerPriorityRule()
    {
        var (workbook, sheet) = CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(150));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.Formula,
            FormulaText = "$A$1>100",
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
        block.Fill.Should().Be(new PresentationRgb(255, 0, 0), "the matched Formula rule's fill must reach print/PDF, not just the screen grid");
        block.Font.Color.Should().Be(new PresentationRgb(0, 0, 0), "the priority-1 Stop-If-True Formula rule must suppress the lower-priority CellValue rule in print, exactly as on screen");
    }

    [Fact]
    public void Build_Top10RuleAppliesStyleOnlyToTopRankedCell()
    {
        var (workbook, sheet) = CreateWorkbook();
        var low = new CellAddress(sheet.Id, 1, 1);
        var mid = new CellAddress(sheet.Id, 2, 1);
        var top = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(low, new NumberValue(10));
        sheet.SetCell(mid, new NumberValue(20));
        sheet.SetCell(top, new NumberValue(30));
        var range = new GridRange(low, top);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            TopBottomPercent = false,
            AboveAverage = true, // top (not bottom) N
            AppliesTo = range,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 3).Fill.Should().Be(new PresentationRgb(255, 0, 0),
            "the single top-ranked cell must be highlighted in print/PDF");
        layout.Cells.Single(c => c.Row == 1).Fill.Should().NotBe(new PresentationRgb(255, 0, 0),
            "a cell outside the top-1 ranking must not be highlighted");
    }

    [Fact]
    public void Build_DuplicateValuesRuleAppliesStyleOnlyToDuplicatedCells()
    {
        var (workbook, sheet) = CreateWorkbook();
        var first = new CellAddress(sheet.Id, 1, 1);
        var duplicate = new CellAddress(sheet.Id, 2, 1);
        var unique = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new NumberValue(5));
        sheet.SetCell(duplicate, new NumberValue(5));
        sheet.SetCell(unique, new NumberValue(7));
        var range = new GridRange(first, unique);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.DuplicateValues,
            AppliesTo = range,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1).Fill.Should().Be(new PresentationRgb(255, 0, 0));
        layout.Cells.Single(c => c.Row == 2).Fill.Should().Be(new PresentationRgb(255, 0, 0));
        layout.Cells.Single(c => c.Row == 3).Fill.Should().NotBe(new PresentationRgb(255, 0, 0),
            "the unique (non-duplicated) value must not be highlighted");
    }

    [Fact]
    public void Build_BlanksRuleAppliesStyleOnlyToBlankCells()
    {
        var (workbook, sheet) = CreateWorkbook();
        var blank = new CellAddress(sheet.Id, 1, 1);
        var filled = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(filled, new NumberValue(5)); // leave `blank` untouched
        // The blank cell has no occupied-cell entry of its own, so it must be pulled into the
        // print range explicitly -- GetUsedRange() alone would not include a row that holds
        // nothing but an (unwritten) CF rule.
        sheet.PrintArea = new GridRange(blank, filled);

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            RuleType = CfRuleType.Blanks,
            AppliesTo = new GridRange(blank, filled),
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var layout = BuildFirstPage(workbook, sheet)!;

        layout.Cells.Single(c => c.Row == 1).Fill.Should().Be(new PresentationRgb(255, 0, 0),
            "the blank cell must be highlighted in print/PDF");
        layout.Cells.Single(c => c.Row == 2).Fill.Should().NotBe(new PresentationRgb(255, 0, 0),
            "a non-blank cell must not match the Blanks rule");
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
