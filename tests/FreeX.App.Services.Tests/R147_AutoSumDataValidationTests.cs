using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression coverage for the R147 data-validation F2 finding
/// (src/FreeX.App.Services/WorkbookSession.cs:3632): unlike <see cref="WorkbookSession.CommitCellText"/>/
/// <see cref="WorkbookSession.CommitCellTextAcrossSelection"/> (both routed through
/// <c>TryBuildValidatedCellEntryEdits</c>, which calls the private <c>EvaluateDataValidationForEntry</c>
/// choke point), <see cref="WorkbookSession.InsertAutoSumFormula"/> committed its planned SUM/AVERAGE/
/// etc. formula straight through <c>_cellEditService.ExecuteEditCommand</c> with no Data Validation
/// check at all -- so a Stop-alert rule on the AutoSum target (an entirely ordinary setup for a totals
/// cell under a column of numbers) never blocked the insert, and a Warning/Information rule was never
/// even offered to <see cref="WorkbookSession.DataValidationPromptResolver"/>.
/// </summary>
public sealed class R147_AutoSumDataValidationTests
{
    /// <summary>
    /// Fails before the fix: a Stop-alert Data Validation rule on the AutoSum target must block the
    /// AutoSum insert exactly like it blocks a manually-typed formula via
    /// <see cref="WorkbookSession.CommitCellText"/>.
    /// </summary>
    [Fact]
    public void InsertAutoSumFormula_StopStyleRuleOnTarget_BlocksTheInsert()
    {
        var (session, sheet, b1, b2, b3) = CreateSessionWithNumbersAndStopRuleOnTotal(max: 100);
        session.SelectRange(new GridRange(b1, b3));

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeFalse(
            "a Stop-alert Data Validation rule on the AutoSum target must block the insert, matching CommitCellText's enforcement of the same rule");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.GetCell(b3).Should().BeNull(
            "the blocked AutoSum formula must not be written to the target cell");
    }

    /// <summary>
    /// Warning-style rule + a No/Cancel decision from the host prompt must also decline the insert,
    /// mirroring <see cref="WorkbookSession.CommitCellText"/>'s AskToContinue handling (R73).
    /// </summary>
    [Fact]
    public void InsertAutoSumFormula_WarningStyleRule_NoDecision_DoesNotCommit()
    {
        var (session, sheet, b1, b2, b3) = CreateSessionWithNumbersAndStopRuleOnTotal(max: 100, DvAlertStyle.Warning);
        session.SelectRange(new GridRange(b1, b3));
        session.DataValidationPromptResolver = _ => UserMessageResult.No;

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeFalse();
        sheet.GetCell(b3).Should().BeNull();
    }

    /// <summary>
    /// Warning-style rule + a Yes decision from the host prompt must still commit the AutoSum formula
    /// (Excel parity: the user explicitly chose to continue past the warning).
    /// </summary>
    [Fact]
    public void InsertAutoSumFormula_WarningStyleRule_YesDecision_StillCommits()
    {
        var (session, sheet, b1, b2, b3) = CreateSessionWithNumbersAndStopRuleOnTotal(max: 100, DvAlertStyle.Warning);
        session.SelectRange(new GridRange(b1, b3));
        session.DataValidationPromptResolver = _ => UserMessageResult.Yes;

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetCell(b3)!.FormulaText.Should().Be("SUM(B1:B2)");
    }

    /// <summary>
    /// No-regression sibling: an AutoSum target with NO Data Validation rule at all (the overwhelming
    /// common case) must continue to commit exactly as before, with no prompt consulted.
    /// </summary>
    [Fact]
    public void InsertAutoSumFormula_NoValidationRuleOnTarget_StillCommitsNormally()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b1, new NumberValue(500));
        sheet.SetCell(b2, new NumberValue(500));
        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        session.DataValidationPromptResolver = _ =>
            throw new InvalidOperationException("No Data Validation rule applies -- the resolver must never be consulted.");
        session.SelectRange(new GridRange(b1, b3));

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetCell(b3)!.FormulaText.Should().Be("SUM(B1:B2)");
        sheet.GetValue(b3).Should().Be(new NumberValue(1000));
    }

    /// <summary>
    /// No-regression sibling: a valid AutoSum result that satisfies a Stop-style rule (result within
    /// range) must still commit -- the fix must not block AutoSum outright, only violating results.
    /// </summary>
    [Fact]
    public void InsertAutoSumFormula_ResultSatisfiesStopStyleRule_StillCommits()
    {
        var (session, sheet, b1, b2, b3) = CreateSessionWithNumbersAndStopRuleOnTotal(max: 5000);
        session.SelectRange(new GridRange(b1, b3));

        var result = session.InsertAutoSumFormula("SUM");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetCell(b3)!.FormulaText.Should().Be("SUM(B1:B2)");
        sheet.GetValue(b3).Should().Be(new NumberValue(1000));
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress B1, CellAddress B2, CellAddress B3)
        CreateSessionWithNumbersAndStopRuleOnTotal(int max, DvAlertStyle alertStyle = DvAlertStyle.Stop)
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var b3 = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(b1, new NumberValue(500));
        sheet.SetCell(b2, new NumberValue(500));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(b3, b3),
            Type = DvType.WholeNumber,
            Operator = DvOperator.LessThanOrEqual,
            Formula1 = max.ToString(),
            AlertStyle = alertStyle,
            ShowErrorMessage = true
        });

        var session = CreateSession(new StartupWorkbookLoadResult(
            workbook, "Book.fxl", "Opened .fxl.", IsFallback: false));
        return (session, sheet, b1, b2, b3);
    }

    private static WorkbookSession CreateSession(StartupWorkbookLoadResult source) =>
        new WorkbookSessionFactory().Create(source, viewportHeight: 240, viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
