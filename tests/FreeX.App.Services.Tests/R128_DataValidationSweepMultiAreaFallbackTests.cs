using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression guard for the r128 finding in
/// <see cref="WorkbookSession.ApplyDataValidationToSelectedRangeAndMatchingRanges"/> (line ~3306):
/// when the "Apply these changes to all other cells with the same settings" checkbox is checked but
/// no EXISTING data-validation range on the sheet matches <c>existingRule</c>'s settings (matches.Count
/// == 0), the method fell back to applying the edited rule to only the single active
/// <c>SelectedRange</c>, silently dropping every other area of a Ctrl+click multi-area
/// <c>SelectedRanges</c> selection. Excel's checkbox, applied against a multi-area selection, still
/// validates every area. Compare the sibling, non-sweep apply path
/// (<c>CreateSetSelectedRangeDataValidationCommand</c>, used by
/// <see cref="WorkbookSession.ApplyDataValidationToSelectedRange"/>), which already folds every area of
/// <c>GetCurrentSelectedRanges()</c> into one rule's AppliesTo+AdditionalRanges.
/// </summary>
public sealed class R128_DataValidationSweepMultiAreaFallbackTests
{
    [Fact]
    public void ApplyDataValidationToSelectedRangeAndMatchingRanges_NoExistingMatch_MultiAreaSelection_CoversEveryArea()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var a1Range = new GridRange(a1, a1);
        var c1Range = new GridRange(c1, c1);

        var session = CreateSession(workbook);

        // Ctrl+click-style multi-area selection: A1 primary, C1 additional. Neither cell has any
        // existing data validation, so existingRule below will never match anything already on the
        // sheet -- this forces the matches.Count == 0 fallback path under test.
        session.SelectRanges(a1Range, [a1Range, c1Range]);

        var existingRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        };
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "20"
        };

        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        outcome.Mutated.Should().BeTrue();

        sheet.DataValidations.Should().Contain(
            r => (r.AppliesTo.Contains(a1) || r.AdditionalRanges.Any(ar => ar.Contains(a1))) && r.Formula2 == "20",
            "the primary area of the multi-area selection must receive the new rule");
        sheet.DataValidations.Should().Contain(
            r => (r.AppliesTo.Contains(c1) || r.AdditionalRanges.Any(ar => ar.Contains(c1))) && r.Formula2 == "20",
            "the SECOND (non-primary) area of the multi-area selection must also receive the new rule -- " +
            "this is exactly what the fallback used to drop by reading only SelectedRange");
    }

    /// <summary>
    /// No-regression sibling: the plain single-area selection case (the overwhelmingly common path)
    /// must keep working through the same fallback branch.
    /// </summary>
    [Fact]
    public void ApplyDataValidationToSelectedRangeAndMatchingRanges_NoExistingMatch_SingleAreaSelection_StillApplies()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var existingRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10"
        };
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "20"
        };

        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        outcome.Mutated.Should().BeTrue();
        sheet.DataValidations.Should().Contain(r => r.AppliesTo.Contains(a1) && r.Formula2 == "20");
        sheet.DataValidations.Should().ContainSingle();
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
