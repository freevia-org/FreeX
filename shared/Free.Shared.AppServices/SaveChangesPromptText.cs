using System.Globalization;

namespace Free.Shared.AppServices;

/// <summary>
/// Resolved text of the shared "save changes before ...?" dirty-gate prompt. The two message formats
/// take the document name as <c>{0}</c> and the action phrase as <c>{1}</c>; the quoted variant is the
/// Avalonia dialog wording (document name wrapped in double quotes), the plain one is the WPF message
/// box wording.
/// </summary>
public sealed record SaveChangesPromptTextValues(
    string MessageFormat,
    string QuotedMessageFormat,
    string SaveButton,
    string DontSaveButton,
    string CancelButton)
{
    public string FormatMessage(string displayName, string action) =>
        Format(MessageFormat, SaveChangesPromptTextResources.DefaultMessageFormat, displayName, action);

    public string FormatQuotedMessage(string displayName, string action) =>
        Format(QuotedMessageFormat, SaveChangesPromptTextResources.DefaultQuotedMessageFormat, displayName, action);

    // A translated format with a stray brace must not turn the dirty gate into a crash: fall back
    // to the English default, which is known to be well formed.
    private static string Format(string format, string fallbackFormat, string displayName, string action)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, displayName, action);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, fallbackFormat, displayName, action);
        }
    }
}

/// <summary>
/// Resource keys and English defaults for the shared dirty-gate prompt (the sentence plus the
/// Save / Don't save / Cancel buttons). Each sister app resolves them through its own neutral
/// catalog by passing its <c>UiText.Get</c>; a missing key falls back to the English default.
/// </summary>
public static class SaveChangesPromptTextResources
{
    public const string DefaultMessageFormat = "Do you want to save changes to {0} before {1}?";
    public const string DefaultQuotedMessageFormat = "Do you want to save changes to \"{0}\" before {1}?";

    public static ResourceTextDescriptor MessageFormat { get; } =
        new("SaveChanges_Prompt_Message_Format", DefaultMessageFormat);

    public static ResourceTextDescriptor QuotedMessageFormat { get; } =
        new("SaveChanges_Prompt_Message_Quoted_Format", DefaultQuotedMessageFormat);

    public static ResourceTextDescriptor SaveButton { get; } =
        new("SaveChanges_Prompt_Save_Button", "Save");

    public static ResourceTextDescriptor DontSaveButton { get; } =
        new("SaveChanges_Prompt_DontSave_Button", "Don't save");

    public static ResourceTextDescriptor CancelButton { get; } =
        new("SaveChanges_Prompt_Cancel_Button", "Cancel");

    public static IReadOnlyList<ResourceTextDescriptor> All { get; } =
        [MessageFormat, QuotedMessageFormat, SaveButton, DontSaveButton, CancelButton];

    public static IReadOnlyList<string> RequiredResourceKeys { get; } =
        All.Select(text => text.ResourceKey).ToArray();

    public static SaveChangesPromptTextValues Resolve(Func<string, string?>? getText = null) =>
        new(
            MessageFormat.Resolve(getText),
            QuotedMessageFormat.Resolve(getText),
            SaveButton.Resolve(getText),
            DontSaveButton.Resolve(getText),
            CancelButton.Resolve(getText));
}
