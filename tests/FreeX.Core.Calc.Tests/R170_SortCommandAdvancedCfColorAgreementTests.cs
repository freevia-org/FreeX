using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R170-freex-autofilter-sort-F1: SortCommand.GetEffectiveColor -- shared by Sort On: Cell/Font
/// Color, FilterCommand's cell/font-color filter commands, and AutoFilterDropdownMenuPlanner's
/// color-swatch scan -- used to hand-evaluate only the "simple" CF rule shapes judgeable purely
/// from a cell's own value (CellValue, Blanks/Errors, text-match), leaving ColorScale, DataBar,
/// Top10, AboveAverage and Duplicate/UniqueValues unresolved. So a column visibly painted by e.g.
/// a Color Scale reported every one of those cells as having no CF color at all -- "Filter by Cell
/// Color"/"Sort by Cell Color" disagreed with what ViewportConditionalFormatEvaluator (the
/// evaluator that actually paints the screen) renders for the exact same cell.
///
/// Fixed by having GetEffectiveColor delegate to that same evaluator (via the public
/// ConditionalFormatEvaluationSession wrapper FreeX.Core.Commands.AccessibilityCheckerService
/// already uses) instead of maintaining a second, narrower copy of the rule logic.
///
/// These tests independently rebuild "what the renderer paints" by driving
/// ViewportConditionalFormatEvaluator directly -- the same evaluator ViewportService's on-screen
/// rendering calls -- completely bypassing SortCommand's own session cache, and assert that
/// SortCommand.GetEffectiveColor (the actual Sort/Filter/swatch-scan entry point) agrees with it
/// for the same cell. This is the two-path agreement check the finding is about, not a
/// substring/literal assertion on either path alone.
/// </summary>
public sealed class R170_SortCommandAdvancedCfColorAgreementTests
{
    /// <summary>
    /// Rebuilds the fill color ViewportConditionalFormatEvaluator would paint for a cell, driving
    /// it directly and independently of anything SortCommand caches or calls internally.
    /// </summary>
    private static CellColor? RenderedFillColor(Workbook workbook, Sheet sheet, CellAddress address, ScalarValue value, CellStyle baseStyle)
    {
        var context = ViewportConditionalFormatEvaluator.BuildContext(sheet, workbook);
        var result = ViewportConditionalFormatEvaluator.Evaluate(sheet, address, value, workbook, context, ViewportService.MatchesFormula);
        var merged = result is null ? baseStyle : ViewportConditionalFormatEvaluator.MergeStyles(baseStyle, result.Value.Style);
        return merged.FillColor;
    }

    [Fact]
    public void GetEffectiveColor_ColorScaleRule_AgreesWithWhatTheRendererPaints()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        // A1:A3 numeric column with a 2-color scale over the whole range -- exactly the finding's
        // gesture (Home > Conditional Formatting > Color Scales on a data column).
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(a1, a3),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = new RgbColor(0, 0, 0),
            MaxColor = new RgbColor(255, 255, 255),
        });

        var midCell = sheet.GetCell(a2);
        var baseStyle = workbook.GetStyle(midCell!.StyleId);

        var expected = RenderedFillColor(workbook, sheet, a2, new NumberValue(50), baseStyle);
        var actual = SortCommand.GetEffectiveColor(workbook, sheet, a2, midCell, wantFill: true);

        expected.Should().NotBeNull("the test's own render check is a sanity guard, not the assertion under test");
        actual.Should().NotBeNull(
            "a cell visibly painted by a Color Scale must report a fill color to Sort/Filter by Color, not \"no fill\"");
        actual.Should().Be(expected,
            "Sort On/Filter by Cell Color must agree with what ViewportConditionalFormatEvaluator paints for the same cell");
    }

    [Fact]
    public void GetEffectiveColor_ColorScaleRule_SwatchScanAcrossRowsAgreesWithRenderer()
    {
        // Mirrors AutoFilterDropdownMenuPlanner.CollectColorOptions (FreeX.App.Presentation),
        // which calls this exact GetEffectiveColor for every row to build the "Filter by Color"
        // swatch list. Scanning three differently-valued rows here proves the swatch list a real
        // AutoFilter dropdown would build is non-empty and matches the renderer row-by-row, not
        // just for one cherry-picked address.
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        var values = new ScalarValue[] { new NumberValue(1), new NumberValue(50), new NumberValue(100) };
        var addresses = new[] { a1, a2, a3 };
        for (var i = 0; i < addresses.Length; i++)
            sheet.SetCell(addresses[i], Cell.FromValue(values[i]));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(a1, a3),
            RuleType = CfRuleType.ColorScale,
            UseThreeColorScale = false,
            MinColor = new RgbColor(10, 20, 30),
            MaxColor = new RgbColor(200, 210, 220),
        });

        var swatches = new HashSet<CellColor>();
        for (var i = 0; i < addresses.Length; i++)
        {
            var cell = sheet.GetCell(addresses[i]);
            var baseStyle = workbook.GetStyle(cell!.StyleId);

            var expected = RenderedFillColor(workbook, sheet, addresses[i], values[i], baseStyle);
            var actual = SortCommand.GetEffectiveColor(workbook, sheet, addresses[i], cell, wantFill: true);

            actual.Should().NotBeNull($"row {i} is visibly painted by the Color Scale");
            actual.Should().Be(expected, $"row {i}'s swatch/row-match color must agree with what the renderer paints");
            swatches.Add(actual!.Value);
        }

        // Three distinct numeric inputs across a 2-color scale must not all collapse to one color.
        swatches.Should().HaveCountGreaterThan(1,
            "the swatch list Filter by Color would offer for this column must not be empty/uniform " +
            "when the column is visibly painted across a gradient");
    }

    [Fact]
    public void GetEffectiveColor_AboveAverageRule_AgreesWithWhatTheRendererPaints()
    {
        // Sibling coverage for a second aggregate-requiring rule kind named by the finding
        // (AboveAverage), proving the fix is the general evaluator delegation, not a special case
        // carved out only for ColorScale.
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(a2, Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(a3, Cell.FromValue(new NumberValue(90))); // well above the mean of 31

        var green = new CellColor(0, 128, 0);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(a1, a3),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            FormatIfTrue = new CellStyle { FillColor = green },
        });

        var cell = sheet.GetCell(a3);
        var baseStyle = workbook.GetStyle(cell!.StyleId);

        var expected = RenderedFillColor(workbook, sheet, a3, new NumberValue(90), baseStyle);
        var actual = SortCommand.GetEffectiveColor(workbook, sheet, a3, cell, wantFill: true);

        expected.Should().Be(green, "the test's own render check is a sanity guard, not the assertion under test");
        actual.Should().Be(expected,
            "Sort On/Filter by Cell Color must agree with what the renderer paints for an AboveAverage rule too");
    }

    [Fact]
    public void GetEffectiveColor_SimpleCellValueRule_StillMatchesUnchangedBehavior()
    {
        // No-regression sibling: the rule kind this method already resolved correctly before the
        // fix (a literal CellValue comparison) must still resolve to the same color afterwards.
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(200)));

        var red = new CellColor(255, 0, 0);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(a1, a1),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "100",
            FormatIfTrue = new CellStyle { FillColor = red },
        });

        var cell = sheet.GetCell(a1);
        var actual = SortCommand.GetEffectiveColor(workbook, sheet, a1, cell, wantFill: true);

        actual.Should().Be(red);
    }
}
