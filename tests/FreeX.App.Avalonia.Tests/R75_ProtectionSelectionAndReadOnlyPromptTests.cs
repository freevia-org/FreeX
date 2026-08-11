using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using Free.Shared.AppServices;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for two round-75 app-protection findings:
///
///   R75-services-protection-security-4-1: <see cref="FreeX.Core.Commands.CommandGuards.CanSelectCell"/>
///   already existed in Core.Commands but was never called from this shell, so a protected sheet
///   with "Select locked cells" unchecked still let the user click or arrow onto a locked cell.
///   <c>MainWindow.SelectClickedCell</c> now refuses a plain click onto a non-selectable locked
///   cell, and <c>MainWindow.NavigateActiveCell</c> now skips over a locked cell during arrow-key
///   navigation instead of landing on it.
///
///   R75-services-protection-security-4-2: a workbook saved with "Read-Only Recommended" or a
///   write-reservation password opened fully editable on this shell with no prompt at all (the WPF
///   host already prompted). <c>MainWindow.ApplyReadOnlyRecommendedPromptIfNeeded</c> now mirrors
///   the WPF host's logic, prompting via the injectable <c>ReadOnlyRecommendedPromptOverrideForTest</c>
///   seam and marking <c>_isWorkbookReadOnly</c> on accept.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R75_ProtectionSelectionAndReadOnlyPromptTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    // ── SelectClickedCell (plain click) ───────────────────────────────────────

    [Fact]
    public async Task SelectClickedCell_LockedCellOnProtectedSheetWithoutSelectLockedCells_DoesNotSelectIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionClickFixture");
            window.Session.SelectSheet(sheet.Id);

            var start = new CellAddress(sheet.Id, 1, 1);
            var locked = new CellAddress(sheet.Id, 3, 3); // default cell style is Locked = true
            window.Session.SelectCell(start);

            sheet.IsProtected = true;
            sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);

            window.SelectClickedCell(locked, KeyModifiers.None);

            window.Session.ActiveCell.Should().Be(start,
                "a plain click onto a locked cell must be refused when Select Locked Cells is unchecked");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectClickedCell_UnlockedCellOnProtectedSheetWithoutSelectLockedCells_SelectsIt()
    {
        // Sibling/no-regression: an unlocked cell must stay selectable regardless of Select Locked
        // Cells (only "Select unlocked cells" governs it, and that stays on by default).
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionClickUnlockedFixture");
            window.Session.SelectSheet(sheet.Id);

            var unlockedStyleId = window.Session.Workbook.RegisterStyle(new CellStyle { Locked = false });
            var target = new CellAddress(sheet.Id, 3, 3);
            sheet.SetStyleOnly(target.Row, target.Col, unlockedStyleId);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            sheet.IsProtected = true;
            sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);

            window.SelectClickedCell(target, KeyModifiers.None);

            window.Session.ActiveCell.Should().Be(target, "an unlocked cell must remain selectable");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectClickedCell_UnprotectedSheet_SelectsALockedCell()
    {
        // Sibling/no-regression: an unprotected sheet must remain fully selectable.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionClickUnprotFixture");
            window.Session.SelectSheet(sheet.Id);

            var target = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            window.SelectClickedCell(target, KeyModifiers.None);

            window.Session.ActiveCell.Should().Be(target, "an unprotected sheet must not restrict selection at all");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task SelectClickedCell_SelectLockedCellsPermissionEnabled_SelectsALockedCell()
    {
        // Sibling/no-regression: checking "Select locked cells" (the default) must keep locked
        // cells selectable on a protected sheet.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionClickAllowedFixture");
            window.Session.SelectSheet(sheet.Id);

            var target = new CellAddress(sheet.Id, 3, 3);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            sheet.IsProtected = true;
            // Sheet.ProtectionPermissions defaults to [SelectLockedCells, SelectUnlockedCells].

            window.SelectClickedCell(target, KeyModifiers.None);

            window.Session.ActiveCell.Should().Be(target,
                "Select Locked Cells being checked must keep locked cells selectable");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── NavigateActiveCell (arrow-key navigation) ─────────────────────────────

    [Fact]
    public async Task NavigateActiveCell_RightArrow_ProtectedSheetWithLockedCellBetween_SkipsToNextSelectableCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionNavSkipFixture");
            window.Session.SelectSheet(sheet.Id);

            var unlockedStyleId = window.Session.Workbook.RegisterStyle(new CellStyle { Locked = false });
            // A1 (start) and C1 (expected landing target) unlocked; B1 stays locked (the default
            // cell style) in between, so a plain click there would be refused.
            sheet.SetStyleOnly(1, 1, unlockedStyleId);
            sheet.SetStyleOnly(1, 3, unlockedStyleId);
            sheet.IsProtected = true;
            sheet.ProtectionPermissions.Remove(SheetProtectionPermission.SelectLockedCells);

            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 3),
                "Right-arrow navigation on a protected sheet must skip the locked B1 cell and land on the next selectable cell C1");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NavigateActiveCell_RightArrow_UnprotectedSheet_MovesToImmediatelyAdjacentCell()
    {
        // Sibling/no-regression: an unprotected sheet must still move one cell at a time.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionNavNoRegressionFix");
            window.Session.SelectSheet(sheet.Id);
            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 2),
                "an unprotected sheet must navigate one cell at a time");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NavigateActiveCell_RightArrow_SelectLockedCellsPermissionEnabled_MovesToImmediatelyAdjacentLockedCell()
    {
        // Sibling/no-regression: with Select Locked Cells checked (the default), a protected sheet
        // must still allow navigating straight onto a locked cell.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("ProtectionNavAllowedFixture");
            window.Session.SelectSheet(sheet.Id);

            var unlockedStyleId = window.Session.Workbook.RegisterStyle(new CellStyle { Locked = false });
            sheet.SetStyleOnly(1, 1, unlockedStyleId);
            sheet.IsProtected = true;
            // Sheet.ProtectionPermissions defaults to [SelectLockedCells, SelectUnlockedCells].

            window.Session.SelectCell(new CellAddress(sheet.Id, 1, 1));

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Right, KeyModifiers = KeyModifiers.None });

            window.Session.ActiveCell.Should().Be(new CellAddress(sheet.Id, 1, 2),
                "Select Locked Cells being checked must allow navigating straight onto the locked B1 cell");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
            return true;
        }, CancellationToken.None);
    }

    // ── ApplyReadOnlyRecommendedPromptIfNeeded (R75-services-protection-security-4-2) ────────────

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_ReadOnlyRecommendedWorkbook_PromptsAndMarksReadOnlyOnAccept()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = new WorkbookFileSharingModel { ReadOnlyRecommended = true };

                var promptedBodies = new List<string>();
                window.ReadOnlyRecommendedPromptOverrideForTest = body =>
                {
                    promptedBodies.Add(body);
                    return UserMessageResult.Yes;
                };

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                promptedBodies.Should().HaveCount(1,
                    "a ReadOnlyRecommended workbook must prompt exactly once on open");
                window.IsWorkbookReadOnlyForTest.Should().BeTrue(
                    "accepting the prompt (Yes) must mark the session read-only");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    // ── Write-reservation password (round-134 SECURITY fix) ────────────────────
    //
    // Before this fix, a write-reservation-password workbook routed through the same plain Yes/No
    // "open read-only?" prompt as ReadOnlyRecommended -- the password itself was never asked for or
    // checked, so declining ("No") granted full write access with zero verification. It now prompts
    // for the actual password via the dedicated ReservationPasswordPromptOverrideForTest seam and
    // verifies it with ProtectionPasswordHelper.VerifyStoredPassword.

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_ReservationPasswordWorkbook_CorrectPassword_UnlocksEditableSession()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };

                var promptCount = 0;
                window.ReservationPasswordPromptOverrideForTest = _ =>
                {
                    promptCount++;
                    return "secret";
                };

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                promptCount.Should().Be(1, "a write-reservation-password workbook must prompt for the password on open");
                window.IsWorkbookReadOnlyForTest.Should().BeFalse(
                    "typing the correct write-reservation password must unlock a fully editable session");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_ReservationPasswordWorkbook_WrongPassword_OpensReadOnly()
    {
        // THE security case: before the round-134 fix, declining/answering "No" to a plain Yes/No
        // question granted full write access with no password ever checked. A wrong password must
        // now fall back to a genuinely read-only session, matching Excel.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };
                window.ReservationPasswordPromptOverrideForTest = _ => "not-the-password";
                var noticeCount = 0;
                window.ReservationPasswordIncorrectNoticeOverrideForTest = () => noticeCount++;

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                window.IsWorkbookReadOnlyForTest.Should().BeTrue(
                    "a wrong write-reservation password must fall back to a read-only session, not grant write access");
                noticeCount.Should().Be(1, "a wrong password must surface an 'opened as read-only' notice");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_ReservationPasswordWorkbook_CancelledPrompt_OpensReadOnly()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = new WorkbookFileSharingModel { ReservationPassword = "secret" };
                window.ReservationPasswordPromptOverrideForTest = _ => null;
                var noticeCount = 0;
                window.ReservationPasswordIncorrectNoticeOverrideForTest = () => noticeCount++;

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                window.IsWorkbookReadOnlyForTest.Should().BeTrue(
                    "cancelling the write-reservation password prompt must fall back to a read-only session");
                noticeCount.Should().Be(0,
                    "a plain Cancel already communicates its own intent and should not also show an 'incorrect password' notice");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_ReadOnlyRecommendedWorkbook_DeclinedPrompt_StaysEditable()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = new WorkbookFileSharingModel { ReadOnlyRecommended = true };
                window.ReadOnlyRecommendedPromptOverrideForTest = _ => UserMessageResult.No;

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                window.IsWorkbookReadOnlyForTest.Should().BeFalse(
                    "declining (No) the prompt must leave the session editable");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ApplyReadOnlyRecommendedPromptIfNeeded_NormalWorkbook_DoesNotPrompt()
    {
        // Sibling/no-regression: a workbook with no FileSharing metadata (or with
        // ReadOnlyRecommended explicitly false and no reservation password) must open without any
        // prompt, exactly as before this fix.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var workbook = window.Session.Workbook;
                workbook.FileSharing = null;

                var promptCount = 0;
                window.ReadOnlyRecommendedPromptOverrideForTest = _ =>
                {
                    promptCount++;
                    return UserMessageResult.Yes;
                };

                window.ApplyReadOnlyRecommendedPromptIfNeededForTest(workbook);

                promptCount.Should().Be(0, "a normal workbook must not prompt at all");
                window.IsWorkbookReadOnlyForTest.Should().BeFalse();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
        }, CancellationToken.None);
    }
}
