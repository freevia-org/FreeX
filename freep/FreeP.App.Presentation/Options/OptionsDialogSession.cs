using System.Globalization;

namespace FreeP.App.Compositor;

public sealed record OptionsDialogInput(
    string? RecentFilesCapText,
    string? Format,
    string? UiLanguage);

public enum OptionsDialogValidationTarget
{
    RecentFilesCap,
}

public sealed record OptionsDialogValidation(
    OptionsDialogValidationTarget Target,
    string Message);

public sealed record OptionsDialogInitialState(
    string RecentFilesCapText,
    string? SelectedFormat,
    string UiLanguage);

public sealed record OptionsDialogCommitPlan(
    bool ShouldApply,
    bool ShouldPersist,
    FreePOptions? Result,
    OptionsDialogValidation? Validation);

/// <summary>
/// Renderer-neutral lifetime and acceptance policy for the paired FreeP options dialogs.
/// </summary>
public sealed class OptionsDialogSession
{
    public static string RecentFilesCapValidationMessage =>
        $"Enter a whole number between {FreePOptions.MinRecentFilesCap} and {FreePOptions.MaxRecentFilesCap}.";

    public OptionsDialogSession(FreePOptions? options, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        InitialResult = options ?? new FreePOptions();
        Surface = OptionsDialogPlanner.BuildSurface(InitialResult, SystemLanguageLabel(culture));
        InitialState = new OptionsDialogInitialState(
            Surface.RecentFilesCap.ToString(culture),
            Surface.FormatChoices.FirstOrDefault()?.Extension,
            Surface.UiLanguage);
    }

    public FreePOptions InitialResult { get; }

    public OptionsDialogSurfaceSpec Surface { get; }

    public OptionsDialogInitialState InitialState { get; }

    public OptionsDialogCommitPlan PlanAcceptance(OptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!OptionsDialogPlanner.TryParseRecentFilesCap(input.RecentFilesCapText, out var cap))
        {
            return new OptionsDialogCommitPlan(
                ShouldApply: false,
                ShouldPersist: false,
                Result: null,
                Validation: new OptionsDialogValidation(
                    OptionsDialogValidationTarget.RecentFilesCap,
                    RecentFilesCapValidationMessage));
        }

        return new OptionsDialogCommitPlan(
            ShouldApply: true,
            ShouldPersist: true,
            Result: OptionsDialogPlanner.BuildResult(cap, input.Format, input.UiLanguage),
            Validation: null);
    }

    private static string SystemLanguageLabel(CultureInfo culture) =>
        string.IsNullOrEmpty(culture.Name) ? "invariant" : culture.Name;
}
