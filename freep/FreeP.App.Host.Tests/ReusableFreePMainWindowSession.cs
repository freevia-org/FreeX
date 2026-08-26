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
        typeof(MainWindow)
            .GetMethod("FileNew", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(window, null);
        window.UpdateLayout();
    }
}
