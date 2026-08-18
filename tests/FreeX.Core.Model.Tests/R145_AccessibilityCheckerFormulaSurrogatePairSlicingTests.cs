using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

// Round-145 finding F1: the accessibility checker's conditional-format custom-formula
// evaluator (AccessibilityCheckerService.Contrast.cs) carries its own private, duplicate
// reimplementation of REPLACE/TEXTBEFORE/TEXTAFTER (FormulaReplaceText,
// FormulaTextBeforeAfterScalar) that used to treat a UTF-16 surrogate pair (e.g. an emoji) as
// ONE indivisible "text element" when computing character positions/lengths. The real formula
// engine (FreeX.Core.Formula BuiltInFunctions.TextCore.Replace.cs / BuiltInFunctions.
// TextSplit.cs) -- and Excel itself -- instead counts/slices by raw UTF-16 code unit,
// deliberately splitting surrogate pairs. See tests/FreeX.Core.Formula.Tests/
// FunctionLibraryTests.TextExtensions.cs "Replace_SlicesOnUtf16CodeUnitBoundaries" and
// ExcelParityModernTextTests.cs "TextBeforeAfter_InstanceNumBeyondOccurrenceCountReturnsNA"
// for the real engine's pinned code-unit semantics.
//
// Before the fix, a conditional-formatting custom-formula rule built from REPLACE/TEXTBEFORE/
// TEXTAFTER on text containing an astral character (surrogate pair) evaluated to a DIFFERENT
// boolean here than what the real formula engine computes for the identical rule -- so the
// Accessibility Checker's contrast report could silently disagree with what FreeX actually
// paints on the grid.
public sealed class R145_AccessibilityCheckerFormulaSurrogatePairSlicingTests
{
    private static CellStyle CreateLowContrastCellStyle() => new()
    {
        FontColor = new CellColor(120, 120, 120),
        FillColor = new CellColor(130, 130, 130)
    };

    private static Workbook CreateWorkbook(ScalarValue sourceValue, out Sheet sheet, out CellAddress target)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        var source = new CellAddress(sheet.Id, 1, 1); // A1
        target = new CellAddress(sheet.Id, 1, 2); // B1

        sheet.SetCell(source, sourceValue);
        sheet.SetCell(target, new TextValue("Value"));

        return workbook;
    }

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

    private static List<AccessibilityIssue> FindLowContrastCellTextIssues(Workbook workbook) =>
        AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaReplaceOnSurrogatePairText()
    {
        // "😀x" == "😀x": the emoji is a 2-code-unit surrogate pair followed by "x".
        // REPLACE($A1,1,1,"Q") must replace only the first UTF-16 code unit (the high
        // surrogate), leaving the orphaned low surrogate + "x" behind -- exactly like Excel
        // and the real formula engine (see Replace_SlicesOnUtf16CodeUnitBoundaries:
        // REPLACE("😀x",1,1,"Q") == "Q\uDE00x", 3 code units).
        //
        // Before the fix: FormulaReplaceText treated the whole surrogate pair as one
        // indivisible "text element", so the entire emoji was consumed by the 1-character
        // replace, producing "Qx" (LEN 2) -- the rule evaluated FALSE and no issue was
        // reported.
        // After the fix: the result is "Q\uDE00x" (LEN 3), matching the real engine, and the
        // rule fires.
        var workbook = CreateWorkbook(new TextValue("😀x"), out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "LEN(REPLACE($A1,1,1,\"Q\"))=3");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaReplaceOnPlainBmpText()
    {
        // Sibling no-regression case: plain BMP-only text (no surrogate pairs) must still
        // slice exactly as before -- REPLACE("Closed",1,1,"X") == "Xlosed" (LEN 6) both before
        // and after the fix, since FormulaReplaceText's surrogate-pair branch never activated
        // for this input either way.
        var workbook = CreateWorkbook(new TextValue("Closed"), out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "REPLACE($A1,1,1,\"X\")=\"Xlosed\"");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaTextBeforeInstanceNumOnSurrogatePairText()
    {
        // "😀" == "😀" alone: 2 UTF-16 code units, 1 "text element".
        // TEXTBEFORE($A1,"z",2) has instance_num 2. Excel/the real engine bound-check
        // instance_num against the raw UTF-16 length (2), so |2| > 2 is FALSE -- the
        // bound check passes, the "z" delimiter is legitimately not found, and the
        // function returns the (default) #N/A "not found" error (see ExcelParityModernTextTests
        // "TextBeforeAfter_InstanceNumBeyondOccurrenceCountReturnsNA").
        //
        // Before the fix: FormulaTextBeforeAfterScalar bound-checked against the
        // surrogate-pair-collapsed "text element" count (1), so |2| > 1 was TRUE and the
        // function short-circuited to a generic #VALUE! domain error instead -- ISNA() on
        // that result is FALSE, so the rule never fired.
        // After the fix: ISNA() is TRUE (the #N/A "not found" result), matching the real
        // engine, and the rule fires.
        var workbook = CreateWorkbook(new TextValue("😀"), out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "ISNA(TEXTBEFORE($A1,\"z\",2))");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaTextBeforeOnPlainBmpText()
    {
        // Sibling no-regression case: plain BMP-only text -- TEXTBEFORE("Closed","s") == "Clo"
        // is unaffected by the surrogate-pair bound-check fix (no surrogate pairs present).
        var workbook = CreateWorkbook(new TextValue("Closed"), out var sheet, out var target);
        AddFormulaContrastRule(sheet, target, "TEXTBEFORE($A1,\"s\")=\"Clo\"");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }
}
