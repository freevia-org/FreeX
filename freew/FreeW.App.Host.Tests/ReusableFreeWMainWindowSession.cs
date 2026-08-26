using System.Reflection;
using Free.Shared.AppServices;
using Free.Shared.Testing;
using FreeW.App.Host;
using FreeW.App.Presentation.Options;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Shared shell for deterministic WPF command and chrome tests. Fresh-window tests retain ownership
/// of startup, document lifecycle, native dialog, focus, clipboard, and multi-window assertions.
/// </summary>
internal static class ReusableFreeWMainWindowSession
{
    private static readonly ReusableWpfWindowSession<MainWindow> Session = new(CreateWindow, ResetWindow);

    internal static void Run(Action<MainWindow> action) => Session.Run(action);

    private static MainWindow CreateWindow() =>
        new(new FreeWOptions(), messageService: DiscardUnsavedChangesMessageService.Instance);

    private static void ResetWindow(MainWindow window)
    {
        ExitTransientViewModes(window);
        var fileCommands = typeof(MainWindow)
            .GetField("_file", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        fileCommands.GetType().GetMethod("New", BindingFlags.Instance | BindingFlags.Public)!.Invoke(fileCommands, null);
        window.UpdateLayout();
    }

    private static void ExitTransientViewModes(MainWindow window)
    {
        if (GetBoolean(window, "IsReadModeActiveForTests"))
            Invoke(window, "ToggleReadModeForTests");
        if (GetBoolean(window, "HasMultiplePagesEditablePageSurfaceForTests"))
            Invoke(window, "ToggleMultiplePages");
        if (GetBoolean(window, "HasSideToSideEditablePageSurfaceForTests"))
            Invoke(window, "ToggleSideToSide");
        if (HasValue(window, "SplitEditorForTests"))
            Invoke(window, "ToggleSplitWindow");
    }

    private static bool GetBoolean(MainWindow window, string propertyName) =>
        (bool?)typeof(MainWindow)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(window) == true;

    private static bool HasValue(MainWindow window, string propertyName) =>
        typeof(MainWindow)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(window) is not null;

    private static void Invoke(MainWindow window, string methodName) =>
        typeof(MainWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes)!
            .Invoke(window, null);

    private sealed class DiscardUnsavedChangesMessageService : IUserMessageService
    {
        internal static readonly DiscardUnsavedChangesMessageService Instance = new();

        public void ShowError(string message, string title = "Error") { }
        public void ShowWarning(string message, string title = "Warning") { }
        public void ShowInfo(string message, string title = "Information") { }
        public bool AskYesNo(string message, string title = "Confirm") => false;
        public UserMessageResult ShowMessage(
            string message,
            string title,
            UserMessageButtons buttons,
            UserMessageIcon icon) => UserMessageResult.No;
    }
}
