using FreeX.App.UI;

namespace FreeX.App.Host.Tests;

/// <summary>
/// No-op implementation of <see cref="IUserMessageService"/> for tests
/// that construct <see cref="MainWindow"/> directly and do not care about
/// message dialogs being shown.
/// </summary>
internal sealed class NullUserMessageService : IUserMessageService
{
    public static readonly NullUserMessageService Instance = new();
    public void ShowError(string message, string title = "Error") { }
    public void ShowWarning(string message, string title = "Warning") { }
    public void ShowInfo(string message, string title = "Information") { }
    public bool AskYesNo(string message, string title = "Confirm") => false;
    public UserMessageResult ShowMessage(
        string message,
        string title,
        UserMessageButtons buttons,
        UserMessageIcon icon) =>
        UserMessageResult.Ok;
}

internal static class MainWindowTestCleanup
{
    /// <summary>
    /// Suppresses the save-changes prompt and then closes the window.
    /// Uses the internal <see cref="MainWindow.SuppressNextClosePrompt"/> method
    /// instead of reflection so it remains type-safe across refactors.
    /// </summary>
    public static void CloseWithoutSavePrompt(MainWindow window)
    {
        window.SuppressNextClosePrompt();
        window.Close();
    }
}
