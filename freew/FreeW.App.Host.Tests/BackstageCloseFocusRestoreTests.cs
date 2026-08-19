using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Free.Shared.AppServices;
using FreeW.App.Host.Backstage;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.Options;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for the fix to keyboard-focus-restore finding F1: closing the WPF Backstage (File screen) —
/// via Esc, the back arrow, or an action that dismisses it (Save/Save As) — must return keyboard focus to
/// the document editor. The Backstage is an in-window overlay
/// (<see cref="Free.Shared.Shell.Wpf.BackstageFrame"/> / <see cref="Free.Shared.Shell.Wpf.BackstageViewShell"/>)
/// that only toggles its host's <c>Visibility</c> on close; WPF does not automatically move
/// <see cref="Keyboard.FocusedElement"/> anywhere once the previously-focused rail button becomes
/// non-visible, so the host's <c>OnClosed</c> callback must explicitly refocus the editor — exactly as
/// FreeX's <c>OnBackstageFrameClosed</c> (src/FreeX.App.Host/MainWindow.BackstageFrame.cs) calls
/// <c>SheetGrid.Focus()</c> and FreeP's Avalonia host calls <c>HideBackstageAndRestoreFocus</c>.
/// STA because this drives a real WPF window and its focus manager.
/// </summary>
public sealed class BackstageCloseFocusRestoreTests
{
    private static DocumentView GetEditor(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_editor",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (DocumentView)field!.GetValue(window)!;
    }

    private static BackstageView GetBackstage(MainWindow window)
    {
        var field = typeof(MainWindow).GetField(
            "_backstage",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return (BackstageView)field!.GetValue(window)!;
    }

    private static void InvokePrivate(MainWindow window, string methodName)
    {
        var method = typeof(MainWindow).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        method!.Invoke(window, null);
    }

    private static MainWindow NewOffscreenWindow()
    {
        var window = new MainWindow(new FreeWOptions(), messageService: new NoUiMessageService())
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
    public void ClosingBackstage_ReturnsKeyboardFocusToTheDocumentEditor()
    {
        var window = NewOffscreenWindow();
        try
        {
            window.Show();
            var editor = GetEditor(window);
            editor.Focus();
            Keyboard.FocusedElement.Should().Be(editor,
                "the editor must actually hold keyboard focus before opening the Backstage, or the test proves nothing");

            // Opens the Backstage via the actual production entry point (title bar's File button routes
            // to this same private method -- MainWindow.cs, `Backstage = ShowBackstage`).
            InvokePrivate(window, "ShowBackstage");

            // In production, BackstageFrame.Show() (and the rail's own key/mouse handling) is what moves
            // keyboard focus off the editor and onto the overlay. Reproducing that exact focus transfer
            // deterministically inside a headless test host is unreliable (this codebase's own
            // ContentControlKeyboardLockTests documents that real WPF keyboard-focus/activation timing is
            // flaky off-screen). What is NOT in question -- and what the fix under test governs -- is the
            // other half of the round trip: once focus is anywhere other than the editor, does *closing*
            // the Backstage bring it back? Keyboard.ClearFocus() reproduces that "focus is elsewhere"
            // precondition deterministically without depending on the overlay's own (flaky-under-test)
            // focus-acquisition behaviour.
            Keyboard.ClearFocus();
            Keyboard.FocusedElement.Should().NotBe(editor,
                "the precondition for this test is that focus is NOT already on the editor");

            var backstage = GetBackstage(window);

            // Mirrors the production close gestures (Esc / back arrow / Save / Save As), all of which
            // route through BackstageView.Hide() -> SisterBackstageHostController.Hide() ->
            // BackstageViewShell.Hide() (Frame.Hide() raises Closed) -> the host's OnClosed callback,
            // which is exactly what MainWindow.cs:737's fix (adding `_editor.Focus()`) changes.
            backstage.Hide();

            Keyboard.FocusedElement.Should().Be(editor,
                "closing the Backstage must return keyboard focus to the document editor so typed " +
                "keystrokes are not silently dropped");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void OpeningBackstage_StillHidesTheAdornerLayer_RegressionCheckForTheSiblingBehaviour()
    {
        // Sibling no-regression check: the fix adds a Focus() call alongside the pre-existing
        // SetEditorAdornersVisible(true) restore -- it must not disturb that adorner-visibility toggle,
        // which exists so page-break markers (drawn in the window AdornerLayer, above sibling content)
        // don't bleed through the opaque Backstage overlay while it is shown.
        var window = NewOffscreenWindow();
        try
        {
            window.Show();
            var editor = GetEditor(window);
            var backstage = GetBackstage(window);

            var layer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(editor);
            layer.Should().NotBeNull();

            InvokePrivate(window, "ShowBackstage");
            layer!.Visibility.Should().Be(Visibility.Collapsed,
                "the adorner layer must still be hidden while the Backstage overlay is showing");

            backstage.Hide();
            layer.Visibility.Should().Be(Visibility.Visible,
                "the adorner layer must still be restored to visible once the Backstage closes");
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class NoUiMessageService : IUserMessageService
    {
        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) =>
            UserMessageResult.No;
    }
}
