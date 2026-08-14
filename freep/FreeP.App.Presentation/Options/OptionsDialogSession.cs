using System.Globalization;
using Free.Shared.AppServices;
using FreeP.App.Localization;

namespace FreeP.App.Compositor;

/// <summary>
/// Renderer-neutral lifetime and acceptance policy for the paired FreeP options dialogs.
/// </summary>
public sealed class OptionsDialogSession
{
    private readonly BasicApplicationOptionsDialogSession<FreePOptions> _basicSession;

    public static string RecentFilesCapValidationMessage =>
        Loc.Format(
            "Options_RecentFilesCapValidation",
            FreePOptions.MinRecentFilesCap,
            FreePOptions.MaxRecentFilesCap);

    public OptionsDialogSession(FreePOptions? options, CultureInfo culture)
    {
        _basicSession = new BasicApplicationOptionsDialogSession<FreePOptions>(
            options,
            culture,
            FreePOptions.FxpDefaultFormat,
            RecentFilesCapValidationMessage);
        Surface = OptionsDialogPlanner.BuildSurface(
            _basicSession.InitialResult,
            _basicSession.SystemLanguageLabel);
    }

    public FreePOptions InitialResult => _basicSession.InitialResult;

    public OptionsDialogSurfaceSpec Surface { get; }

    public BasicApplicationOptionsDialogInitialState InitialState => _basicSession.InitialState;

    /// <summary>
    /// FreeP's Options dialog edits exactly the shared basic fields, so it captures the shared
    /// <see cref="BasicApplicationOptionsDialogInput"/> directly instead of repeating it as a FreeP record.
    /// </summary>
    public BasicApplicationOptionsDialogCommitPlan<FreePOptions> PlanAcceptance(
        BasicApplicationOptionsDialogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return _basicSession.PlanAcceptance(input);
    }
}
