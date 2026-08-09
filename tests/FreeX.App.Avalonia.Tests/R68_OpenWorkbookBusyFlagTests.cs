using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R68-async-ordering-race-sweep-3: <c>MainWindow.OpenWorkbookAsync</c> used to leave the
/// <c>_isOpening</c> busy flag FALSE all the way through its confirm-dialog / file-picker phase --
/// it was only set deep inside <c>OpenWorkbookFromTargetAsync</c>, reached (if ever) only after a
/// file was actually picked. A second Open request (a drop, OS file-activation, or another click)
/// arriving during that window saw <c>_isOpening == false</c> and silently raced the first instead
/// of being rejected. The fix claims the flag SYNCHRONOUSLY at the top of <c>OpenWorkbookAsync</c>
/// (and the guarded <c>OpenWorkbookPathAsync</c> wrapper), before the very first await, and clears
/// it in a <c>finally</c> -- mirroring the WPF host's <c>_isOpeningFile</c> set-before-await.
///
/// These tests mark the session dirty so <c>ConfirmBeforeDestructiveWorkbookActionAsync</c> shows a
/// REAL modal <c>Window</c> dialog (<c>await dialog.ShowDialog(this)</c>) -- a genuine, controllable
/// async suspension point reached before any file is ever touched, so the test never depends on a
/// real OS file picker (which the headless platform cannot drive) and can never hang.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R68_OpenWorkbookBusyFlagTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task OpenWorkbookAsync_ClaimsBusyFlagBeforeConfirmDialogAwait_RejectingASecondConcurrentOpen()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Session.AddSheet(); // makes the session dirty -> a real confirm dialog opens

                var openMethod = typeof(MainWindow).GetMethod("OpenWorkbookAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var isOpeningField = typeof(MainWindow).GetField("_isOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;

                var task1 = (Task)openMethod.Invoke(window, null)!;
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle(
                    "the first Open's dirty-workbook confirm dialog must be showing by now");
                isOpeningField.GetValue(window).Should().Be(true,
                    "the busy flag must already be claimed while the confirm dialog is still pending -- " +
                    "before the fix it stayed false until deep inside OpenWorkbookFromTargetAsync, never reached here");

                // A second Open request while the first is still waiting on its confirm dialog must
                // be rejected immediately (bail out via the busy-flag guard before any await), not
                // silently open a second confirm dialog / race the first.
                var task2 = (Task)openMethod.Invoke(window, null)!;
                task2.IsCompleted.Should().BeTrue(
                    "a second concurrent Open must return synchronously via the busy-flag guard");
                window.OwnedWindows.Should().ContainSingle(
                    "the second Open must never have opened its own confirm dialog");

                // Dismiss the first dialog with Cancel so OpenWorkbookAsync's own finally clears the
                // flag, proving a legitimate single Open still completes (doesn't deadlock).
                var dialog = window.OwnedWindows.Single();
                // The dialog's own KeyDown handler closes (and disposes) it synchronously on Escape,
                // so only the press is raised here -- a follow-up release would target an already-
                // disposed Window.
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task1;

                isOpeningField.GetValue(window).Should().Be(false,
                    "the busy flag must clear once the (canceled) Open finishes");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task OpenWorkbookAsync_SingleRequest_CancelsCleanlyAndLeavesTheWindowUsable()
    {
        // Sibling no-regression check: claiming the flag earlier must not deadlock or otherwise
        // break a normal, solitary Open request -- it still opens its dialog, can be canceled, and
        // the busy flag returns to false afterward exactly as before the fix.
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Show();
                window.Session.AddSheet();

                var openMethod = typeof(MainWindow).GetMethod("OpenWorkbookAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var isOpeningField = typeof(MainWindow).GetField("_isOpening", BindingFlags.Instance | BindingFlags.NonPublic)!;

                var task = (Task)openMethod.Invoke(window, null)!;
                await DrainInputAsync();

                window.OwnedWindows.Should().ContainSingle();
                var dialog = window.OwnedWindows.Single();
                // The dialog's own KeyDown handler closes (and disposes) it synchronously on Escape,
                // so only the press is raised here -- a follow-up release would target an already-
                // disposed Window.
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await DrainInputAsync();
                await task;

                isOpeningField.GetValue(window).Should().Be(false);
                window.Session.IsDirty.Should().BeTrue("canceling the confirm dialog must leave the (still-dirty) workbook untouched");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
