using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R91-formula-editing-assist-5-4: <see cref="CellEntryParser.CreateCell"/> previously stripped
/// the leading '=' and called <c>Cell.FromFormula</c> unconditionally, with no lexing/parsing/
/// balance check at entry time at all -- an unbalanced/invalid formula (e.g. "=SUM(A1", a missing
/// closing paren) silently committed as a broken formula cell, only ever surfacing as a #VALUE!
/// error later during recalculation. Real Excel refuses to leave edit mode for genuinely malformed
/// formula syntax. This adds an up-front <see cref="FormulaEvaluator.ParseFormula"/> validation
/// choke point inside <c>CreateCell</c> so a parse failure rejects the entry via
/// <see cref="FormulaParseException"/>, which the real product entry point --
/// <see cref="WorkbookSession.CommitCellText"/>, shared by both shells -- now catches and turns
/// into a failed <see cref="WorkbookCellEditResult"/> instead of committing.
/// </summary>
public sealed class R91_CellEntryFormulaValidationTests
{
    private static readonly CellAddress Anchor = new(SheetId.New(), 2, 2);

    [Fact]
    public void CreateCell_UnbalancedParenFormula_ThrowsFormulaParseException()
    {
        var act = () => CellEntryParser.CreateCell("=SUM(A1", Anchor, useR1C1ReferenceStyle: false);

        act.Should().Throw<FormulaParseException>();
    }

    [Fact]
    public void CreateCell_ValidFormula_StillCommitsAsFormulaCell()
    {
        // No-regression sibling: a genuinely valid, balanced formula must be entirely unaffected
        // by the new up-front validation.
        var cell = CellEntryParser.CreateCell("=SUM(A1:A2)", Anchor, useR1C1ReferenceStyle: false);

        cell.FormulaText.Should().Be("SUM(A1:A2)");
    }

    [Fact]
    public void CreateCell_LeadingApostropheBeforeUnbalancedFormulaText_StaysLiteralTextNotValidated()
    {
        // No-regression sibling: Excel's leading-apostrophe text escape takes priority over
        // formula recognition entirely -- "'=SUM(A1" must stay literal text "=SUM(A1" and must
        // never even reach the new formula-syntax validation (it would incorrectly throw
        // otherwise, since the remaining text is indeed unbalanced).
        var cell = CellEntryParser.CreateCell("'=SUM(A1", Anchor, useR1C1ReferenceStyle: false);

        cell.Value.Should().BeOfType<TextValue>().Which.Value.Should().Be("=SUM(A1");
        cell.FormulaText.Should().BeNull();
    }

    [Fact]
    public void CommitCellText_UnbalancedParenFormula_FailsCommitInsteadOfPersistingBrokenFormula()
    {
        var (session, sheet, address) = CreateSessionAtA1();

        var result = session.CommitCellText("=SUM(A1");

        result.Success.Should().BeFalse(
            "an unbalanced formula must fail the commit, matching Excel's correction-prompt refusal to leave edit mode");
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

        var result = session.CommitCellText("=B1+1");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(5));
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
