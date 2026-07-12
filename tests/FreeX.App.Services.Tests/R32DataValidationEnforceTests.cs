using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R32-commands-datavalidation-enforce-1: the Avalonia/host-agnostic commit path
/// (<see cref="WorkbookSession.CommitCellText"/>) never called into
/// <see cref="FreeX.Core.Commands.DataValidationService"/>, so a Stop-alert data validation rule
/// was purely decorative outside the WPF host (which enforces it in
/// MainWindow.Editing.cs's TryCreateCellFromEntryText). CommitCellText must now reject an entry
/// that violates a Stop-style rule -- mirroring the WPF host's Block behavior -- while still
/// committing entries that satisfy the rule (or that have no rule at all).
/// </summary>
public sealed class R32DataValidationEnforceTests
{
    [Fact]
    public void CommitCellText_RejectsEntryThatViolatesStopStyleRule()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10);

        var result = session.CommitCellText("999");

        result.Success.Should().BeFalse(
            "a Stop-alert data validation rule must block an out-of-range entry on the Avalonia/session path, matching the WPF host");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.GetCell(address).Should().BeNull(
            "the rejected entry must not be written to the sheet");
    }

    [Fact]
    public void CommitCellText_AcceptsValidEntryUnderStopStyleRule()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10);

        var result = session.CommitCellText("5");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void CommitCellText_CommitsNormallyWhenNoValidationRuleApplies()
    {
        var workbook = CreateWorkbook();
        var sheet = workbook.Sheets.Single();
        var address = new CellAddress(sheet.Id, 1, 1);
        var session = CreateSession(workbook);
        session.SelectCell(address);

        var result = session.CommitCellText("hello");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue("hello"));
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
            AlertStyle = DvAlertStyle.Stop,
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
