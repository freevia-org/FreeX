using System.Reflection;

namespace Freexcel.App.Host.Tests;

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
