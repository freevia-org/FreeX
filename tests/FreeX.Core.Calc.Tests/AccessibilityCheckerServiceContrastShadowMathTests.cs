using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// Round-57 findings R57-formula-trig-math-5-1 / -5-2: the accessibility checker's
// conditional-format contrast checker carries its own independent shadow arithmetic
// evaluator (AccessibilityCheckerService.Contrast.cs) that must agree with the real
// Core.Formula evaluator for POWER and QUOTIENT edge cases, or it silently
// mis-reports (false positive/negative) low-contrast conditional-format issues.
public sealed class AccessibilityCheckerServiceContrastShadowMathTests
{
    private static CellStyle CreateLowContrastCellStyle() => new()
    {
        FontColor = new CellColor(120, 120, 120),
        FillColor = new CellColor(130, 130, 130)
    };

    private static Workbook CreateSingleCellFormulaContrastWorkbook(
        double baseValue,
        out Sheet sheet,
        out CellAddress target)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        target = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.SetCell(source, new NumberValue(baseValue));
        sheet.SetCell(target, new TextValue("Value"));

        return workbook;
    }

    private static List<AccessibilityIssue> FindLowContrastCellTextIssues(Workbook workbook) =>
        AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

    private static void AddFormulaContrastRule(Sheet sheet, CellAddress target, string formulaText)
    {
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(target, target),
            RuleType = CfRuleType.Formula,
            FormulaText = formulaText,
            FormatIfTrue = CreateLowContrastCellStyle()
        });
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatPowerZeroToTheZeroOperand()
    {
        // Excel's POWER(0,0) is #NUM! (matching Core.Formula's PowerScalar guard), and a
        // formula error is treated as FALSE for conditional-format purposes, so the format
        // must NOT apply and no low-contrast issue should be reported for B1.
        var workbook = CreateSingleCellFormulaContrastWorkbook(0, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "POWER($A1,0)>=1");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPowerNonZeroBaseZeroExponent()
    {
        // Sibling/no-regression case: a non-zero base raised to the zero exponent is a
        // perfectly ordinary POWER call (result 1), not the #NUM! special case, so the
        // 0^0 guard must not affect it - the format should still apply normally.
        var workbook = CreateSingleCellFormulaContrastWorkbook(5, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "POWER($A1,0)>=1");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatQuotientDecimalPrecisionOperands()
    {
        // Excel's QUOTIENT(0.3,0.1) is 3 (Core.Formula's TruncateExcelQuotient round-trips
        // through decimal to avoid the 0.3/0.1==2.9999999999999996 IEEE-754 double
        // truncation error), so 3>2 is TRUE and the format must apply.
        var workbook = CreateSingleCellFormulaContrastWorkbook(0.3, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "QUOTIENT($A1,0.1)>2");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatQuotientOrdinaryOperands()
    {
        // Sibling/no-regression case: ordinary integer-ish operands unaffected by the
        // decimal-precision correction must keep truncating the same way as before.
        var workbook = CreateSingleCellFormulaContrastWorkbook(5, out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "QUOTIENT($A1,2)>=2");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }
}
