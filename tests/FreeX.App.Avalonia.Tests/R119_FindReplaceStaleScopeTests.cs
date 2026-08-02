using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R119-avalonia-findreplace-stale-scope (src/FreeX.App.Avalonia/MainWindow.cs,
/// src/FreeX.App.Avalonia/MainWindow.WindowManagement.cs, src/FreeX.App.Avalonia/MainWindow.ModelessDialogs.cs).
///
/// Before this fix, <c>ReplaceSession</c> (the single choke point every workbook-replacing path -- File >
/// New, Close Workbook, File > Open, and recovery-snapshot load -- funnels through) never touched the
/// modeless Find &amp; Replace dialog (<c>_findReplaceDialog</c>). That dialog captures its selection scope
/// exactly ONCE, at open time (<c>CaptureFindReplaceSelectionScopeAtOpen</c>), and reuses that frozen
/// <c>GridRange</c> list for every subsequent Find Next/Find All/Replace/Replace All call for as long as it
/// stays open. <c>GridRange.Contains</c> requires <c>addr.Sheet == Start.Sheet</c>, and a workbook
/// replacement always mints fresh <see cref="SheetId"/>s for the new document's sheets -- so once the
/// session was swapped while the dialog stayed open, the frozen scope's <c>Start.Sheet</c> could never again
/// equal any candidate's <c>.Sheet</c>, and every subsequent Find/Replace action would silently report zero
/// matches forever, even for text that is visibly present. The WPF host has always guarded against exactly
/// this (<c>MainWindow.WorkbookUiState.CloseFindReplaceDialogIfOpen</c>, called from every workbook-replacing
/// path) -- the Avalonia shell had no equivalent.
///
/// The fix adds <c>MainWindow.ModelessDialogs.CloseFindReplaceDialogIfOpen</c> and calls it from
/// <c>ReplaceSession</c>, mirroring the WPF host exactly: the dialog is closed (not merely reset) so a user
/// who wants to search the newly-opened/created workbook must reopen Find &amp; Replace, which captures a
/// fresh, correctly-scoped selection against the CURRENT session.
///
/// These tests drive the REAL production entry points directly: <c>ShowFindDialogAsync</c> (the actual
/// Ctrl+F handler) via reflection, exactly as <c>DialogInteractionValidationTests</c> does for the sibling
/// modeless-dialog contract tests, and <c>ReplaceSession</c> itself (internal, exercised via
/// <c>InternalsVisibleTo</c>) -- the identical method that <c>CreateNewWorkbookAsync</c>, <c>ResetToNewWorkbook</c>
/// (Close Workbook), <c>OpenWorkbookFromTargetAsync</c>, and the recovery-snapshot loader all call.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R119_FindReplaceStaleScopeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ReplaceSession_WhileFindReplaceDialogOpenWithCapturedScope_ClosesTheDialog()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                var originalSheet = window.Session.ActiveSheet;
                var originalSheetId = originalSheet.Id;
                originalSheet.SetCell(new CellAddress(originalSheetId, 1, 1), new TextValue("needle"));

                // A multi-cell selection at open time is exactly what makes
                // CaptureFindReplaceSelectionScopeAtOpen capture a non-null scope (Excel: Find All is
                // restricted to the pre-open selection) -- this is the precise condition the defect
                // describes, tied to the OLD workbook's SheetId.
                window.Session.SelectRange(new GridRange(
                    new CellAddress(originalSheetId, 2, 2),
                    new CellAddress(originalSheetId, 3, 3)));

                await InvokePrivateTaskAsync(window, "ShowFindDialogAsync");
                var dialog = FindOwnedFindReplaceWindow(window);
                dialog.IsVisible.Should().BeTrue(
                    "sanity check: the modeless Find & Replace dialog must actually be open before the swap");

                // Simulate File > New / File > Open / Close Workbook: a fresh session with fresh
                // SheetIds, deliberately still containing "needle" so a real (unscoped) search of the
                // new workbook would find it if the dialog were still usable.
                var newSession = new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: true);
                var newSheet = newSession.ActiveSheet;
                newSheet.SetCell(new CellAddress(newSheet.Id, 1, 1), new TextValue("needle"));
                newSheet.Id.Should().NotBe(originalSheetId, "a workbook replacement always mints fresh SheetIds");

                window.ReplaceSession(newSession);

                dialog.IsVisible.Should().BeFalse(
                    "R119-avalonia-findreplace-stale-scope: ReplaceSession must close the modeless " +
                    "Find & Replace dialog -- before this fix the dialog stayed open with its selection " +
                    "scope frozen to the OLD workbook's SheetId, so every subsequent Find/Replace action " +
                    "would silently report zero matches forever against the new workbook, because " +
                    "GridRange.Contains requires addr.Sheet == Start.Sheet and the new workbook's sheets " +
                    "always have fresh SheetIds");
                window.OwnedWindows.Should().NotContain(w =>
                        string.Equals(AutomationProperties.GetAutomationId(w), "FindReplaceDialog", StringComparison.Ordinal),
                    "the dialog's own Closed handler must have nulled _findReplaceDialog and detached it " +
                    "from the owner's window collection");

                // Positive proof the user is not stuck: reopening Find & Replace against the NEW session
                // captures a fresh scope and finds the needle that is genuinely present.
                await InvokePrivateTaskAsync(window, "ShowFindDialogAsync");
                var freshDialog = FindOwnedFindReplaceWindow(window);
                freshDialog.Should().NotBeSameAs(dialog, "a brand new dialog instance must be created for the new session");

                var findBox = freshDialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(t => AutomationProperties.GetAutomationId(t) == "FindReplaceFindBox");
                findBox.Text = "needle";
                var findAllButton = freshDialog.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "FindReplaceFindAllButton");
                findAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, findAllButton));

                var resultsList = freshDialog.GetVisualDescendants().OfType<ListBox>().First();
                var matches = (resultsList.ItemsSource as System.Collections.IEnumerable)?
                    .Cast<object>().ToList() ?? new System.Collections.Generic.List<object>();
                matches.Should().HaveCount(1,
                    "a freshly-opened dialog against the new session must find the needle genuinely " +
                    "present in it, proving the user is not permanently stuck at zero matches");

                freshDialog.Close();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }
                if (window.IsVisible)
                    window.Close();
            }

            // IMPORTANT: HeadlessUnitTestSession.Dispatch's Func<Task> (non-generic) overload does NOT
            // propagate an exception/assertion failure thrown inside the delegate back to the awaiting
            // xUnit test -- it is silently swallowed and the test reports Passed regardless of what
            // happened inside. Only the Func<Task<T>> overload propagates correctly. This return makes
            // the compiler pick that overload; do not remove it.
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplaceSession_WithNoFindReplaceDialogOpen_SwapsSessionNormally()
    {
        // Sibling no-regression case: the overwhelming majority of ReplaceSession calls happen with no
        // Find & Replace dialog open at all (the ribbon-interaction-validation disposable-session swaps,
        // ordinary New/Open with no dialog open). CloseFindReplaceDialogIfOpen must be a no-op in that
        // case, and the ordinary session-swap contract (active sheet/session reference updated, no
        // exception) must be completely unaffected by this fix.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.OwnedWindows.Should().BeEmpty("no modeless dialog should be open yet");

                var newSession = new WorkbookSessionFactory().CreateNew(600, 800, includeObjects: true);
                var newSheet = newSession.ActiveSheet;
                newSheet.SetCell(new CellAddress(newSheet.Id, 1, 1), new TextValue("marker"));

                var act = () => window.ReplaceSession(newSession);
                act.Should().NotThrow("CloseFindReplaceDialogIfOpen must tolerate a null _findReplaceDialog");

                window.Session.Should().BeSameAs(newSession, "ReplaceSession's normal swap contract must be unaffected");
                window.Session.ActiveSheet.Id.Should().Be(newSheet.Id);
            }
            finally
            {
                if (window.IsVisible)
                    window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FindReplaceDialog_WithoutSessionSwap_StaysOpenAndReusableAcrossModeSwitches()
    {
        // Sibling no-regression case: the fix must not make the dialog close itself (or stop being
        // reused/mode-switched) for any reason OTHER than a genuine session replacement -- this is the
        // pre-existing modeless-reuse contract (see DialogInteractionValidationTests's
        // ReusableWpfModelessDialogs_... test) that this fix must not disturb.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();

                await InvokePrivateTaskAsync(window, "ShowFindDialogAsync");
                var findDialog = FindOwnedFindReplaceWindow(window);

                await InvokePrivateTaskAsync(window, "ShowReplaceDialogAsync");
                var stillSameDialog = FindOwnedFindReplaceWindow(window);

                stillSameDialog.Should().BeSameAs(findDialog,
                    "switching between Find and Replace must reuse the same modeless window instance, " +
                    "exactly as before this fix");
                findDialog.IsVisible.Should().BeTrue(
                    "the dialog must remain open across a mode switch when no session was swapped");

                findDialog.Close();
            }
            finally
            {
                foreach (var owned in window.OwnedWindows.ToArray())
                {
                    if (owned.IsVisible)
                        owned.Close();
                }
                if (window.IsVisible)
                    window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static Window FindOwnedFindReplaceWindow(MainWindow owner) =>
        owner.OwnedWindows.Single(window =>
            string.Equals(AutomationProperties.GetAutomationId(window), "FindReplaceDialog", StringComparison.Ordinal));

    private static Task InvokePrivateTaskAsync(MainWindow owner, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing production dialog opener {methodName}.");
        return method.Invoke(owner, null) as Task
            ?? throw new InvalidOperationException($"Production dialog opener {methodName} did not return Task.");
    }
}
