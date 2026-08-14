using Free.Shared.AppServices;
using Free.Shared.Shell;
using FreeP.App.Localization;

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

    public static string Title => Loc.Get("Options_Title");
    public static string RecentFilesLabel => Loc.Get("Options_RecentFilesLabel");
    public static string DefaultSaveFormatLabel => Loc.Get("Options_DefaultSaveFormatLabel");
    public static string UiLanguageLabel => Loc.Get("Options_UiLanguageLabel");

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
        var seed = BasicApplicationOptionsDialogSession<FreePOptions>.BuildResult(
            source.RecentFilesCap,
            source.DefaultSaveFormat,
            source.UiLanguage,
            FreePOptions.FxpDefaultFormat);

        return new OptionsDialogSurfaceSpec(
            Title,
            ShellStrings.Current.Ok,
            ShellStrings.Current.Cancel,
            BasicApplicationOptionsSurfacePlanner.BuildGeneral(
                RecentFilesLabel,
                DefaultSaveFormatLabel,
                UiLanguageLabel,
                systemLanguageLabel,
                Loc.Get("Options_UiLanguageSystemHint"),
                Loc.Get("Options_UiLanguageCurrentHint"),
                [
                    new(Loc.Get("Options_PresentationFormat"), FreePOptions.FxpDefaultFormat),
                ]),
            seed.RecentFilesCap,
            seed.UiLanguage);
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

/// <summary>
/// FreeP's Options surface. The labelled field schema, the language-hint rule, and the format-choice
/// record are the shared basic-options core (<see cref="BasicApplicationOptionsGeneralSpec"/>) because
/// FreeW makes the identical decisions; the dialog chrome (title and button labels) and the seeded values
/// stay here. The per-field members forward to <see cref="General"/> so the renderers keep reading one
/// flat surface.
/// </summary>
public sealed record OptionsDialogSurfaceSpec(
    string Title,
    string AcceptLabel,
    string CancelLabel,
    BasicApplicationOptionsGeneralSpec General,
    int RecentFilesCap,
    string UiLanguage)
{
    public string RecentFilesLabel => General.RecentFilesLabel;

    public string DefaultSaveFormatLabel => General.DefaultSaveFormatLabel;

    public string UiLanguageLabel => General.UiLanguageLabel;

    public string UiLanguageHint => General.UiLanguageHint;

    public IReadOnlyList<ApplicationOptionsFormatChoice> FormatChoices => General.FormatChoices;
}
