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
    // Portable WPF-authority metrics consumed by both desktop adapters.
    public const int DialogWidth = 460;
    public const int TabMargin = 14;
    public const int ContentMargin = 16;
    public const int ContentBottomMargin = 12;
    public const int ActionRowTopMargin = 8;
    public const int ActionRowBottomMargin = 12;
    public const int ActionButtonWidth = 84;
    public const int ReplacementTableHeight = 180;
    public const int ToggleTopMargin = 4;
    public const int SectionHeaderTopMargin = 12;
    public const int HelpTextFontSize = 11;
    // Retained paired evidence paints the WPF AutoCorrect pane one pixel narrower than the
    // Avalonia template's otherwise equivalent client surface.
    public const int AutoCorrectTabPaneRightInset = 1;

    public const string Title = "FreeW Options";
    public const string GeneralTabHeader = "General";
    public const string AutoCorrectTabHeader = "AutoCorrect";
    public const string AutoFormatTabHeader = "AutoFormat As You Type";
    public const string RecentFilesLabel = "Recent files to keep:";
    public const string DefaultSaveFormatLabel = "Default save format:";
    public const string UiLanguageLabel = "UI language:";
    public const string AutoFormatSectionLabel = "Apply as you type:";
    public const string ReplacementsLabel = "Replace text as you type:";
    public const string ReplacementsHelpText =
        "Enter one replacement per line as 'replace => with'. Blank lines are ignored.";
    public const string ReplacementsValidationMessage =
        "Enter replacements as 'replace => with', one per line.";

    /// <summary>
    /// Parses the recent-files-count text against <see cref="FreeWOptions"/>'s valid range. Returns false
    /// when the text is not a whole number within
    /// [<see cref="FreeWOptions.MinRecentFilesCap"/>, <see cref="FreeWOptions.MaxRecentFilesCap"/>].
    /// </summary>
    public static bool TryParseRecentFilesCap(string? text, out int cap) =>
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(text, out cap);

    public static OptionsDialogSurfaceSpec BuildSurface(FreeWOptions? options, string systemLanguageLabel)
    {
        var source = options ?? new FreeWOptions();
        var seed = new FreeWOptions
        {
            RecentFilesCap = source.RecentFilesCap,
            DefaultSaveFormat = source.DefaultSaveFormat,
            UiLanguage = source.UiLanguage,
            AutoCorrectEnabled = source.AutoCorrectEnabled,
            AutoFormat = source.AutoFormat ?? AutoFormatOptions.Default,
            AutoCorrect = CopyAutoCorrect(source.AutoCorrect),
        };
        seed.Normalize();
        var autoFormat = seed.AutoFormat ?? AutoFormatOptions.Default;
        var autoCorrect = seed.AutoCorrect ?? AutoCorrectOptions.Default;
        var languageHint = string.IsNullOrWhiteSpace(systemLanguageLabel)
            ? "Empty = follow the system culture."
            : $"Empty = follow the system culture (currently {systemLanguageLabel}).";

        return new OptionsDialogSurfaceSpec(
            Title,
            [
                new(GeneralTabHeader, "OptionsGeneralTab"),
                new(AutoCorrectTabHeader, "OptionsAutoCorrectTab"),
                new(AutoFormatTabHeader, "OptionsAutoFormatTab"),
            ],
            new OptionsDialogGeneralSurfaceSpec(
                RecentFilesLabel,
                DefaultSaveFormatLabel,
                UiLanguageLabel,
                languageHint,
                [
                    new("Word Document (*.docx)", FreeWOptions.DocxDefaultFormat),
                ]),
            new OptionsDialogAutoCorrectSurfaceSpec(
                AutoCorrectTabHeader,
                [
                    new(OptionsDialogToggleKind.CorrectTwoInitialCapitals, "Correct TWo INitial CApitals", autoCorrect.CorrectTwoInitialCapitals),
                    new(OptionsDialogToggleKind.CapitalizeDayNames, "Capitalize names of days", autoCorrect.CapitalizeDayNames),
                    new(OptionsDialogToggleKind.ReplaceText, "Replace text as you type", autoCorrect.ReplaceText),
                ],
                ReplacementsLabel,
                FormatAutoCorrectReplacements(autoCorrect.Replacements),
                ReplacementsHelpText,
                ReplacementsValidationMessage,
                [
                    new(OptionsDialogReplacementFieldKind.Replace, "Replace", 1),
                    new(OptionsDialogReplacementFieldKind.With, "With", 2),
                ]),
            new OptionsDialogAutoFormatSurfaceSpec(
                AutoFormatTabHeader,
                new OptionsDialogToggleSpec(
                    OptionsDialogToggleKind.AutoCorrectEnabled,
                    "Enable AutoCorrect (smart typing) as you type",
                    seed.AutoCorrectEnabled),
                AutoFormatSectionLabel,
                [
                    new(OptionsDialogToggleKind.SmartQuotes, "Straight quotes with smart quotes (\" \" and ' ')", autoFormat.SmartQuotes),
                    new(OptionsDialogToggleKind.Dashes, "Hyphens (--) with dash", autoFormat.Dashes),
                    new(OptionsDialogToggleKind.Ellipsis, "Three periods (...) with ellipsis", autoFormat.Ellipsis),
                    new(OptionsDialogToggleKind.Symbols, "Symbols ( (c) (r) (tm) ) with copyright, registered, and trademark symbols", autoFormat.Symbols),
                    new(OptionsDialogToggleKind.Capitalization, "Capitalize first letter of sentences", autoFormat.Capitalization),
                    new(OptionsDialogToggleKind.BulletedLists, "Automatic bulleted lists", autoFormat.BulletedLists),
                    new(OptionsDialogToggleKind.NumberedLists, "Automatic numbered lists", autoFormat.NumberedLists),
                    new(OptionsDialogToggleKind.Ordinals, "Ordinals (1st) with superscript", autoFormat.Ordinals),
                    new(OptionsDialogToggleKind.Fractions, "Fractions (1/2) with fraction character", autoFormat.Fractions),
                    new(OptionsDialogToggleKind.Hyperlinks, "Internet and network paths with hyperlinks", autoFormat.Hyperlinks),
                ]));
    }

    private static AutoCorrectOptions CopyAutoCorrect(AutoCorrectOptions? source)
    {
        source ??= AutoCorrectOptions.Default;
        return new AutoCorrectOptions
        {
            CorrectTwoInitialCapitals = source.CorrectTwoInitialCapitals,
            CapitalizeDayNames = source.CapitalizeDayNames,
            ReplaceText = source.ReplaceText,
            Replacements = source.Replacements?
                .Select(replacement => new AutoCorrectReplacement(replacement.Replace, replacement.With))
                .ToList() ?? [],
        };
    }

    public static string FormatAutoCorrectReplacements(IEnumerable<AutoCorrectReplacement>? replacements)
    {
        if (replacements is null)
            return string.Empty;

        return string.Join(
            Environment.NewLine,
            replacements
                .Where(replacement =>
                    !string.IsNullOrWhiteSpace(replacement.Replace) &&
                    !string.IsNullOrEmpty(replacement.With))
                .Select(replacement => $"{replacement.Replace.Trim()} => {replacement.With}"));
    }

    public static bool TryParseAutoCorrectReplacements(
        string? text,
        out IReadOnlyList<AutoCorrectReplacement> replacements,
        out string? errorMessage)
    {
        var rows = new List<AutoCorrectReplacement>();
        foreach (var rawLine in (text ?? string.Empty).Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var separatorIndex = line.IndexOf("=>", StringComparison.Ordinal);
            if (separatorIndex < 0)
                separatorIndex = line.IndexOf('\t');

            if (separatorIndex < 0)
            {
                replacements = [];
                errorMessage = ReplacementsValidationMessage;
                return false;
            }

            var separatorLength = line[separatorIndex] == '\t' ? 1 : 2;
            var replace = line[..separatorIndex].Trim();
            var with = line[(separatorIndex + separatorLength)..].Trim();
            if (replace.Length == 0 || with.Length == 0)
            {
                replacements = [];
                errorMessage = ReplacementsValidationMessage;
                return false;
            }

            rows.Add(new AutoCorrectReplacement(replace, with));
        }

        replacements = rows;
        errorMessage = null;
        return true;
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

public sealed record OptionsDialogSurfaceSpec(
    string Title,
    IReadOnlyList<OptionsDialogTabSpec> Tabs,
    OptionsDialogGeneralSurfaceSpec General,
    OptionsDialogAutoCorrectSurfaceSpec AutoCorrect,
    OptionsDialogAutoFormatSurfaceSpec AutoFormat);

public sealed record OptionsDialogTabSpec(string Header, string AutomationId);

public sealed record OptionsDialogGeneralSurfaceSpec(
    string RecentFilesLabel,
    string DefaultSaveFormatLabel,
    string UiLanguageLabel,
    string UiLanguageHint,
    IReadOnlyList<OptionsDialogFormatChoice> FormatChoices)
{
    public IReadOnlyList<OptionsDialogGeneralFieldSpec> Fields { get; } =
    [
        new(OptionsDialogGeneralFieldKind.RecentFilesCap, RecentFilesLabel),
        new(OptionsDialogGeneralFieldKind.DefaultSaveFormat, DefaultSaveFormatLabel),
        new(OptionsDialogGeneralFieldKind.UiLanguage, UiLanguageLabel, UiLanguageHint),
    ];
}

public enum OptionsDialogGeneralFieldKind
{
    RecentFilesCap,
    DefaultSaveFormat,
    UiLanguage,
}

public sealed record OptionsDialogGeneralFieldSpec(
    OptionsDialogGeneralFieldKind Kind,
    string Label,
    string? Hint = null);

public sealed record OptionsDialogFormatChoice(string Label, string Extension)
{
    public override string ToString() => Label;
}

public sealed record OptionsDialogAutoCorrectSurfaceSpec(
    string Header,
    IReadOnlyList<OptionsDialogToggleSpec> Toggles,
    string ReplacementsLabel,
    string ReplacementsText,
    string ReplacementsHelpText,
    string ReplacementsValidationMessage,
    IReadOnlyList<OptionsDialogReplacementColumnSpec> ReplacementColumns);

public enum OptionsDialogReplacementFieldKind
{
    Replace,
    With,
}

public sealed record OptionsDialogReplacementColumnSpec(
    OptionsDialogReplacementFieldKind Kind,
    string Header,
    int WidthWeight);

public sealed record OptionsDialogAutoFormatSurfaceSpec(
    string Header,
    OptionsDialogToggleSpec MasterToggle,
    string RuleSectionLabel,
    IReadOnlyList<OptionsDialogToggleSpec> RuleToggles);

public sealed record OptionsDialogToggleSpec(
    OptionsDialogToggleKind Kind,
    string Label,
    bool IsChecked);

public enum OptionsDialogToggleKind
{
    AutoCorrectEnabled,
    SmartQuotes,
    Dashes,
    Ellipsis,
    Symbols,
    Capitalization,
    BulletedLists,
    NumberedLists,
    Ordinals,
    Fractions,
    Hyperlinks,
    CorrectTwoInitialCapitals,
    CapitalizeDayNames,
    ReplaceText
}
