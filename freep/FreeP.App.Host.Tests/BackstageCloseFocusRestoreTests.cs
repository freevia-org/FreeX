using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FreeP.App.Compositor;
using FreeP.App.Host.Backstage;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Coverage for the fix to keyboard-focus-restore finding F2: closing the WPF Backstage (File screen) --
/// via Esc, the back arrow, or an action that dismisses it (Save/Save As/Export) -- must return keyboard
/// focus to the slide canvas. The Backstage is an in-window overlay
/// (<see cref="Free.Shared.Shell.Wpf.BackstageFrame"/> / <see cref="Free.Shared.Shell.Wpf.BackstageViewShell"/>)
/// that only toggles its host's <c>Visibility</c> on close (<c>Frame.Hide()</c> just sets
/// <c>Visibility = Collapsed</c> and raises <c>Closed</c>); WPF does not automatically move
/// <see cref="Keyboard.FocusedElement"/> anywhere on its own, so the host's <c>OnClosed</c> callback must
/// explicitly refocus the canvas -- exactly as FreeP's own Avalonia host
/// (<c>HideBackstageAndRestoreFocus</c> in FreeP.App.Avalonia/MainWindow.cs) already does, and as FreeW's
/// WPF host does for its document editor (FreeW.App.Host/MainWindow.cs, <c>OnClosed: () =>
/// { SetEditorAdornersVisible(true); _editor.Focus(); }</c>).
///
/// The precondition ("focus is not on the canvas") is forced with <see cref="Keyboard.ClearFocus"/> rather
/// than by asserting on <c>BackstageFrame.Show()</c>'s own focus-shifting behaviour: in this offscreen STA
/// test host, whether <c>Show()</c>'s internal <c>Focus()</c> call actually lands is independent of, and
/// orthogonal to, the thing under test (whether <c>Hide()</c>'s <c>OnClosed</c> callback restores focus to
/// the canvas). Forcing the precondition directly keeps the test meaningful regardless of that.
/// STA because this drives a real WPF window and its focus manager.
/// </summary>
public sealed class BackstageCloseFocusRestoreTests
{
    private static BackstageView GetBackstage(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_backstage",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (BackstageView)field!.GetValue(window)!;
    }

    // The Backstage rail (BackstageFrame / its nav buttons) starts Collapsed and only becomes part of the
    // realized visual tree once its host's Visibility flips to Visible, inside Show()/Hide(). WPF can defer
    // that layout/Loaded pass to the dispatcher queue rather than running it synchronously. Mirrors the
    // PumpLayout/PumpDispatcher pattern in WpfDialogPaneVisualEvidenceCapture.cs.
    private static void PumpLayout(Window window)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        window.UpdateLayout();
    }

    private static MainWindow NewOffscreenWindow()
    {
        var window = new MainWindow(
            new FreePOptions(),
            messageService: TestUserMessageService.DiscardUnsavedChanges)
        {
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            Left = -10000,
            Top = -10000,
            Width = 400,
            Height = 300,
        };
        return window;
    }

    [StaFact]
    public void ClosingBackstage_ReturnsKeyboardFocusToTheSlideCanvas()
    {
        var window = NewOffscreenWindow();
        try
        {
            window.Show();
            PumpLayout(window);
            var canvas = window.SlideCanvas;
            canvas.Focus();
            Keyboard.FocusedElement.Should().Be(canvas,
                "the slide canvas must actually hold keyboard focus before opening the Backstage, or the test proves nothing");

            var backstage = GetBackstage(window);
            backstage.Show();
            PumpLayout(window);

            // Force focus off the canvas regardless of whether BackstageFrame.Show()'s own focus-shift
            // landed in this offscreen test host -- what matters for this test is only that focus is
            // demonstrably NOT on the canvas going into Hide().
            Keyboard.ClearFocus();
            PumpLayout(window);
            Keyboard.FocusedElement.Should().NotBe(canvas,
                "the precondition requires focus to be off the canvas before Hide() is exercised, or the test proves nothing");

            // Mirrors the production close gestures (Esc / back arrow / Save / Save As / Export), all of
            // which route through BackstageView.Hide() -> SisterBackstageHostController.Hide() ->
            // BackstageViewShell.Hide() (Frame.Hide() raises Closed) -> the host's OnClosed callback.
            backstage.Hide();
            PumpLayout(window);

            Keyboard.FocusedElement.Should().Be(canvas,
                "closing the Backstage must return keyboard focus to the slide canvas so arrow keys, " +
                "Delete, and typed text are not silently dropped");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void OpeningAndClosingBackstage_StillTogglesIsOpen_RegressionCheckForTheSiblingBehaviour()
    {
        // Sibling no-regression check: the fix only adds a Focus() call to the Backstage's OnClosed
        // callback -- it must not disturb the pre-existing Show()/Hide() visibility contract that
        // IsBackstageOpen (MainWindow) / IsOpen (BackstageView) and the rest of the host rely on.
        var window = NewOffscreenWindow();
        try
        {
            window.Show();
            var backstage = GetBackstage(window);

            backstage.IsOpen.Should().BeFalse("the Backstage starts closed");

            backstage.Show();
            backstage.IsOpen.Should().BeTrue("Show() must still open the overlay after the fix");

            backstage.Hide();
            backstage.IsOpen.Should().BeFalse("Hide() must still close the overlay after the fix");
        }
        finally
        {
            window.Close();
        }
    }
}
