using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R58-commands-dv-input-enforce-6-2: Circle Invalid Data circles did not auto-clear when the
/// flagged cell was subsequently corrected. Both shells now project the shared per-sheet circle
/// state owned by <see cref="WorkbookValidationCircleWorkflow"/>. This class retains coverage for
/// the compatibility helper <see cref="WorkbookSession.PruneCorrectedValidationCircles"/>, which
/// delegates the same portable prune policy.
/// </summary>
public sealed class R58ValidationCirclePruneTests
{
    [Fact]
    public void PruneCorrectedValidationCircles_DropsCellOnceItNoLongerViolatesItsRule()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10);

        // AlertStyle = Warning is not enforced by CommitCellText (only Stop blocks), so the
        // invalid entry commits -- matching how a real Circle Invalid Data scenario arises (data
        // that was invalid when loaded, pasted, or entered under a non-Stop rule).
        session.CommitCellText("50").Success.Should().BeTrue();

        var circled = DataValidationCirclePlanner.FindInvalidDataCells(session.Workbook, sheet);
        circled.Should().Contain(address, "the out-of-range entry must be flagged before the fix");

        // Correct the cell.
        session.CommitCellText("5").Success.Should().BeTrue();

        var pruned = WorkbookSession.PruneCorrectedValidationCircles(session.Workbook, sheet, circled);

        pruned.Should().NotContain(address,
            "Excel auto-clears a cell's circle the instant the flagged value is corrected");
        pruned.Should().BeEmpty();
        ReferenceEquals(pruned, circled).Should().BeFalse(
            "a real prune must return a new, shorter list rather than the stale reference");
    }

    [Fact]
    public void PruneCorrectedValidationCircles_KeepsStillInvalidCellsAndReturnsSameListWhenNothingChanged()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var stillInvalidAddress = new CellAddress(sheet.Id, 1, 1);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(stillInvalidAddress, stillInvalidAddress),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AlertStyle = DvAlertStyle.Warning,
            ShowErrorMessage = true
        });

        var session = CreateSession(workbook);
        session.SelectCell(stillInvalidAddress);
        session.CommitCellText("50").Success.Should().BeTrue();

        // A circled address that belongs to a different (inactive) sheet: the fresh scan only
        // covers the active sheet, so this entry must never be touched regardless of its content.
        var otherSheet = workbook.AddSheet("Sheet2");
        var otherSheetAddress = new CellAddress(otherSheet.Id, 1, 1);

        var circled = new List<CellAddress> { stillInvalidAddress, otherSheetAddress };

        // Nothing was corrected on the active sheet, so the still-invalid cell must remain, the
        // other-sheet entry is left alone on principle, and since no entry needed pruning the
        // helper must hand back the exact same list reference.
        var pruned = WorkbookSession.PruneCorrectedValidationCircles(workbook, sheet, circled);

        pruned.Should().BeEquivalentTo(circled);
        ReferenceEquals(pruned, circled).Should().BeTrue(
            "no-op prunes should be cheap to detect via reference equality");
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress Address)
        CreateSessionWithWholeNumberBetweenRule(int min, int max)
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var address = new CellAddress(sheet.Id, 1, 1);

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = min.ToString(),
            Formula2 = max.ToString(),
            AlertStyle = DvAlertStyle.Warning,
            ShowErrorMessage = true
        });

        var session = CreateSession(workbook);
        session.SelectCell(address);
        return (session, sheet, address);
    }

    private static WorkbookSession CreateSession(Workbook workbook) =>
        new WorkbookSessionFactory().Create(
            new StartupWorkbookLoadResult(workbook, "Book.fxl", "Opened .fxl.", IsFallback: false),
            viewportHeight: 240,
            viewportWidth: 320);

    private static Workbook CreateWorkbook(string name = "Book")
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        return workbook;
    }
}
