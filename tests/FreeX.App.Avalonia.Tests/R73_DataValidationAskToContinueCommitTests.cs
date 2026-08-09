using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R73-dv-warning-info-avalonia: a Warning/Information ("AskToContinue") data-validation rule had
/// no dialog seam on the Avalonia shell's cell-commit path -- <c>WorkbookSession.CommitCellText</c>
/// silently accepted the invalid entry without ever asking the user, unlike the WPF host's
/// <c>ShowOwnedMessage</c> prompt (Warning: Yes/No/Cancel; Information: OK/Cancel). The fix wires
/// <c>WorkbookSession.DataValidationPromptResolver</c> to <c>MainWindow.ResolveDataValidationPrompt</c>,
/// which checks the headless-injectable <see cref="MainWindow.DataValidationPromptOverrideForTest"/>
/// before falling back to a real owned dialog -- mirroring the shell's existing
/// <c>ConfirmSelectionMoveOverwriteOverrideForTest</c> seam pattern. These tests inject a canned
/// decision and drive the commit through <c>window.Session.CommitCellText</c>, exactly like the
/// existing R32 Stop-style regression guard.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R73_DataValidationAskToContinueCommitTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task WarningStyle_YesDecision_CommitsInvalidValue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.Yes;

            var result = window.Session.CommitCellText("999");

            result.Success.Should().BeTrue(result.ErrorMessage);
            window.Session.ActiveSheet.GetValue(address).Should().Be(new NumberValue(999));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WarningStyle_NoDecision_DoesNotCommit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.No;

            var result = window.Session.CommitCellText("999");

            result.Success.Should().BeFalse(
                "a Warning-style DV rule's No answer must leave the invalid entry uncommitted");
            window.Session.ActiveSheet.GetCell(address).Should().BeNull();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task WarningStyle_CancelDecision_DoesNotCommit()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.Cancel;

            var result = window.Session.CommitCellText("999");

            result.Success.Should().BeFalse(
                "a Warning-style DV rule's Cancel answer must leave the invalid entry uncommitted");
            window.Session.ActiveSheet.GetCell(address).Should().BeNull();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InformationStyle_OkDecision_CommitsInvalidValue()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Information);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.Ok;

            var result = window.Session.CommitCellText("999");

            result.Success.Should().BeTrue(result.ErrorMessage);
            window.Session.ActiveSheet.GetValue(address).Should().Be(new NumberValue(999));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StopStyle_StillRejects_RegardlessOfInjectedDecision()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Stop);
            window.DataValidationPromptOverrideForTest = _ =>
                throw new System.InvalidOperationException(
                    "A Stop-alert rule must never consult the data-validation prompt.");

            var result = window.Session.CommitCellText("999");

            result.Success.Should().BeFalse("a Stop-alert data validation rule must still block outright (R32)");
            window.Session.ActiveSheet.GetCell(address).Should().BeNull();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ValidEntry_CommitsWithNoPrompt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ =>
                throw new System.InvalidOperationException(
                    "A satisfied data validation rule must never trigger a prompt.");

            var result = window.Session.CommitCellText("5");

            result.Success.Should().BeTrue(result.ErrorMessage);
            window.Session.ActiveSheet.GetValue(address).Should().Be(new NumberValue(5));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static CellAddress SeedWholeNumberBetweenRule(MainWindow window, DvAlertStyle alertStyle)
    {
        // The default startup workbook's active sheet already carries sample content, so use a
        // freshly added, otherwise-empty sheet -- mirroring R16_avalonia_mw_Tests' pattern -- to
        // guarantee the target cell starts genuinely blank.
        var sheet = window.Session.Workbook.AddSheet("DvAskToContinueFixture");
        window.Session.SelectSheet(sheet.Id);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(address, address),
            Type = DvType.WholeNumber,
            Operator = DvOperator.Between,
            Formula1 = "1",
            Formula2 = "10",
            AlertStyle = alertStyle,
            ShowErrorMessage = true,
        });

        window.Session.SelectCell(address);
        return address;
    }
}
