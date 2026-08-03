using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R120-formula-entry-nesting-length-validation: <see cref="CellEntryParser.CreateCell"/>
/// previously only validated a typed formula's raw syntax (R91) and built-in-function arity
/// (R120-formula-entry-arity-validation) at entry time, never Excel's documented 64-level
/// function-nesting limit or 8,192-character formula-length limit. Real Excel's formula bar
/// refuses to leave edit mode for a formula built with more than 64 nested function levels (e.g.
/// 100 nested <c>IF()</c> calls) or longer than 8,192 characters, even though FreeX's own parser
/// happily accepted both -- its <see cref="FormulaSafetyLimits.MaxParseNesting"/>/
/// <see cref="FormulaSafetyLimits.MaxParseDepth"/> caps (256/512) are internal recursion/stack-depth
/// DoS guards, not a stand-in for Excel's much smaller real limit. This adds
/// <see cref="FormulaEvaluator.ValidateFunctionNestingDepth"/> and
/// <see cref="FormulaEvaluator.ValidateFormulaEntryLength"/> as choke points <c>CreateCell</c> now
/// calls, so an over-nested or over-long formula rejects the entry via
/// <see cref="FormulaParseException"/> -- caught by the real product entry point,
/// <see cref="WorkbookSession.CommitCellText"/> (shared by both shells), exactly the way the
/// R91 syntax-balance and R120 arity checks already are.
/// </summary>
public sealed class R120_CellEntryFormulaNestingLengthValidationTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    private static string BuildNestedIfFormula(int nestingLevels)
    {
        var formula = "1";
        for (var i = 0; i < nestingLevels; i++)
            formula = $"IF({formula},1,1)";
        return formula;
    }

    [Fact]
    public void CreateCell_100NestedIfCalls_ThrowsFormulaParseException()
    {
        // 100 nested IF() calls exceed Excel's documented 64-level function-nesting limit but
        // stay well under the parser's internal 256-level DoS guard, so this must be the NEW
        // check -- not the pre-existing generic nesting cap -- that rejects it.
        var act = () => CellEntryParser.CreateCell(
            "=" + BuildNestedIfFormula(100), Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_TooLongFormula_ThrowsFormulaParseException()
    {
        // Deliberately a WIDE shape (many SUM() siblings), not a deep chain: SUM's argument list
        // recurses only one level per argument (see FormulaEvaluator.ValidateBuiltInFunctionArity/
        // ValidateFunctionNestingDepth), so this exercises the length limit in isolation without
        // also depending on a deep-recursion-safe AST walk elsewhere in the entry-time pipeline.
        var tooLong = "=SUM(" + string.Join(",", Enumerable.Repeat("1", 6000)) + ")";
        tooLong.Length.Should().BeGreaterThan(FormulaEvaluator.MaxFormulaEntryLength);

        var act = () => CellEntryParser.CreateCell(tooLong, Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_ValidModestlyNestedFormula_StillCommitsAsFormulaCell()
    {
        // No-regression sibling: a genuinely valid, modestly nested formula (well under both the
        // new 64-level check and the old 256-level DoS guard) must be entirely unaffected.
        var cell = CellEntryParser.CreateCell(
            "=" + BuildNestedIfFormula(10), Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be(BuildNestedIfFormula(10));
    }

    [Fact]
    public void CreateCell_ManySiblingFunctionCallsNotNested_StillCommits()
    {
        // No-regression sibling: many function calls that are SIBLINGS (SUM's own argument list),
        // not nested inside each other, must not be rejected -- only actual function-in-function
        // nesting counts.
        var args = string.Join(",", Enumerable.Range(1, 100).Select(n => $"ABS({-n})"));
        var cell = CellEntryParser.CreateCell($"=SUM({args})", Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be($"SUM({args})");
    }

    [Fact]
    public void CommitCellText_100NestedIfCalls_FailsCommitInsteadOfPersistingOverNestedFormula()
    {
        var (session, sheet, address) = CreateSessionAtA1();

        var result = session.CommitCellText("=" + BuildNestedIfFormula(100));

        result.Success.Should().BeFalse(
            "a formula exceeding Excel's documented 64-level function-nesting limit must fail the commit, matching Excel's refusal to leave edit mode");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.GetCell(address).Should().BeNull("the invalid entry must not be persisted into the model at all");
    }

    [Fact]
    public void CommitCellText_ValidFormula_StillCommitsAndRecalculates()
    {
        // No-regression sibling: the real WorkbookSession.CommitCellText entry point must still
        // commit and recalculate a genuinely valid, modestly nested formula exactly as before.
        var (session, sheet, address) = CreateSessionAtA1();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(4));

        var result = session.CommitCellText("=IF(B1>0,B1,0)");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(4));
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress Address) CreateSessionAtA1()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var sheet = workbook.Sheets.Single();
        var address = new CellAddress(sheet.Id, 1, 1);

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        session.SelectCell(address);

        return (session, sheet, address);
    }
}
