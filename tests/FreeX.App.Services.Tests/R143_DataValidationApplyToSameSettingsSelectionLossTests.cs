using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R143-freex-datavalidation-DV-2: <see cref="WorkbookSession.ApplyDataValidationToSelectedRangeAndMatchingRanges"/>
/// (the shared session method the Avalonia shell drives for "Apply these changes to all other
/// cells with the same settings") had the identical selection-loss defect as the WPF host's own
/// <c>CreateDataValidationCommand</c> (DV-1, see <c>R143_DataValidationApplyToSameSettingsSelectionLossTests</c>
/// in FreeX.App.Host.Tests): the current selection was only folded in as a fallback when NO
/// existing rule matched <c>existingRule</c>'s settings. Since <c>existingRule</c> itself always
/// satisfies <see cref="DataValidation.HasSameSettings"/> trivially, <c>matches</c> was never empty
/// on the sheet that owns it, so widening the selection past the rule being edited silently left
/// the newly-selected cells with no validation at all.
///
/// The existing regression suite (<c>tests/FreeX.App.Avalonia.Tests/DataValidationApplyToSameSettingsTests.cs</c>)
/// only ever selects exactly the edited rule's own <see cref="DataValidation.AppliesTo"/> before
/// running the sweep, so the selection-widened case was untested -- these tests close that gap by
/// calling the shared session method directly (no Avalonia UI needed; the method lives in
/// FreeX.App.Services, shared by both shells) with a selection that WIDENS past the edited rule.
/// </summary>
public sealed class R143_DataValidationApplyToSameSettingsSelectionLossTests
{
    [Fact]
    public void SelectionWidenedPastEditedRule_WidenedSelectionReceivesTheNewRule()
    {
        var (session, sheet) = CreateSession();
        var sheetId = sheet.Id;

        var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
        session.SelectRange(a1a10);
        session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
        }).Success.Should().BeTrue();
        var existingRule = sheet.DataValidations.Should().ContainSingle().Which;
        existingRule.AppliesTo.Should().Be(a1a10);

        // Widen the selection to A1:A20 (past the rule's own old range) and edit its upper bound.
        var a1a20 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 1));
        session.SelectRange(a1a20);
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        };

        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var rules = sheet.DataValidations;
        rules.Should().Contain(r => r.AppliesTo == a1a20 && r.Formula2 == "100",
            "the widened selection A1:A20 -- what the user actually selected -- must receive the edited rule");
        rules.Should().NotContain(r => r.Formula2 == "10",
            "the stale rule must not survive under its old, narrower A1:A10 footprint once the wider " +
            "selection has been given the new settings");
    }

    [Fact]
    public void MatchedRuleHasDisjointAdditionalRange_AdditionalRangeSurvivesWithNewSettings()
    {
        var (session, sheet) = CreateSession();
        var sheetId = sheet.Id;

        // A single rule spanning two disjoint areas: A1:A10 (AppliesTo) plus C1:C10
        // (AdditionalRanges) -- the shape Excel produces for one validation rule applied to a
        // Ctrl+click multi-area selection.
        var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
        var c1c10 = new GridRange(new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 10, 3));
        var existingRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AppliesTo = a1a10,
        };
        existingRule.AdditionalRanges.Add(c1c10);
        sheet.DataValidations.Add(existingRule);

        // Re-select only A1:A20 (widening past A1:A10, but NOT touching C1:C10) and edit the rule.
        var a1a20 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 20, 1));
        session.SelectRange(a1a20);
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "100",
        };

        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var rules = sheet.DataValidations;
        var c1 = new CellAddress(sheetId, 1, 3);
        rules.Should().Contain(r => RangeContains(r, c1) && r.Formula2 == "100",
            "C1:C10 shared the edited rule's old settings via AdditionalRanges and was not " +
            "reselected -- it must survive under the new settings instead of losing its validation " +
            "outright");
        rules.Should().Contain(r => r.AppliesTo == a1a20 && r.Formula2 == "100",
            "the widened selection A1:A20 must also receive the edited rule");
        rules.Should().NotContain(r => r.Formula2 == "10",
            "no range should be left behind on the stale rule once the sweep runs");
    }

    [Fact]
    public void UnrelatedRuleWithDifferentSettings_IsLeftUntouched()
    {
        // No-regression sibling (mirrors the existing Avalonia suite's own coverage): a differently
        // typed rule elsewhere must never be swept in by this method, widened selection or not.
        var (session, sheet) = CreateSession();
        var sheetId = sheet.Id;

        var a1a10 = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 10, 1));
        session.SelectRange(a1a10);
        session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
        }).Success.Should().BeTrue();
        var existingRule = sheet.DataValidations.Should().ContainSingle().Which;

        var c1 = new CellAddress(sheetId, 1, 3);
        session.SelectCell(c1);
        session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "X,Y,Z",
        }).Success.Should().BeTrue();

        session.SelectRange(a1a10);
        var editedRule = new DataValidation
        {
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "99",
        };

        var outcome = session.ApplyDataValidationToSelectedRangeAndMatchingRanges(editedRule, existingRule);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var rules = sheet.DataValidations;
        rules.Should().Contain(r => RangeContains(r, c1) && r.Type == DvType.List,
            "the unrelated List rule on C1 must survive the sweep untouched");
        rules.Should().Contain(r => r.AppliesTo == a1a10 && r.Formula2 == "99");
    }

    private static bool RangeContains(DataValidation rule, CellAddress address) =>
        rule.AppliesTo.Contains(address) || rule.AdditionalRanges.Any(r => r.Contains(address));

    private static (WorkbookSession Session, Sheet Sheet) CreateSession()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;

        var session = new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);
        return (session, sheet);
    }
}
