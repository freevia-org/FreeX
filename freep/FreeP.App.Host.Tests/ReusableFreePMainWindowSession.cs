using System.Reflection;
using Free.Shared.Testing;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Shared shell for deterministic WPF command and chrome tests. Tests that exercise startup,
/// file lifecycle, native dialogs, focus, clipboard, or multiple windows use a fresh shell.
/// </summary>
internal static class ReusableFreePMainWindowSession
{
    private static readonly ReusableWpfWindowSession<MainWindow> Session = new(CreateWindow, ResetWindow);

    internal static void Run(Action<MainWindow> action) => Session.Run(action);

    private static MainWindow CreateWindow() =>
        new(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);

    private static void ResetWindow(MainWindow window)
    {
        // FileNew replaces the presentation but does not complete the window's autosave session.
        // Stop first so a dirty borrower cannot leave a recovery snapshot behind in the real test
        // user's app-data store, then resume the timer for the next borrower.
        var autosave = window.AutosaveCoordinatorForCrashHandler;
        autosave?.Stop();
        try
        {
            typeof(MainWindow)
                .GetMethod("FileNew", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
                .Invoke(window, null);
            window.UpdateLayout();
        }
        finally
        {
            autosave?.Start();
        }
    }
}
