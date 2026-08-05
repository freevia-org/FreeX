using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

public sealed record OptionsDialogInitialState(
    string RecentFilesCapText,
    string? SelectedFormat,
    string UiLanguage,
    IReadOnlyCollection<OptionsDialogToggleKind> CheckedToggles,
    IReadOnlyList<AutoCorrectReplacement> Replacements);

public sealed record OptionsDialogCommitPlan(
    bool ShouldApply,
    bool ShouldPersist,
    FreeWOptions? Result,
    OptionsDialogValidation? Validation);

/// <summary>
/// Owns the renderer-neutral lifetime of the paired FreeW options dialogs. Native hosts project
/// <see cref="Surface"/>, capture control values, and ask this session for enabled-state and commit plans.
/// </summary>
public sealed class OptionsDialogSession
{
    public OptionsDialogSession(FreeWOptions? options, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        InitialResult = options ?? new FreeWOptions();
        Surface = OptionsDialogPlanner.BuildSurface(InitialResult, SystemLanguageLabel(culture));

        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            Surface.AutoCorrect.ReplacementsText,
            out var replacements,
            out _);

        InitialState = new OptionsDialogInitialState(
            InitialResult.RecentFilesCap.ToString(culture),
            Surface.General.FormatChoices.FirstOrDefault()?.Extension,
            InitialResult.UiLanguage,
            Surface.AutoCorrect.Toggles
                .Concat([Surface.AutoFormat.MasterToggle])
                .Concat(Surface.AutoFormat.RuleToggles)
                .Where(toggle => toggle.IsChecked)
                .Select(toggle => toggle.Kind)
                .ToArray(),
            replacements.ToArray());
    }

    public FreeWOptions InitialResult { get; }

    public OptionsDialogSurfaceSpec Surface { get; }

    public OptionsDialogInitialState InitialState { get; }

    public OptionsDialogEnabledState PlanEnabledState(
        bool autoCorrectEnabled,
        bool replaceTextEnabled) =>
        OptionsDialogWorkflowPlanner.PlanEnabledState(autoCorrectEnabled, replaceTextEnabled);

    public OptionsDialogCommitPlan PlanAcceptance(OptionsDialogInput input)
    {
        if (!OptionsDialogWorkflowPlanner.TryBuildResult(input, out var result, out var validation))
            return new OptionsDialogCommitPlan(false, false, null, validation);

        return new OptionsDialogCommitPlan(true, true, result, null);
    }

    private static string SystemLanguageLabel(CultureInfo culture) =>
        string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;
}
