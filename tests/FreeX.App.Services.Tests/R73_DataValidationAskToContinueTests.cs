using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R73-dv-warning-info-avalonia: <see cref="WorkbookSession.CommitCellText"/> enforced Stop-style
/// data validation (R32) but a Warning/Information ("AskToContinue") rule had no seam to ask the
/// host anything -- the violation was silently accepted, unlike the WPF host's
/// <c>TryCreateCellFromEntryText</c>, which prompts via <c>IUserMessageService.ShowMessage</c>
/// (Warning: Yes/No/Cancel, Information: OK/Cancel) before deciding whether to commit. This adds
/// <see cref="WorkbookSession.DataValidationPromptResolver"/> so a host can supply that decision:
/// Yes/OK still commits the invalid value, No/Cancel does not. A Stop-style rule must still reject
/// outright (never consulting the resolver), and a valid entry must never even invoke the
/// resolver -- it must commit with no prompt.
/// </summary>
public sealed class R73_DataValidationAskToContinueTests
{
    [Fact]
    public void CommitCellText_WarningStyle_YesDecision_CommitsInvalidValue()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Warning);
        session.DataValidationPromptResolver = _ => UserMessageResult.Yes;

        var result = session.CommitCellText("999");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(999));
    }

    [Fact]
    public void CommitCellText_WarningStyle_NoDecision_DoesNotCommit()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Warning);
        session.DataValidationPromptResolver = _ => UserMessageResult.No;

        var result = session.CommitCellText("999");

        result.Success.Should().BeFalse(
            "a Warning-style DV rule's No answer must leave the invalid entry uncommitted");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_WarningStyle_CancelDecision_DoesNotCommit()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Warning);
        session.DataValidationPromptResolver = _ => UserMessageResult.Cancel;

        var result = session.CommitCellText("999");

        result.Success.Should().BeFalse(
            "a Warning-style DV rule's Cancel answer must leave the invalid entry uncommitted");
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_InformationStyle_OkDecision_CommitsInvalidValue()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Information);
        session.DataValidationPromptResolver = _ => UserMessageResult.Ok;

        var result = session.CommitCellText("999");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(999));
    }

    [Fact]
    public void CommitCellText_InformationStyle_CancelDecision_DoesNotCommit()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Information);
        session.DataValidationPromptResolver = _ => UserMessageResult.Cancel;

        var result = session.CommitCellText("999");

        result.Success.Should().BeFalse();
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_StopStyle_StillRejects_WithoutConsultingResolver()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Stop);
        session.DataValidationPromptResolver = _ =>
            throw new InvalidOperationException("A Stop-alert rule must never consult the prompt resolver.");

        var result = session.CommitCellText("999");

        result.Success.Should().BeFalse("a Stop-alert data validation rule must still block outright (R32)");
        sheet.GetCell(address).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_ValidEntryUnderWarningStyleRule_CommitsWithoutConsultingResolver()
    {
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Warning);
        session.DataValidationPromptResolver = _ =>
            throw new InvalidOperationException("A satisfied DV rule must never invoke the prompt resolver.");

        var result = session.CommitCellText("5");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(5));
    }

    [Fact]
    public void CommitCellText_WarningStyle_WithoutResolverWired_KeepsPassThroughBehavior()
    {
        // A host that hasn't opted in (DataValidationPromptResolver left null) must keep the
        // session's original behavior for AskToContinue rules: silently accepted, matching the
        // documented pass-through this session has always had.
        var (session, sheet, address) = CreateSessionWithWholeNumberBetweenRule(1, 10, DvAlertStyle.Warning);

        var result = session.CommitCellText("999");

        result.Success.Should().BeTrue(result.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(999));
    }

    private static (WorkbookSession Session, Sheet Sheet, CellAddress Address)
        CreateSessionWithWholeNumberBetweenRule(int min, int max, DvAlertStyle alertStyle)
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
            AlertStyle = alertStyle,
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
