using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class AvaloniaSharedWorkbookWindowTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task NewWindow_SharesDocumentRefreshesMutationAndKeepsViewStateLocal()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                first.Session.Workbook.Should().BeSameAs(second.Session.Workbook);
                first.Session.Should().NotBeSameAs(second.Session);
                first.Title.Should().Be($"{first.Session.DisplayName}:1 - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName}:2 - FreeX");
                first.Session.DataValidationPromptResolver
                    .Should().NotBeSameAs(second.Session.DataValidationPromptResolver);

                var sheet = first.Session.ActiveSheet;
                var firstCell = new CellAddress(sheet.Id, 2, 2);
                var secondCell = new CellAddress(sheet.Id, 4, 4);
                first.Session.SelectCell(firstCell);
                second.Session.SelectCell(secondCell);
                first.Session.SetViewportOrigin(20, 5).Should().BeTrue();
                second.Session.SetViewportOrigin(30, 7).Should().BeTrue();

                first.Session.ActiveCell.Should().Be(firstCell);
                second.Session.ActiveCell.Should().Be(secondCell);
                first.Session.ViewportOrigin.Should().Be((20u, 5u));
                second.Session.ViewportOrigin.Should().Be((30u, 7u));

                first.Session.CommitCellText("shared edit").Success.Should().BeTrue();

                second.Session.Workbook.Should().BeSameAs(first.Session.Workbook);
                second.Session.ActiveSheet.GetValue(firstCell)
                    .Should().Be(new TextValue("shared edit"));
                second.Session.ActiveCell.Should().Be(secondCell);
                second.Session.ViewportOrigin.Should().Be((30u, 7u));
                second.Title.Should().Be($"{second.Session.DisplayName}:2 * - FreeX");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReplacingOneViewDetachesItAndClosingItLeavesSiblingFunctional()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                var originalWorkbook = second.Session.Workbook;
                var replacement = new WorkbookSessionFactory().CreateNew(
                    viewportHeight: 880,
                    viewportWidth: 1440,
                    includeObjects: true);

                first.ReplaceSession(replacement);

                first.Session.Workbook.Should().NotBeSameAs(originalWorkbook);
                second.Session.Workbook.Should().BeSameAs(originalWorkbook);
                first.Title.Should().Be($"{first.Session.DisplayName} - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName} - FreeX");

                first.AllowCloseWithoutDirtyPromptForParityCapture();
                first.Close();
                second.IsVisible.Should().BeTrue();

                var address = second.Session.ActiveCell;
                second.Session.CommitCellText("surviving view").Success.Should().BeTrue();
                second.Session.ActiveSheet.GetValue(address)
                    .Should().Be(new TextValue("surviving view"));
            }
            finally
            {
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UndoToSavePointRefreshesSiblingTitleToClean()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                var savePath = Path.Combine(Path.GetTempPath(), "AvaloniaSharedSavePoint.xlsx");
                first.Session.MarkSaved(savePath);

                first.Title.Should().Be($"{first.Session.DisplayName}:1 - FreeX");
                second.Title.Should().Be($"{second.Session.DisplayName}:2 - FreeX");

                first.Session.CommitCellText("dirty after save").Success.Should().BeTrue();
                second.Title.Should().Be($"{second.Session.DisplayName}:2 * - FreeX");

                first.Session.UndoLastEdit().Success.Should().BeTrue();

                first.Session.IsDirty.Should().BeFalse();
                second.Session.IsDirty.Should().BeFalse();
                second.Title.Should().Be($"{second.Session.DisplayName}:2 - FreeX");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                second.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task NewWindow_UsesOriginatingSheetAndStartsThatViewAtA1()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            MainWindow? second = null;
            try
            {
                var activeSheet = first.Session.Workbook.AddSheet("OriginatingSheet");
                first.Session.SelectSheet(activeSheet.Id);
                var originCell = new CellAddress(activeSheet.Id, 7, 3);
                first.Session.SelectCell(originCell);

                second = first.CreateSharedViewForTest();

                second.Session.ActiveSheet.Should().BeSameAs(activeSheet);
                second.Session.ActiveCell.Should().Be(new CellAddress(activeSheet.Id, 1, 1));
                first.Session.ActiveSheet.Should().BeSameAs(activeSheet);
                first.Session.ActiveCell.Should().Be(originCell);
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second?.AllowCloseWithoutDirtyPromptForParityCapture();
                second?.Close();
                first.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlF6_CyclesRealWorkbookWindowsThroughRegistryForwardAndBackward()
    {
        await Session.Dispatch(async () =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            var third = first.CreateSharedViewForTest();
            Show(first);
            Show(second);
            Show(third);

            try
            {
                MainWindow? activeBefore = null;
                first.Activated += (_, _) => activeBefore = first;
                second.Activated += (_, _) => activeBefore = second;
                third.Activated += (_, _) => activeBefore = third;

                first.ActivateWorkbookWindow();
                MainWindow.WindowRegistryForTest.NextWindowTarget(first, forward: true)
                    .Should().BeSameAs(second);
                MainWindow.WindowRegistryForTest.NextWindowTarget(second, forward: true)
                    .Should().BeSameAs(third);
                MainWindow.WindowRegistryForTest.NextWindowTarget(first, forward: false)
                    .Should().BeSameAs(third);

                await first.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control,
                });
                // Headless has no window manager, so neither IsActive nor the Activated event ever reports the

                // switch. ActivateWorkbookWindow also focuses the target's sheet grid, and focus DOES work

                // headless -- assert the focused element now lives inside the expected window.

                AssertFocusMovedInto(second);


                await second.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control,
                });
                AssertFocusMovedInto(third);

                await third.RaiseKeyDownForTest(new KeyEventArgs
                {
                    Key = Key.F6,
                    KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift,
                });
                AssertFocusMovedInto(second);
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                third.AllowCloseWithoutDirtyPromptForParityCapture();
                third.Close();
                second.Close();
                first.Close();
            }
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Regression coverage for the Avalonia twin of the WPF host's
    /// <c>DocumentSharedWithOtherWindows()</c> guard (<c>MainWindow.WorkbookLifecycle.cs</c>): closing
    /// one "New Window" view of a dirty document must not prompt to save while a sibling view still
    /// holds that document open -- the document is not going away, only this one view of it. Before
    /// the fix, <c>MainWindow_Closing</c> checked only <c>_session.IsDirty</c> and cancelled/prompted
    /// regardless of any surviving sibling.
    /// </summary>
    [Fact]
    public async Task Closing_NonLastSiblingWindow_DoesNotPromptToSaveAndDocumentSurvives()
    {
        await Session.Dispatch(() =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                first.Session.CommitCellText("dirty via sibling close").Success.Should().BeTrue();
                second.Session.IsDirty.Should().BeTrue();

                // Drives the REAL close path -- no AllowCloseWithoutDirtyPromptForParityCapture
                // bypass here -- so the assertions below actually exercise MainWindow_Closing's
                // sibling check rather than a test-only shortcut around it.
                first.Close();

                first.IsVisible.Should().BeFalse(
                    "closing a non-last sibling view must proceed immediately, not cancel for an unanswered dirty-workbook dialog");
                second.IsVisible.Should().BeTrue("the surviving sibling must be untouched by the other view's close");
                second.Session.IsDirty.Should().BeTrue(
                    "the shared document's dirty state must survive -- it was never discarded");
            }
            finally
            {
                // Cleanup only -- the assertions above already ran against the real (non-bypassed)
                // MainWindow_Closing outcome, so calling the guard here cannot mask a failure.
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                if (first.IsVisible)
                    first.Close();
                if (second.IsVisible)
                    second.Close();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// No-regression sibling of <see cref="Closing_NonLastSiblingWindow_DoesNotPromptToSaveAndDocumentSurvives"/>:
    /// a dirty document with NO other open view must still prompt (and cancel the close) exactly as
    /// before the sibling-aware fix -- proving that fix did not weaken the ordinary, non-shared close
    /// path.
    /// </summary>
    [Fact]
    public async Task Closing_LastWindowOfDirtyDocument_StillPromptsToSaveBeforeClosing()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            Show(window);

            try
            {
                window.Session.CommitCellText("dirty single window").Success.Should().BeTrue();

                window.Close();

                window.IsVisible.Should().BeTrue(
                    "the sole view of a dirty document must still prompt (and cancel the close) before closing");
                window.OwnedWindows.Should().ContainSingle(
                    "cancelling the close for the sole dirty view must pop the save-changes confirmation dialog");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                if (window.IsVisible)
                    window.Close();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Regression coverage for the round-137-remediation gap: quit from a window whose document also
    /// has a live "New Window" sibling used to prompt TWICE for the same document -- once from
    /// <c>TryQuitApplicationAsync</c>'s own dirty gate (which checked only the invoking window's
    /// <c>_session.IsDirty</c>, with no <see cref="WindowRegistry"/>/sibling awareness), and a second
    /// time when <c>desktop.TryShutdown</c>'s cascade reached the sibling's own
    /// <c>MainWindow_Closing</c> -- because Discard never clears the shared document's dirty flag and
    /// <c>_allowCloseWithoutDirtyPrompt</c> was previously set on only the invoking window. This test
    /// drives the real Quit entry point (see <c>TryQuitApplicationAsyncForTest</c>'s doc comment for
    /// why a headless test cannot raise the native OS menu's own <c>Click</c>) and then closes the
    /// sibling directly -- exactly what <c>desktop.TryShutdown</c>'s cascade does to every remaining
    /// window once <c>TryQuitApplicationAsync</c> returns -- to prove the fix's sibling propagation
    /// makes that second Close() a no-op prompt-wise.
    /// </summary>
    [Fact]
    public async Task TryQuitApplicationAsync_TwoWindowsOnOneDirtyDocument_PromptsExactlyOnceAndDiscardClosesBothWindows()
    {
        await Session.Dispatch(async () =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                first.Session.CommitCellText("dirty for quit").Success.Should().BeTrue();
                second.Session.IsDirty.Should().BeTrue("New Window siblings share the same document state");

                var quitTask = first.TryQuitApplicationAsyncForTest();
                await DrainInputAsync();

                first.OwnedWindows.Should().ContainSingle(
                    "quitting a dirty document must show exactly ONE save-changes confirmation for it");
                second.OwnedWindows.Should().BeEmpty(
                    "the sibling view of the SAME document must not get its own, separate confirmation");

                ClickDirtyWorkbookButton(first.OwnedWindows.Single(), "DirtyWorkbookDiscardButton");
                await DrainInputAsync();
                await quitTask;

                first.IsVisible.Should().BeFalse("the quitting window closes once Discard is chosen");

                // Simulates desktop.TryShutdown's cascade reaching the sibling. Before the fix this
                // Close() would find the shared document still dirty (Discard never clears it) and no
                // other window left for it (first already unregistered) and pop a SECOND, redundant
                // confirmation -- the exact bug under test.
                second.Close();
                await DrainInputAsync();

                second.OwnedWindows.Should().BeEmpty(
                    "the already-confirmed document must not prompt again when its sibling window closes");
                second.IsVisible.Should().BeFalse(
                    "the sibling must close too instead of being left open as half of a half-quit document");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                if (first.IsVisible)
                    first.Close();
                if (second.IsVisible)
                    second.Close();
            }

            // IMPORTANT: Session.Dispatch's Func<Task> overload does NOT propagate exceptions/failed
            // assertions thrown inside an async lambda with no return value -- they are silently
            // swallowed and the test reports as passed regardless of what happened inside. Returning a
            // value routes through the Func<Task<T>> overload instead, which does propagate. Verified
            // empirically: every assertion above passed unconditionally (including a "must always fail"
            // canary) until this return was added. See R115_StartupRecoveryDedupTests.cs for the same
            // return-true convention.
            return true;
        }, CancellationToken.None);
    }

    /// <summary>
    /// Sibling of <see cref="TryQuitApplicationAsync_TwoWindowsOnOneDirtyDocument_PromptsExactlyOnceAndDiscardClosesBothWindows"/>:
    /// cancelling the single confirmation must abort the quit entirely, leaving BOTH windows open --
    /// not the "half-quit" the duplicate-prompt bug produced (invoking window already gone by the time
    /// the redundant second prompt was cancelled).
    /// </summary>
    [Fact]
    public async Task TryQuitApplicationAsync_CancellingTheSingleConfirmation_LeavesBothWindowsOpen()
    {
        await Session.Dispatch(async () =>
        {
            var first = new MainWindow([]);
            var second = first.CreateSharedViewForTest();
            Show(first);
            Show(second);

            try
            {
                first.Session.CommitCellText("dirty for quit cancel").Success.Should().BeTrue();

                var quitTask = first.TryQuitApplicationAsyncForTest();
                await DrainInputAsync();

                first.OwnedWindows.Should().ContainSingle();

                ClickDirtyWorkbookButton(first.OwnedWindows.Single(), "DirtyWorkbookCancelButton");
                await DrainInputAsync();
                await quitTask;

                first.IsVisible.Should().BeTrue("cancelling the one confirmation must abort the quit entirely");
                second.IsVisible.Should().BeTrue(
                    "the sibling must never have been touched -- a cancelled quit must not half-close a shared document");
                second.Session.IsDirty.Should().BeTrue("a cancelled quit must not silently discard anything");
            }
            finally
            {
                first.AllowCloseWithoutDirtyPromptForParityCapture();
                second.AllowCloseWithoutDirtyPromptForParityCapture();
                if (first.IsVisible)
                    first.Close();
                if (second.IsVisible)
                    second.Close();
            }

            // See the sibling test's comment above -- required for assertion failures to propagate.
            return true;
        }, CancellationToken.None);
    }

    private static void ClickDirtyWorkbookButton(Window dialog, string automationId)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == automationId);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }

    private static void Show(MainWindow window)
    {
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        window.UpdateLayout();
    }

    /// <summary>
    /// Asserts the keyboard focus now sits inside <paramref name="window"/>. Written as two statements
    /// rather than a null-conditional chain on purpose: <c>focused?.Ancestor().Should()...</c> silently
    /// SKIPS the assertion when focus is null, which would let this pass vacuously.
    /// </summary>
    private static void AssertFocusMovedInto(Window window)
    {
        var focused = window.FocusManager!.GetFocusedElement() as Visual;
        focused.Should().NotBeNull("cycling to a workbook window must move keyboard focus into it");
        focused!.FindAncestorOfType<Window>(includeSelf: true).Should().BeSameAs(window);
    }
}
