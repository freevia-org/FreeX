using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

/// <summary>
/// The portable, UI-free decision logic behind the FreeW options dialogs: parse + validate the
/// recent-files count from its text-box string, and assemble a normalized <see cref="FreeWOptions"/> from
/// the dialog's raw inputs. Keeping this out of either host lets WPF and Avalonia share one policy.
/// </summary>
public static class OptionsDialogPlanner
{
    /// <summary>
    /// Parses the recent-files-count text against <see cref="FreeWOptions"/>'s valid range. Returns false
    /// when the text is not a whole number within
    /// [<see cref="FreeWOptions.MinRecentFilesCap"/>, <see cref="FreeWOptions.MaxRecentFilesCap"/>].
    /// </summary>
    public static bool TryParseRecentFilesCap(string? text, out int cap) =>
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(text, out cap);

    /// <summary>
    /// Builds the normalized options the dialog hands back on OK. <paramref name="format"/> falls back to
    /// the single shipped <c>.docx</c> format when null/blank; <see cref="FreeWOptions.Normalize"/> clamps
    /// and trims everything so the result is already store-ready.
    /// </summary>
    public static FreeWOptions BuildResult(
        int recentFilesCap,
        string? format,
        string? uiLanguage,
        bool autoCorrectEnabled,
        AutoFormatOptions autoFormat,
        AutoCorrectOptions autoCorrect)
    {
        var result = new FreeWOptions
        {
            RecentFilesCap = recentFilesCap,
            DefaultSaveFormat = string.IsNullOrWhiteSpace(format) ? FreeWOptions.DocxDefaultFormat : format!,
            UiLanguage = uiLanguage ?? FreeWOptions.SystemDefaultLanguage,
            AutoCorrectEnabled = autoCorrectEnabled,
            AutoFormat = autoFormat ?? AutoFormatOptions.Default,
            AutoCorrect = autoCorrect ?? AutoCorrectOptions.Default,
        };
        result.Normalize();
        return result;
    }
}
