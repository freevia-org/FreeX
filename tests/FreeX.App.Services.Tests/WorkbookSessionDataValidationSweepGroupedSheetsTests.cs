using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression guard for J31 (MED): <see cref="WorkbookSession.ApplyDataValidationToSelectedRangeAndMatchingRanges"/>
/// (the shared session equivalent of the "Apply to all other cells with the same settings" checkbox)
/// only ever read <c>ActiveSheet</c> and swept that single sheet's <c>DataValidations</c> — it never
/// consulted <c>CurrentGroupedEditSheetIds()</c> the way every other grouped-edit session API does
/// (e.g. <c>CreateSetSelectedRangeDataValidationCommand</c>, used by the plain, non-sweep
/// <see cref="WorkbookSession.ApplyDataValidationToSelectedRange"/>). When sheet tabs were grouped,
/// editing a rule and checking the sweep checkbox silently left every other grouped sheet untouched
/// and non-undoable as a single composite, diverging from Excel's grouped-sheet semantics and from the
/// WPF host's own <c>TryExecuteRepeatableGroupedSheetCommand</c>-based sweep.
/// </summary>
public sealed class WorkbookSessionDataValidationSweepGroupedSheetsTests
{
    [Fact]
    public void ApplyDataValidationToSelectedRangeAndMatchingRanges_SweepsEveryGroupedVisibleSheetAndUndoRestoresAll()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;

        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var summaryB1 = new CellAddress(summary.Id, 1, 2);
        var detailsA1 = new CellAddress(details.Id, 1, 1);
        var detailsB1 = new CellAddress(details.Id, 1, 2);
        var hiddenA1 = new CellAddress(hidden.Id, 1, 1);

        var originalRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        };

        var session = CreateSession(workbook);

        // Seed the SAME rule settings on A1 and B1 of the (not-yet-grouped) active sheet, mirroring
        // a rule that was previously applied broadly, plus a decoy on the hidden sheet.
        session.SelectCell(summaryA1);
        session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();
        session.SelectCell(summaryB1);
        session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();

        // Now group all visible sheets (Summary + Details; Hidden is excluded) and seed the matching
        // rule on Details too, so the sweep has something to find there as well.
        session.SelectAllVisibleSheets();
        session.IsWorkbookGrouped.Should().BeTrue();
        session.SelectSheetPreservingGroup(details.Id);
        session.SelectRange(new GridRange(detailsA1, detailsB1));
        session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();

        var existingRule = summary.DataValidations[0];
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "20"
        };

        // Act: select only Summary!A1 (as if the dialog was opened for that cell's rule) and run the
        // "apply to same settings" sweep while Summary + Details are grouped.
        session.SelectSheetPreservingGroup(summary.Id);
        session.SelectCell(summaryA1);
        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        outcome.Mutated.Should().BeTrue();

        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryA1) && r.Formula2 == "20");
        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryB1) && r.Formula2 == "20");
        summary.DataValidations.Should().NotContain(r => r.Formula2 == "10");

        details.DataValidations.Should().Contain(
            r => r.AppliesTo.Contains(detailsA1) && r.Formula2 == "20",
            "Details is grouped with Summary, so the sweep must reach its matching rule too");
        details.DataValidations.Should().NotContain(r => r.Formula2 == "10");

        hidden.DataValidations.Should().BeEmpty("the hidden sheet was never grouped or given a rule");

        // The whole grouped sweep must be a single undoable composite.
        session.CanUndo.Should().BeTrue();
        session.UndoLastEdit().Success.Should().BeTrue();
        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryA1) && r.Formula2 == "10");
        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryB1) && r.Formula2 == "10");
        details.DataValidations.Should().Contain(r => r.AppliesTo.Contains(detailsA1) && r.Formula2 == "10");

        session.RedoLastEdit().Success.Should().BeTrue();
        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryA1) && r.Formula2 == "20");
        details.DataValidations.Should().Contain(r => r.AppliesTo.Contains(detailsA1) && r.Formula2 == "20");
    }

    [Fact]
    public void ApplyDataValidationToSelectedRangeAndMatchingRanges_UngroupedSheets_OnlyAffectsActiveSheet()
    {
        var workbook = CreateWorkbook();
        var summary = workbook.Sheets.Single();
        var details = workbook.AddSheet("Details");

        var summaryA1 = new CellAddress(summary.Id, 1, 1);
        var detailsA1 = new CellAddress(details.Id, 1, 1);

        var originalRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        };

        var session = CreateSession(workbook);
        session.SelectCell(summaryA1);
        session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();
        session.SelectSheet(details.Id);
        session.SelectCell(detailsA1);
        session.ApplyDataValidationToSelectedRange(originalRule).Success.Should().BeTrue();

        session.IsWorkbookGrouped.Should().BeFalse();

        session.SelectSheet(summary.Id);
        var existingRule = summary.DataValidations[0];
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "20"
        };

        session.SelectCell(summaryA1);
        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        summary.DataValidations.Should().Contain(r => r.AppliesTo.Contains(summaryA1) && r.Formula2 == "20");
        details.DataValidations.Should().Contain(
            r => r.AppliesTo.Contains(detailsA1) && r.Formula2 == "10",
            "sheets are not grouped, so the sweep must stay confined to the active sheet");
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(
                workbook,
                "Book.fxl",
                "Opened .fxl.",
                IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
