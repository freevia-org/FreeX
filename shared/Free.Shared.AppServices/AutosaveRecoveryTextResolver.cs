namespace Free.Shared.AppServices;

public sealed record AutosaveRecoveryTextDefaults(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat);

public sealed record AutosaveRecoveryTextValues(
    string Title,
    string RecoverButton,
    string SkipButton,
    string NoDocumentsMessage,
    string FailureMessageFormat);

/// <summary>Resolves the common autosave recovery chrome shared by sister apps.</summary>
public sealed class AutosaveRecoveryTextResolver
{
    private readonly ResourceTextDescriptor[] _texts;

    public AutosaveRecoveryTextResolver(AutosaveRecoveryTextDefaults defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        _texts =
        [
            new("Autosave_Recovery_Title", defaults.Title),
            new("Autosave_Recovery_Recover_Button", defaults.RecoverButton),
            new("Autosave_Recovery_Skip_Button", defaults.SkipButton),
            new("Autosave_Recovery_None_Message", defaults.NoDocumentsMessage),
            new("Autosave_Recovery_Failure_Message_Format", defaults.FailureMessageFormat),
        ];
    }

    public IReadOnlyList<string> RequiredResourceKeys =>
        _texts.Select(text => text.ResourceKey).ToArray();

    public AutosaveRecoveryTextValues Resolve(Func<string, string?>? getText = null) =>
        new(
            _texts[0].Resolve(getText),
            _texts[1].Resolve(getText),
            _texts[2].Resolve(getText),
            _texts[3].Resolve(getText),
            _texts[4].Resolve(getText));
}
