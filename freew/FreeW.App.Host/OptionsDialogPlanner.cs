using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Host;

/// <summary>
/// The portable, WPF-free decision logic behind <see cref="OptionsDialog"/>: parse + validate the
/// recent-files count from its text-box string, and assemble a normalized <see cref="FreeWOptions"/> from
/// the dialog's raw inputs. Keeping this off the dialog (the FreeX "*DialogPlanner" pattern) lets it be
/// unit-tested headlessly and reused if the same settings surface anywhere else.
/// </summary>
internal static class OptionsDialogPlanner
{
    /// <summary>
    /// Parses the recent-files-count text against <see cref="FreeWOptions"/>'s valid range. Returns false
    /// (so the dialog can warn) when the text is not a whole number within
    /// [<see cref="FreeWOptions.MinRecentFilesCap"/>, <see cref="FreeWOptions.MaxRecentFilesCap"/>].
    /// </summary>
    public static bool TryParseRecentFilesCap(string? text, out int cap)
    {
        if (int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out cap)
            && cap >= FreeWOptions.MinRecentFilesCap
            && cap <= FreeWOptions.MaxRecentFilesCap)
        {
            return true;
        }

        cap = 0;
        return false;
    }

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
        AutoFormatOptions autoFormat)
    {
        var result = new FreeWOptions
        {
            RecentFilesCap = recentFilesCap,
            DefaultSaveFormat = string.IsNullOrWhiteSpace(format) ? FreeWOptions.DocxDefaultFormat : format!,
            UiLanguage = uiLanguage ?? FreeWOptions.SystemDefaultLanguage,
            AutoCorrectEnabled = autoCorrectEnabled,
            AutoFormat = autoFormat ?? AutoFormatOptions.Default,
        };
        result.Normalize();
        return result;
    }
}
