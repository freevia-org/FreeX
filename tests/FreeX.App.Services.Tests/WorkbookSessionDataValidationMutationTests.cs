using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionDataValidationMutationTests
{
    [Fact]
    public void ApplyDataValidationToSelectedRange_AppliesRulePreservesSelectionAndUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        var range = new GridRange(a1, b2);
        var session = CreateSession(workbook);
        session.SelectRange(range);

        var result = session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "Yes,No",
            AllowBlank = false,
            ErrorTitle = "Pick one"
        });

        result.Success.Should().BeTrue();
        result.Mutated.Should().BeTrue();
        result.AffectedCells.Should().BeEmpty();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(range);
        sheet.DataValidations.Should().ContainSingle().Which.Should().Match<DataValidation>(rule =>
            rule.AppliesTo == range &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Yes,No" &&
            !rule.AllowBlank &&
            rule.ErrorTitle == "Pick one");

        session.UndoLastEdit().Success.Should().BeTrue();
        session.CanRedo.Should().BeTrue();
        sheet.DataValidations.Should().BeEmpty();
        session.SelectedRange.Should().Be(range);

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == range &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Yes,No");
    }

    [Fact]
    public void ApplyDataValidationToSelectedRange_DoesNotMarkDirtyWhenRuleAlreadyMatches()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(a1, a1);
        var existing = new DataValidation
        {
            AppliesTo = range,
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9",
            ErrorMessage = "Use 1 through 9."
        };
        sheet.DataValidations.Add(existing);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var result = session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "9",
            ErrorMessage = "Use 1 through 9."
        });

        result.Success.Should().BeTrue();
        result.Mutated.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.DataValidations.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    [Fact]
    public void ClearSelectedRangeDataValidation_ClearsRulePreservesSelectionAndUndoRedo()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var range = new GridRange(a1, b1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "Open,Closed"
        });
        var session = CreateSession(workbook);
        session.SelectRange(range);

        var result = session.ClearSelectedRangeDataValidation();

        result.Success.Should().BeTrue();
        result.Mutated.Should().BeTrue();
        session.IsDirty.Should().BeTrue();
        session.CanUndo.Should().BeTrue();
        session.ActiveCell.Should().Be(a1);
        session.SelectedRange.Should().Be(range);
        sheet.DataValidations.Should().BeEmpty();

        session.UndoLastEdit().Success.Should().BeTrue();
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == range &&
            rule.Type == DvType.List &&
            rule.Formula1 == "Open,Closed");

        session.RedoLastEdit().Success.Should().BeTrue();
        sheet.DataValidations.Should().BeEmpty();
    }

    [Fact]
    public void ClearSelectedRangeDataValidation_DoesNotMarkDirtyWhenSelectionHasNoRule()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var result = session.ClearSelectedRangeDataValidation();

        result.Success.Should().BeTrue();
        result.Mutated.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.DataValidations.Should().BeEmpty();
    }

    [Fact]
    public void ApplyAndClearDataValidation_RejectProtectedSheetWithoutMarkingDirty()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(a1, a1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "Keep,Me"
        });
        sheet.IsProtected = true;
        var session = CreateSession(workbook);
        session.SelectCell(a1);

        var apply = session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.List,
            Formula1 = "New,Rule"
        });
        var clear = session.ClearSelectedRangeDataValidation();

        apply.Success.Should().BeFalse();
        apply.ErrorMessage.Should().Contain("protected");
        apply.Mutated.Should().BeFalse();
        clear.Success.Should().BeFalse();
        clear.ErrorMessage.Should().Contain("protected");
        clear.Mutated.Should().BeFalse();
        session.IsDirty.Should().BeFalse();
        session.CanUndo.Should().BeFalse();
        sheet.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == range &&
            rule.Formula1 == "Keep,Me");
    }

    [Fact]
    public void ApplyDataValidationToSelectedRange_PropagatesAcrossGroupedSheetsAndUndoRestores()
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
        var summaryRange = new GridRange(summaryA1, summaryB1);
        var detailsRange = new GridRange(detailsA1, detailsB1);
        var hiddenRange = new GridRange(hiddenA1, new CellAddress(hidden.Id, 1, 2));
        var session = CreateSession(workbook);
        session.SelectAllVisibleSheets();
        session.SelectRange(summaryRange);

        var result = session.ApplyDataValidationToSelectedRange(new DataValidation
        {
            Type = DvType.Custom,
            Formula1 = "=A1<>\"\"",
            PromptTitle = "Required"
        });

        result.Success.Should().BeTrue();
        result.Mutated.Should().BeTrue();
        session.IsWorkbookGrouped.Should().BeTrue();
        session.SelectedRange.Should().Be(summaryRange);
        summary.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == summaryRange &&
            rule.Formula1 == "=A1<>\"\"" &&
            rule.PromptTitle == "Required");
        details.DataValidations.Should().ContainSingle(rule =>
            rule.AppliesTo == detailsRange &&
            rule.Formula1 == "=A1<>\"\"" &&
            rule.PromptTitle == "Required");
        hidden.DataValidations.Should().NotContain(rule => rule.AppliesTo == hiddenRange);

        session.UndoLastEdit().Success.Should().BeTrue();
        summary.DataValidations.Should().BeEmpty();
        details.DataValidations.Should().BeEmpty();
        hidden.DataValidations.Should().BeEmpty();
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
