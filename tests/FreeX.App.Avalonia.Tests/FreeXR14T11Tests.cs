using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-14 review finding (bucket T11):
///
///   R14-accessibility-automation-1 - a failed formula-bar commit (CommitFormulaBox) set
///     _statusText.Text directly WITHOUT calling EnsureStatusTextLiveRegion(), unlike every other
///     result.ErrorMessage-driven failure path in the Avalonia shell which goes through
///     ShowEditIssue. Because _statusText only ever becomes an AutomationLiveSetting.Polite live
///     region lazily (inside EnsureStatusTextLiveRegion, called by ShowEditIssue/ShowSaveIssue/
///     ShowOpenIssue/ShowExportIssue), a screen-reader user who has not yet hit any OTHER edit
///     issue in the session gets no announcement at all when a formula-bar commit is rejected
///     (e.g. by sheet protection) and can believe the edit succeeded.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class FreeXR14T11Tests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task CommitFormulaBox_OnRejectedEdit_AnnouncesFailureViaStatusTextLiveRegion()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var address = new CellAddress(sheet.Id, 1, 1);
            window.Session.SelectCell(address);

            // Protect the sheet (default cell style is Locked) so the commit is rejected by
            // CommandGuards.RejectSheetProtected — a real, no-modal-dialog failure path, exactly like
            // a data-validation-restricted cell rejecting an Enter commit.
            sheet.IsProtected = true;

            // Sanity: this window has never hit ANY prior edit issue, so if the live region were only
            // ever applied lazily by some OTHER path, it would still be unset here.
            global::Avalonia.Automation.AutomationProperties.GetLiveSetting(window.StatusTextForTest)
                .Should().NotBe(global::Avalonia.Automation.AutomationLiveSetting.Polite,
                    "test setup sanity: the live region must not already be applied before the commit");

            window.BeginFormulaEditForTest(address, "hello");
            window.RaiseFormulaBoxKeyDownForTest(new KeyEventArgs { Key = Key.Enter, KeyModifiers = KeyModifiers.None });

            global::Avalonia.Automation.AutomationProperties.GetLiveSetting(window.StatusTextForTest)
                .Should().Be(global::Avalonia.Automation.AutomationLiveSetting.Polite,
                    "a rejected formula-bar commit must mark _statusText as a Polite live region so a " +
                    "screen reader announces the rejection — previously CommitFormulaBox's failure " +
                    "branch bypassed EnsureStatusTextLiveRegion entirely");
            window.StatusTextForTest.Text.Should().Be("The sheet is protected.",
                "the rejection message must still reach the visible status text as before");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }
}
