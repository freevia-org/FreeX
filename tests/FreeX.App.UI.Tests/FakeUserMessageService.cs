using System.Collections.Generic;
using FreeX.App.UI;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Test double for <see cref="IUserMessageService"/> that records every call
/// so tests can assert on which messages were shown.
/// </summary>
public sealed class FakeUserMessageService : IUserMessageService
{
    public record MessageRecord(string Kind, string Message, string Title);

    public List<MessageRecord> Calls { get; } = [];

    /// <summary>Controls the value returned by <see cref="AskYesNo"/>.</summary>
    public bool YesNoAnswer { get; set; } = true;

    public void ShowError(string message, string title = "Error") =>
        Calls.Add(new MessageRecord("Error", message, title));

    public void ShowWarning(string message, string title = "Warning") =>
        Calls.Add(new MessageRecord("Warning", message, title));

    public void ShowInfo(string message, string title = "Information") =>
        Calls.Add(new MessageRecord("Info", message, title));

    public bool AskYesNo(string message, string title = "Confirm")
    {
        Calls.Add(new MessageRecord("YesNo", message, title));
        return YesNoAnswer;
    }
}
