using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// ── Round-23 fix bucket cf-text-date-rules ─────────────────────────────────────
//
// R23-conditional-format-eval-deep-1 [HIGH]: MatchesTextRule (ViewportConditionalFormatEvaluator.
// Aggregates.cs) called GetString(value) with no ErrorValue guard, so an error cell's code text
// (e.g. "#DIV/0!") was fed straight into the Contains/BeginsWith/EndsWith/NotContains substring
// match. Real Excel's text rules are effectively ISERROR-gated (Contains ~ NOT(ISERROR(SEARCH(...
// )))), so an error cell never matches Contains/BeginsWith/EndsWith and always matches
// NotContains.
//
// R23-conditional-format-eval-deep-2 [HIGH]: MatchesDateOccurring required `value is
// DateTimeValue`, but ordinary date arithmetic (e.g. "=A1+1") always decays to a plain
// NumberValue (FormulaEvaluator's arithmetic path routes every numeric result through
// NumberValueFor, which only ever returns NumberValue), so a computed "date" cell never matched
// any Dates-Occurring rule. Fixed to use TryGetDouble (accepts both NumberValue and
// DateTimeValue) like every sibling matcher in the file, then interpret the double as an OADate
// serial.
public sealed class R23_CfTextRuleErrorsAndDateOccurringArithmeticTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void ContainsTextRule_DoesNotMatchErrorCellEvenThoughItsCodeContainsTheSubstring()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // "#DIV/0!" contains a literal "0", but an error cell must never match a Contains rule --
        // Excel's Contains is effectively NOT(ISERROR(SEARCH(...))), and SEARCH on an error
        // propagates the error so ISERROR is TRUE and the rule never fires.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.DivByZero);

        var blue = new CellStyle { FillColor = new CellColor(189, 215, 238) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "0",
            FormatIfTrue = blue
        });

        var vp = new ViewportService().GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        GetCell(vp, 1, 1).Style?.FillColor.Should().NotBe(new CellColor(189, 215, 238),
            "a #DIV/0! error cell must never match a 'Contains 0' rule even though its display code contains '0'");
    }

    [Fact]
    public void NotContainsTextRule_MatchesErrorCellEvenThoughItsCodeContainsTheSubstring()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // "#N/A" contains "N", but a NotContains rule must always match an error cell (the
        // complement of the always-false Contains/BeginsWith/EndsWith case).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), ErrorValue.NA);

        var gray = new CellStyle { FillColor = new CellColor(217, 217, 217) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.NotContainsText,
            TextRuleText = "N",
            FormatIfTrue = gray
        });

        var vp = new ViewportService().GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(217, 217, 217),
            "a #N/A error cell must match a 'Does Not Contain N' rule -- error cells always satisfy NotContains in Excel");
    }

    [Fact]
    public void DateOccurring_MatchesComputedDateFromFormulaArithmetic()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // A1 = a literal date (today). B1 = "=A1+1" ("tomorrow") -- ordinary date arithmetic,
        // which decays to a plain NumberValue via FormulaEvaluator's NumberValueFor, not a
        // DateTimeValue.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(DateTime.Today));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A1+1");

        Engine().RecalculateAllFormulas(workbook);

        // Confirm the premise: the computed "tomorrow" cell is a plain NumberValue, not a
        // DateTimeValue -- exactly the decay the finding describes.
        sheet.GetValue(1, 2).Should().BeOfType<NumberValue>();

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 1, 2)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "tomorrow",
            FormatIfTrue = green
        });

        var vp = new ViewportService().GetViewport(workbook, sheet.Id, new ViewportRequest(1, 1, 500, 500));

        GetCell(vp, 1, 2).Style!.FillColor.Should().Be(new CellColor(198, 239, 206),
            "a formula-computed 'tomorrow' date (decayed to NumberValue) must still match the Dates Occurring rule, same as a literal date cell");
    }
}
