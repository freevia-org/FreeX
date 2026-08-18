using System.Threading;

using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;

using FreeX.App.Presentation.Backstage;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression tests for round-140 finding "backstage-shortcuts-leak-to-worksheet" on the
/// Avalonia/Linux/macOS shell: while the File-menu Backstage overlay is open, a real (headless
/// input-injected, tunnel+bubble routed) worksheet keyboard shortcut like Delete must not reach
/// the hidden worksheet underneath. The fix lives in a Tunnel KeyDown handler MainWindow.LiveBackstage.cs
/// adds on the live <c>_backstageOverlay</c> control itself, so these tests must dispatch a real,
/// routed key event (<see cref="HeadlessWindowExtensions.KeyPress"/>) -- NOT the
/// <c>RaiseKeyDownForTest</c> helper, which calls <c>MainWindow_KeyDownAsync</c> directly and would
/// never reach the overlay's own routed handler, proving nothing about this fix.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class BackstageWorksheetShortcutLeakTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Delete_WhileBackstageOverlayOpen_DoesNotClearWorksheetCell()
    {
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow();
            try
            {
                var address = window.Session.ActiveCell;
                Edit(window, address, "keep-me");

                window.ShowBackstageOverlayForTest();
                window.Measure(new Size(1120, 720));
                window.Arrange(new Rect(0, 0, 1120, 720));
                await DrainInputAsync();
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue();

                // The failure scenario is specifically "a rail button has focus" -- Show()'s own
                // _backButton.Focus() call can no-op before the newly-IsVisible overlay subtree has
                // been laid out, so focus it explicitly here and confirm it actually took, exactly
                // as the other physical-input tests in this suite do for the worksheet's own
                // active-cell border (ActiveCellBorderForTest!.Focus().Should().BeTrue()).
                var homeButton = window.BackstagePaneButtonForTest(FreeXBackstagePaneId.Home);
                homeButton.Should().NotBeNull();
                homeButton!.Focus().Should().BeTrue("the Backstage Home rail button must be able to take real keyboard focus");

                PressDelete(window);
                await DrainInputAsync();

                window.Session.ActiveSheet.GetValue(address).Should().Be(new TextValue("keep-me"),
                    "Delete pressed while the Backstage overlay is open must not clear the hidden worksheet cell");
                window.IsBackstageOverlayVisibleForTest.Should().BeTrue(
                    "the overlay itself must remain open -- Delete is not a Backstage command");
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
    public async Task Delete_WithBackstageClosed_StillClearsWorksheetCell()
    {
        // Sibling/neighbouring-behavior guard: the fix must not disable Delete/ClearSelection for
        // the ordinary (Backstage-closed) case -- only while the overlay is actually visible.
        await Session.Dispatch(async () =>
        {
            var window = CreateShownWindow();
            try
            {
                var address = window.Session.ActiveCell;
                Edit(window, address, "clear-me");
                window.IsBackstageOverlayVisibleForTest.Should().BeFalse();

                window.ActiveCellBorderForTest.Should().NotBeNull();
                window.ActiveCellBorderForTest!.Focus().Should().BeTrue();

                PressDelete(window);
                await DrainInputAsync();

                window.Session.ActiveSheet.GetValue(address).Should().Be(BlankValue.Instance,
                    "Delete must still clear the active cell when Backstage is not open");
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static MainWindow CreateShownWindow()
    {
        var window = new MainWindow([]);
        window.Show();
        window.Measure(new Size(1120, 720));
        window.Arrange(new Rect(0, 0, 1120, 720));
        return window;
    }

    private static void Edit(MainWindow window, CellAddress address, string text)
    {
        var result = window.Session.ExecuteReviewCommand(
            EditCellsCommand.ForValue(address.Sheet, address, new TextValue(text)),
            address);
        result.Success.Should().BeTrue(result.ErrorMessage);
        window.Session.IsDirty.Should().BeTrue();
    }

    private static void PressDelete(MainWindow window)
    {
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
    }

    private static async Task DrainInputAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
