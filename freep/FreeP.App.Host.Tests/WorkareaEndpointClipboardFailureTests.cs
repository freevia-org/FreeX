using System.Reflection;
using FreeP.App.Compositor;

namespace FreeP.App.Host.Tests;

/// <summary>
/// shell-clipboard F1: the WPF keyboard-shortcut Copy/Cut path (wired in
/// <c>MainWindow.WorkareaEndpoint.cs</c>'s <c>CreateWorkareaEndpoint()</c>) used to call
/// <see cref="OsClipboardService.Copy"/>/<see cref="OsClipboardService.Cut"/> with no
/// <c>onWriteFailed</c> callback, so an OS-clipboard write failure (clipboard locked by another
/// process, a routine <c>CLIPBRD_E_CANT_OPEN</c>) vanished silently -- no status message, no
/// dialog -- even though the ribbon-button Copy/Cut path on the SAME host surfaced it via
/// <c>ReportClipboardWriteFailure</c>. These tests drive the real Ctrl+C/Ctrl+X call chain
/// (<c>MainWindow._workareaSession.ExecuteCommand</c>, the same entry point the keyboard
/// shortcut uses) with a clipboard rigged to fail, and assert the status bar surfaces it.
/// </summary>
public sealed class WorkareaEndpointClipboardFailureTests
{
    [StaFact]
    public void KeyboardCopy_OsClipboardWriteFails_SurfacesFailureInStatusBar()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            InstallFailingClipboard(window, out _);
            var shape = window.Editor.CurrentSlide!.Shapes.First();
            window.Editor.Select(shape.Id);

            GetWorkareaSession(window).ExecuteCommand(FreePKeyboardCommand.Copy);

            GetSlideCountText(window).Should().Contain("clipboard locked",
                "a failed OS-clipboard write on the keyboard-shortcut Copy path must reach the " +
                "status bar, exactly like the WPF ribbon Copy button and the Avalonia shell do");
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void KeyboardCut_OsClipboardWriteFails_SurfacesFailureInStatusBar()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            InstallFailingClipboard(window, out _);
            var shape = window.Editor.CurrentSlide!.Shapes.First();
            window.Editor.Select(shape.Id);

            GetWorkareaSession(window).ExecuteCommand(FreePKeyboardCommand.Cut);

            GetSlideCountText(window).Should().Contain("clipboard locked",
                "a failed OS-clipboard write on the keyboard-shortcut Cut path must reach the " +
                "status bar, exactly like the WPF ribbon Cut button and the Avalonia shell do");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Sibling no-regression: a successful keyboard Copy must NOT touch the status bar with a
    /// failure message (i.e. the fix must not report a failure on the happy path).
    /// </summary>
    [StaFact]
    public void KeyboardCopy_OsClipboardWriteSucceeds_StatusBarUnaffected()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            InstallClipboard(window, throwOnWrite: false, out _);
            var shape = window.Editor.CurrentSlide!.Shapes.First();
            window.Editor.Select(shape.Id);
            var before = GetSlideCountText(window);

            GetWorkareaSession(window).ExecuteCommand(FreePKeyboardCommand.Copy);

            GetSlideCountText(window).Should().Be(before,
                "a successful copy must not overwrite the status bar with a failure message");
        }
        finally
        {
            window.Close();
        }
    }

    // ── Test plumbing ──────────────────────────────────────────────────────────────

    private static void InstallFailingClipboard(MainWindow window, out OsClipboardServiceTests.FakeOsClipboard fake)
        => InstallClipboard(window, throwOnWrite: true, out fake);

    private static void InstallClipboard(
        MainWindow window,
        bool throwOnWrite,
        out OsClipboardServiceTests.FakeOsClipboard fake)
    {
        fake = new OsClipboardServiceTests.FakeOsClipboard { ThrowOnWrite = throwOnWrite };
        var service = new OsClipboardService(fake, new OsClipboardServiceTests.StubShapeRenderer());
        var field = typeof(MainWindow).GetField("_osClipboard", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull("MainWindow must still own a private _osClipboard field for this test to rig");
        field!.SetValue(window, service);
    }

    private static PresentationWorkareaSession GetWorkareaSession(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_workareaSession", BindingFlags.Instance | BindingFlags.NonPublic);
        return (PresentationWorkareaSession)field!.GetValue(window)!;
    }

    private static string GetSlideCountText(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_slideCountText", BindingFlags.Instance | BindingFlags.NonPublic);
        var textBlock = (System.Windows.Controls.TextBlock)field!.GetValue(window)!;
        return textBlock.Text;
    }
}
