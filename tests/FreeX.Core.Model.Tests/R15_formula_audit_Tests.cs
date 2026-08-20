using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Round-15 regression coverage for FormulaAuditingService.References.cs, .Errors.cs, and
/// FormulaEvaluationSummaryService.cs. See R15-formula-auditing-help-1 and
/// R15-formula-auditing-help-3.
/// </summary>
public sealed class R15_formula_audit_Tests
{
    [Fact]
    public void FindFormulaErrorIssues_DoesNotFlagLog10FunctionCallAsBlankCellReference()
    {
        // R15-formula-auditing-help-1: TryReadFormulaReference used to fire before the
        // function-call ('(' ) check, so "LOG10(5)" misparsed the column+row "LOG10" as a cell
        // reference (row 10, column "LOG") and flagged it as referring to a blank cell.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 4, 4);
        sheet.SetCell(formulaAddress, Cell.FromFormula("LOG10(5)"));

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id);

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindFormulaErrorIssues_StillFlagsGenuineBlankCellReferenceAlongsideFunctionCall()
    {
        // Guards against the fix over-correcting: a real blank precedent next to a function call
        // that resembles a reference (LOG10) must still be caught.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var formulaAddress = new CellAddress(sheet.Id, 20, 1);
        sheet.SetCell(formulaAddress, Cell.FromFormula("LOG10(5)+A1"));

        var issue = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id)
            .Should().ContainSingle().Subject;

        issue.ErrorCode.Should().Be(FormulaAuditingService.FormulaRefersToBlankCellsErrorCode);
    }

    [Fact]
    public void NormalizeFormulaPattern_DoesNotTreatFunctionNameEndingInDigitsAsReference()
    {
        // R15-formula-auditing-help-1 also applies to NormalizeFormulaPattern (used to detect
        // "inconsistent formula" runs): "LOG10" must not be misread as a cell reference (row 10,
        // column "LOG") and rewritten into a bogus R[]C[] relative-reference pattern. With no
        // genuine cell references present, the normalized pattern must equal the original text.
        var normalizeFormulaPattern = typeof(FormulaAuditingService).GetMethod(
            "NormalizeFormulaPattern",
            BindingFlags.NonPublic | BindingFlags.Static);
        normalizeFormulaPattern.Should().NotBeNull();

        var address = new CellAddress(new SheetId(Guid.NewGuid()), 1, 1);

        var normalized = (string)normalizeFormulaPattern!.Invoke(null, [address, "LOG10(5)"])!;

        normalized.Should().Be("LOG10(5)");
    }

    [Fact]
    public void FormulaEvaluationSession_CurrentHighlight_PointsAtTrailingTokenNotEmbeddedSubstring()
    {
        // R15-formula-auditing-help-3: formula.IndexOf(expression) used to match the FIRST
        // substring occurrence, so highlighting "A1" inside "=A11+A1" underlined the "A1" embedded
        // in "A11" instead of the actual trailing A1 token the step refers to.
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=A11+A1",
            "value",
            [
                new FormulaEvaluationStep("A11", "..."),
                new FormulaEvaluationStep("A1", "..."),
                new FormulaEvaluationStep("A11+A1", "...")
            ]);

        var session = FormulaEvaluationSession.Start(summary);
        session.MoveNext();

        session.CurrentStep.Should().Be(summary.Steps[1]);
        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=A11+", "A1", ""));
    }

    [Fact]
    public void FormulaEvaluationSession_CurrentHighlight_StillFindsFirstOfRepeatedIdenticalSubExpression()
    {
        // Guards against the fix over-correcting: when the same token legitimately appears twice
        // ("=A1+A1"), the first step ("A1") should still resolve to the first occurrence.
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=A1+A1",
            "value",
            [
                new FormulaEvaluationStep("A1", "..."),
                new FormulaEvaluationStep("A1", "..."),
                new FormulaEvaluationStep("A1+A1", "...")
            ]);

        var session = FormulaEvaluationSession.Start(summary);

        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=", "A1", "+A1"));
    }

    [Fact]
    public void FormulaEvaluationSession_CurrentHighlight_MovesToSecondOccurrenceOnSecondStep()
    {
        // freex-formula-auditing F1: stepping past the first "A1" in "=A1+A1" must move the
        // highlight to the SECOND "A1", not keep re-highlighting the first one. Before the fix,
        // FindExpressionTokenIndex always returned the first textual match for a given expression
        // string, so both steps highlighted the same leading span.
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=A1+A1",
            "value",
            [
                new FormulaEvaluationStep("A1", "..."),
                new FormulaEvaluationStep("A1", "..."),
                new FormulaEvaluationStep("A1+A1", "...")
            ]);

        var session = FormulaEvaluationSession.Start(summary);

        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=", "A1", "+A1"));

        session.MoveNext();

        session.CurrentStep.Should().Be(summary.Steps[1]);
        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=A1+", "A1", ""));
    }
}
