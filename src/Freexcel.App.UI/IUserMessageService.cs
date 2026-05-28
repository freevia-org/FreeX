namespace Freexcel.App.UI;

/// <summary>
/// Abstracts modal message dialogs so that callers remain testable
/// without triggering real WPF MessageBox windows.
/// </summary>
public interface IUserMessageService
{
    void ShowError(string message, string title = "Error");
    void ShowWarning(string message, string title = "Warning");
    void ShowInfo(string message, string title = "Information");
    bool AskYesNo(string message, string title = "Confirm");
}
