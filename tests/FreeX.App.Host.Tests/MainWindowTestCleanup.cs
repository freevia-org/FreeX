using System.Reflection;
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
}

internal static class MainWindowTestCleanup
{
    private static readonly FieldInfo SuppressClosePromptField =
        typeof(MainWindow).GetField("_suppressClosePrompt", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(nameof(MainWindow), "_suppressClosePrompt");

    public static void CloseWithoutSavePrompt(MainWindow window)
    {
        SuppressClosePromptField.SetValue(window, true);
        window.Close();
    }
}
