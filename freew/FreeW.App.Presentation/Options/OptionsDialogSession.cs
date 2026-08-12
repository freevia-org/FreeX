using System.Globalization;
using Free.Shared.AppServices;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Options;

public sealed record OptionsDialogInitialState(
    string RecentFilesCapText,
    string? SelectedFormat,
    string UiLanguage,
    IReadOnlyCollection<OptionsDialogToggleKind> CheckedToggles,
    IReadOnlyList<AutoCorrectReplacement> Replacements);

/// <summary>
/// Owns the renderer-neutral lifetime of the paired FreeW options dialogs. Native hosts project
/// <see cref="Surface"/>, capture control values, and ask this session for enabled-state and commit plans.
/// </summary>
public sealed class OptionsDialogSession
{
    private readonly BasicApplicationOptionsDialogSession<FreeWOptions> _basicSession;

    public OptionsDialogSession(FreeWOptions? options, CultureInfo culture)
    {
        _basicSession = new BasicApplicationOptionsDialogSession<FreeWOptions>(
            options,
            culture,
            FreeWOptions.DocxDefaultFormat,
            OptionsDialogWorkflowPlanner.RecentFilesCapValidationMessage);
        Surface = OptionsDialogPlanner.BuildSurface(
            _basicSession.InitialResult,
            _basicSession.SystemLanguageLabel);

        OptionsDialogPlanner.TryParseAutoCorrectReplacements(
            Surface.AutoCorrect.ReplacementsText,
            out var replacements,
            out _);

        InitialState = new OptionsDialogInitialState(
            _basicSession.InitialState.RecentFilesCapText,
            _basicSession.InitialState.SelectedFormat,
            _basicSession.InitialState.UiLanguage,
            Surface.AutoCorrect.Toggles
                .Concat([Surface.AutoFormat.MasterToggle])
                .Concat(Surface.AutoFormat.RuleToggles)
                .Where(toggle => toggle.IsChecked)
                .Select(toggle => toggle.Kind)
                .ToArray(),
            replacements.ToArray());
    }

    public FreeWOptions InitialResult => _basicSession.InitialResult;

    public OptionsDialogSurfaceSpec Surface { get; }

    public OptionsDialogInitialState InitialState { get; }

    public OptionsDialogEnabledState PlanEnabledState(
        bool autoCorrectEnabled,
        bool replaceTextEnabled) =>
        OptionsDialogWorkflowPlanner.PlanEnabledState(autoCorrectEnabled, replaceTextEnabled);

    public BasicApplicationOptionsDialogCommitPlan<FreeWOptions> PlanAcceptance(OptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var basicPlan = _basicSession.PlanAcceptance(new BasicApplicationOptionsDialogInput(
            input.RecentFilesCapText,
            input.Format,
            input.UiLanguage));
        if (!basicPlan.ShouldApply)
            return basicPlan;

        OptionsDialogWorkflowPlanner.ApplyExtensions(basicPlan.Result!, input);
        return basicPlan;
    }
}
