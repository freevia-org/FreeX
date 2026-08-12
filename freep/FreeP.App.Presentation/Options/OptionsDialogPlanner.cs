using Free.Shared.AppServices;
using Free.Shared.Shell;

namespace FreeP.App.Compositor;

/// <summary>
/// The portable, UI-free decision logic behind the FreeP options dialogs: parse + validate the
/// recent-files count from its text-box string, and assemble a normalized <see cref="FreePOptions"/> from
/// the dialog's raw inputs. Keeping this out of either host lets WPF and Avalonia share one policy, the
/// same way <c>FreeW.App.Presentation.Options.OptionsDialogPlanner</c> does for FreeW.
/// </summary>
public static class OptionsDialogPlanner
{
    // Portable WPF-authority metrics consumed by both desktop adapters.
    public const int DialogWidth = 380;
    public const int ContentMargin = 16;
    public const int ContentBottomMargin = 12;
    public const int ActionRowTopMargin = 8;
    public const int ActionRowBottomMargin = 12;
    public const int ActionButtonWidth = 84;

    public const string Title = "FreeP Options";
    public const string RecentFilesLabel = "Recent files to keep:";
    public const string DefaultSaveFormatLabel = "Default save format:";
    public const string UiLanguageLabel = "UI language:";

    /// <summary>
    /// Parses the recent-files-count text against <see cref="FreePOptions"/>'s valid range. Returns false
    /// when the text is not a whole number within
    /// [<see cref="FreePOptions.MinRecentFilesCap"/>, <see cref="FreePOptions.MaxRecentFilesCap"/>].
    /// </summary>
    public static bool TryParseRecentFilesCap(string? text, out int cap) =>
        ApplicationOptionsNormalizer.TryParseRecentFilesCap(text, out cap);

    public static OptionsDialogSurfaceSpec BuildSurface(FreePOptions? options, string systemLanguageLabel)
    {
        var source = options ?? new FreePOptions();
        var seed = new FreePOptions
        {
            RecentFilesCap = source.RecentFilesCap,
            DefaultSaveFormat = source.DefaultSaveFormat,
            UiLanguage = source.UiLanguage,
        };
        seed.Normalize();

        var languageHint = string.IsNullOrWhiteSpace(systemLanguageLabel)
            ? "Empty = follow the system culture."
            : $"Empty = follow the system culture (currently {systemLanguageLabel}).";

        return new OptionsDialogSurfaceSpec(
            Title,
            ShellStrings.Current.Ok,
            ShellStrings.Current.Cancel,
            RecentFilesLabel,
            DefaultSaveFormatLabel,
            UiLanguageLabel,
            languageHint,
            seed.RecentFilesCap,
            seed.UiLanguage,
            [
                new("Presentation (*.fxp)", FreePOptions.FxpDefaultFormat),
            ]);
    }

    /// <summary>
    /// Builds the normalized options the dialog hands back on OK. <paramref name="format"/> falls back to
    /// the single shipped <c>.fxp</c> format when null/blank; <see cref="FreePOptions.Normalize"/> clamps
    /// and trims everything so the result is already store-ready.
    /// </summary>
    public static FreePOptions BuildResult(int recentFilesCap, string? format, string? uiLanguage)
        => BasicApplicationOptionsDialogSession<FreePOptions>.BuildResult(
            recentFilesCap,
            format,
            uiLanguage,
            FreePOptions.FxpDefaultFormat);
}

public sealed record OptionsDialogSurfaceSpec(
    string Title,
    string AcceptLabel,
    string CancelLabel,
    string RecentFilesLabel,
    string DefaultSaveFormatLabel,
    string UiLanguageLabel,
    string UiLanguageHint,
    int RecentFilesCap,
    string UiLanguage,
    IReadOnlyList<OptionsDialogFormatChoice> FormatChoices);

public sealed record OptionsDialogFormatChoice(string Label, string Extension)
{
    public override string ToString() => Label;
}
