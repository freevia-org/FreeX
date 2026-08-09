using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

public sealed record FreeWEditorTypingOptionsPlan(
    bool AutoCorrectEnabled,
    AutoFormatOptions AutoFormat,
    AutoCorrectOptions AutoCorrect);

/// <summary>
/// Owns the mutable application-options instance consumed by a running FreeW shell and projects the
/// editor settings that native hosts apply to their platform-specific work area.
/// </summary>
public sealed class FreeWOptionsRuntimeSession
{
    public FreeWOptionsRuntimeSession(FreeWOptions liveOptions)
    {
        LiveOptions = liveOptions ?? throw new ArgumentNullException(nameof(liveOptions));
        LiveOptions.Normalize();
    }

    public FreeWOptions LiveOptions { get; }

    public FreeWEditorTypingOptionsPlan EditorTypingOptions => new(
        LiveOptions.AutoCorrectEnabled,
        LiveOptions.AutoFormat,
        LiveOptions.AutoCorrect);

    public FreeWEditorTypingOptionsPlan Apply(FreeWOptions editedOptions)
    {
        ArgumentNullException.ThrowIfNull(editedOptions);

        LiveOptions.RecentFilesCap = editedOptions.RecentFilesCap;
        LiveOptions.DefaultSaveFormat = editedOptions.DefaultSaveFormat;
        LiveOptions.UiLanguage = editedOptions.UiLanguage;
        LiveOptions.AutoCorrectEnabled = editedOptions.AutoCorrectEnabled;
        LiveOptions.AutoFormat = editedOptions.AutoFormat;
        LiveOptions.AutoCorrect = editedOptions.AutoCorrect;
        LiveOptions.Normalize();

        return EditorTypingOptions;
    }
}
