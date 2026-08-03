using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R120-formula-entry-arity-validation: <see cref="CellEntryParser.CreateCell"/> previously only
/// validated a typed formula's raw lexer/parser syntax (R91), never a well-known built-in
/// function's argument count against its registered arity. Real Excel's formula-entry compiler
/// checks BOTH: typing e.g. "=IF(A1&gt;0)" (1 argument; IF requires 2 or 3) or
/// "=LEFT(\"x\",1,2,3)" (4 arguments; LEFT allows at most 2) pops Excel's "You've entered too
/// few/too many arguments for this function" dialog and refuses to leave edit mode, even though
/// the text is otherwise syntactically well-formed. FreeX silently committed such formulas and
/// only ever surfaced the problem later as a #VALUE! during recalculation. This adds an
/// arity-validation choke point (<see cref="FormulaEvaluator.ValidateBuiltInFunctionArity"/>)
/// that <c>CreateCell</c> now calls right after a successful
/// <see cref="FormulaEvaluator.ParseFormula"/>, so an out-of-range built-in call rejects the
/// entry via <see cref="FormulaParseException"/> -- caught by the real product entry point,
/// <see cref="WorkbookSession.CommitCellText"/> (shared by both shells), exactly the way R91's
/// syntax-balance check already is.
/// </summary>
public sealed class R120_CellEntryFormulaArityValidationTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_IfWithOneArgument_ThrowsFormulaParseException()
    {
        // IF requires 2 or 3 arguments; Excel rejects this at entry with "too few arguments".
        var act = () => CellEntryParser.CreateCell("=IF(A1>0)", Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_LeftWithFourArguments_ThrowsFormulaParseException()
    {
        // LEFT allows at most 2 arguments; Excel rejects this at entry with "too many arguments".
        var act = () => CellEntryParser.CreateCell("=LEFT(\"x\",1,2,3)", Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_NestedTooFewArgumentCall_ThrowsFormulaParseException()
    {
        // The bad arity is nested inside another call's argument list, not at the top level --
        // Excel's entry-time check still catches it, so the validator must recurse into arguments.
        var act = () => CellEntryParser.CreateCell("=SUM(IF(A1>0),1)", Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_ValidIfFormula_StillCommitsAsFormulaCell()
    {
        // No-regression sibling: a genuinely valid, in-range IF call must be entirely unaffected.
        var cell = CellEntryParser.CreateCell("=IF(A1>0,1,2)", Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be("IF(A1>0,1,2)");
    }

    [Fact]
    public void CreateCell_SumWithManyArguments_StillCommits()
    {
        // No-regression sibling: aggregate functions (SUM et al.) are genuinely variadic and must
        // not be rejected merely for having "many" arguments, mirroring the recalculation path's
        // own isAggregate max-args exemption in FormulaEvaluator.Functions.cs.
        var manyArgs = string.Join(",", Enumerable.Range(1, 40).Select(n => n.ToString()));
        var cell = CellEntryParser.CreateCell($"=SUM({manyArgs})", Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be($"SUM({manyArgs})");
    }

    [Fact]
    public void CommitCellText_IfWithOneArgument_FailsCommitInsteadOfPersistingBrokenFormula()
    {
        var (session, sheet, address) = CreateSessionAtA1();

        var result = session.CommitCellText("=IF(A1>0)");

        result.Success.Should().BeFalse(
            "a well-known function called with too few arguments must fail the commit, matching Excel's refusal to leave edit mode");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.GetCell(address).Should().BeNull("the invalid entry must not be persisted into the model at all");
    }

    [Fact]
    public void CommitCellText_ValidFormula_StillCommitsAndRecalculates()
    {
        // No-regression sibling: the real WorkbookSession.CommitCellText entry point must still
        // commit and recalculate a genuinely valid formula exactly as before.
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
