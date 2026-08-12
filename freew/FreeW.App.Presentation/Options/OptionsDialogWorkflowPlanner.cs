using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

public sealed record OptionsDialogReplacementInput(string? Replace, string? With);

public sealed record OptionsDialogInput(
    string? RecentFilesCapText,
    string? Format,
    string? UiLanguage,
    IReadOnlyCollection<OptionsDialogToggleKind> CheckedToggles,
    IReadOnlyCollection<OptionsDialogReplacementInput> Replacements);

public sealed record OptionsDialogEnabledState(
    bool AutoFormatRulesEnabled,
    bool ReplacementsEnabled);

/// <summary>
/// Owns the neutral acceptance and dependent-control decisions for both FreeW options dialogs.
/// Renderers only capture native control values and apply the returned result or validation target.
/// </summary>
public static class OptionsDialogWorkflowPlanner
{
    public static string RecentFilesCapValidationMessage =>
        $"Enter a whole number between {FreeWOptions.MinRecentFilesCap} and {FreeWOptions.MaxRecentFilesCap} for the recent-files count.";

    public static OptionsDialogEnabledState PlanEnabledState(
        bool autoCorrectEnabled,
        bool replaceTextEnabled) =>
        new(autoCorrectEnabled, replaceTextEnabled);

    public static FreeWOptions ApplyExtensions(
        FreeWOptions result,
        OptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(input);

        var checkedToggles = input.CheckedToggles ?? [];
        bool IsChecked(OptionsDialogToggleKind kind) => checkedToggles.Contains(kind);

        var autoFormat = new AutoFormatOptions
        {
            SmartQuotes = IsChecked(OptionsDialogToggleKind.SmartQuotes),
            Dashes = IsChecked(OptionsDialogToggleKind.Dashes),
            Ellipsis = IsChecked(OptionsDialogToggleKind.Ellipsis),
            Symbols = IsChecked(OptionsDialogToggleKind.Symbols),
            Capitalization = IsChecked(OptionsDialogToggleKind.Capitalization),
            BulletedLists = IsChecked(OptionsDialogToggleKind.BulletedLists),
            NumberedLists = IsChecked(OptionsDialogToggleKind.NumberedLists),
            Ordinals = IsChecked(OptionsDialogToggleKind.Ordinals),
            Fractions = IsChecked(OptionsDialogToggleKind.Fractions),
            Hyperlinks = IsChecked(OptionsDialogToggleKind.Hyperlinks),
        };
        var autoCorrect = new AutoCorrectOptions
        {
            CorrectTwoInitialCapitals = IsChecked(OptionsDialogToggleKind.CorrectTwoInitialCapitals),
            CapitalizeDayNames = IsChecked(OptionsDialogToggleKind.CapitalizeDayNames),
            ReplaceText = IsChecked(OptionsDialogToggleKind.ReplaceText),
            Replacements = (input.Replacements ?? [])
                .Where(row => !string.IsNullOrWhiteSpace(row.Replace) && !string.IsNullOrEmpty(row.With))
                .Select(row => new AutoCorrectReplacement(row.Replace!.Trim(), row.With!))
                .ToList(),
        };

        result.AutoCorrectEnabled = IsChecked(OptionsDialogToggleKind.AutoCorrectEnabled);
        result.AutoFormat = autoFormat;
        result.AutoCorrect = autoCorrect;
        result.Normalize();
        return result;
    }
}
