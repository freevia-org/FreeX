using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;
using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R161-freex-data-validation-ui-F2: a Warning/Information ("AskToContinue") data-validation
/// alert's keyboard-typed commit path (Formula Bar Enter -&gt; <c>CommitFormulaBox</c>) used to
/// leave the formula edit session dangling on ANY decline -- <c>_session.FormulaEditAddress</c>
/// stayed set and the Formula Bar kept showing the rejected text -- regardless of whether the user
/// answered Cancel (which Excel discards) or No (which Excel keeps open for further editing). This
/// exercises the real production KeyDown handler (<c>RaiseFormulaBoxKeyDownForTest</c> -&gt;
/// <c>FormulaBox_KeyDown</c> -&gt; <c>CommitFormulaBox</c>), the same path a physical Enter key
/// takes from the Formula Bar, mirroring R73's DV-prompt injection seam.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R161_DataValidationDeclineFormulaBoxRevertTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task WarningStyle_CancelDecision_DiscardsEntryAndRevertsFormulaBar()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.Cancel;

            window.Session.BeginFormulaEdit(address);
            window.FormulaBoxTextForTest = "99";
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.Session.FormulaEditAddress.Should().BeNull(
                "Cancel must discard the invalid entry and end the edit session, exactly like Escape");
            window.Session.ActiveSheet.GetCell(address).Should().BeNull(
                "the invalid 99 must never be committed to the model");
            window.FormulaBoxTextForTest.Should().NotBe("99",
                "the Formula Bar must revert to the cell's prior (blank) committed value, not keep showing the rejected text");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling/adjacent case: a Warning-style No answer must NOT be treated like Cancel. Excel
    /// leaves the invalid text in place so the user can keep fixing it, so the formula edit
    /// session must stay open and the Formula Bar must keep showing what was typed.
    /// </summary>
    [Fact]
    public async Task WarningStyle_NoDecision_KeepsEditSessionAndFormulaBoxTextForFurtherEditing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Warning);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.No;

            window.Session.BeginFormulaEdit(address);
            window.FormulaBoxTextForTest = "99";
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.Session.FormulaEditAddress.Should().Be(address,
                "No must leave the user mid-edit so they can fix the invalid entry, unlike Cancel");
            window.Session.ActiveSheet.GetCell(address).Should().BeNull(
                "the invalid 99 must still never be committed to the model");
            window.FormulaBoxTextForTest.Should().Be("99",
                "No must keep the rejected text in the Formula Bar for the user to correct, not revert it");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task InformationStyle_CancelDecision_DiscardsEntryAndRevertsFormulaBar()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var address = SeedWholeNumberBetweenRule(window, DvAlertStyle.Information);
            window.DataValidationPromptOverrideForTest = _ => UserMessageResult.Cancel;

            window.Session.BeginFormulaEdit(address);
            window.FormulaBoxTextForTest = "99";
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            window.Session.FormulaEditAddress.Should().BeNull(
                "Information style only offers OK/Cancel -- Cancel must discard exactly like Escape");
            window.FormulaBoxTextForTest.Should().NotBe("99",
                "the Formula Bar must revert to the cell's prior (blank) committed value");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static CellAddress SeedWholeNumberBetweenRule(MainWindow window, DvAlertStyle alertStyle)
    {
        // Mirrors R73_DataValidationAskToContinueCommitTests' fixture pattern: a freshly added,
        // otherwise-empty sheet guarantees the target cell starts genuinely blank.
        var sheet = window.Session.Workbook.AddSheet("DvDeclineFormulaBoxFixture");
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
